public interface IExcelReader
{
    /// <summary>
    /// Reads the specified sheet (or first if null/empty) from inputStream into a DataTable.
    /// </summary>
    Task<DataTable> ReadAsync(Stream inputStream, string? sheetName, CancellationToken ct);
}
using ExcelDataReader;
using Microsoft.Extensions.Options;

public class ExcelDataReaderService : IExcelReader
{
    private readonly ExcelImportOptions _opts;
    private readonly ILogger<ExcelDataReaderService> _logger;

    public ExcelDataReaderService(
        IOptions<ExcelImportOptions> opts,
        ILogger<ExcelDataReaderService> logger)
    {
        _opts   = opts.Value;
        _logger = logger;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public Task<DataTable> ReadAsync(Stream inputStream, string? sheetName, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            _logger.LogInformation("Starting to read Excel stream (sheet: {Sheet})", sheetName ?? "<first>");
            
            using var reader = ExcelReaderFactory.CreateReader(inputStream);
            var config = new ExcelDataSetConfiguration
            {
                ConfigureDataTable = _ => new ExcelDataTableConfiguration
                {
                    UseHeaderRow = _opts.UseHeaderRow
                }
            };
            var ds = reader.AsDataSet(config, ct);

            DataTable? table;
            if (string.IsNullOrWhiteSpace(sheetName))
            {
                table = ds.Tables.Count > 0
                    ? ds.Tables[0]
                    : throw new InvalidOperationException("No worksheets found in Excel.");
            }
            else
            {
                table = ds.Tables[sheetName!]
                     ?? throw new ArgumentException($"Worksheet '{sheetName}' not found.");
            }

            _logger.LogInformation("Read {RowCount} rows and {ColCount} columns", table.Rows.Count, table.Columns.Count);
            return table;
        }, ct);
    }
}
public interface IBulkInserter
{
    Task BulkInsertAsync(
        DataTable table,
        string stagingTable,
        IDictionary<string,string>? columnMappings,
        CancellationToken ct);
}
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

public class SqlBulkInserter : IBulkInserter
{
    private readonly string              _connString;
    private readonly ExcelImportOptions  _opts;
    private readonly ILogger<SqlBulkInserter> _logger;

    public SqlBulkInserter(
        IConfiguration config,
        IOptions<ExcelImportOptions> opts,
        ILogger<SqlBulkInserter> logger)
    {
        _connString = config.GetConnectionString("DefaultConnection") 
                      ?? throw new InvalidOperationException("DefaultConnection missing");
        _opts       = opts.Value;
        _logger     = logger;
    }

    public async Task BulkInsertAsync(
        DataTable                  table,
        string                     stagingTable,
        IDictionary<string,string>? columnMappings,
        bool                       truncateBeforeInsert,  // ← new
        CancellationToken          ct)
    {
        await using var conn = new SqlConnection(_connString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            if (truncateBeforeInsert)
            {
                _logger.LogInformation("Truncating table {Table}", stagingTable);
                await using var truncateCmd = new SqlCommand(
                    $"TRUNCATE TABLE {stagingTable}", conn, tx);
                await truncateCmd.ExecuteNonQueryAsync(ct);
            }

            _logger.LogInformation(
                "Bulk inserting {Rows} rows into {Table}",
                table.Rows.Count, stagingTable);

            await using (var bulk = new SqlBulkCopy(conn, SqlBulkCopyOptions.Default, tx)
            {
                DestinationTableName = stagingTable,
                BatchSize           = _opts.BulkBatchSize,
                BulkCopyTimeout     = _opts.BulkTimeoutSeconds
            })
            {
                if (columnMappings != null)
                {
                    foreach (var (src, dest) in columnMappings)
                        bulk.ColumnMappings.Add(src, dest);
                }
                else
                {
                    foreach (DataColumn col in table.Columns)
                        bulk.ColumnMappings.Add(col.ColumnName, col.ColumnName);
                }

                await bulk.WriteToServerAsync(table, ct);
            }

            await tx.CommitAsync(ct);
            _logger.LogInformation("Bulk insert committed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bulk insert failed—rolling back");
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
public interface IValidationRunner
{
    Task<(int TotalRows, int RejectedRows)> RunValidationAsync(
        string stagingTable,
        CancellationToken ct);
}
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

public class StoredProcValidationRunner : IValidationRunner
{
    private readonly string             _connString;
    private readonly ExcelImportOptions _opts;
    private readonly ILogger<StoredProcValidationRunner> _logger;

    public StoredProcValidationRunner(
        IConfiguration config,
        IOptions<ExcelImportOptions> opts,
        ILogger<StoredProcValidationRunner> logger)
    {
        _connString = config.GetConnectionString("DefaultConnection")!;
        _opts       = opts.Value;
        _logger     = logger;
    }

    public async Task<(int TotalRows, int RejectedRows)> RunValidationAsync(
        string stagingTable,
        CancellationToken ct)
    {
        await using var conn = new SqlConnection(_connString);
        await conn.OpenAsync(ct);

        await using var cmd = new SqlCommand(_opts.ValidationSpName, conn)
        {
            CommandType = CommandType.StoredProcedure
        };

        cmd.Parameters.Add(new SqlParameter("@StagingTable", SqlDbType.NVarChar, 128)
        {
            Value = stagingTable
        });
        var pTotal = cmd.Parameters.Add(new SqlParameter("@TotalRows", SqlDbType.Int)
        {
            Direction = ParameterDirection.Output
        });
        var pReject = cmd.Parameters.Add(new SqlParameter("@RejectedRows", SqlDbType.Int)
        {
            Direction = ParameterDirection.Output
        });

        _logger.LogInformation("Calling validation SP {Sp}", _opts.ValidationSpName);
        await cmd.ExecuteNonQueryAsync(ct);

        int total    = (int)pTotal.Value!;
        int rejected = (int)pReject.Value!;

        _logger.LogInformation("Validation complete: Total={Total}, Rejected={Rejected}", total, rejected);
        return (total, rejected);
    }
}
public class ExcelImportService : IExcelImportService
{
    private readonly IExcelReader       _reader;
    private readonly IBulkInserter      _inserter;
    private readonly IValidationRunner  _validator;

    public ExcelImportService(
        IExcelReader reader,
        IBulkInserter inserter,
        IValidationRunner validator)
    {
        _reader    = reader;
        _inserter  = inserter;
        _validator = validator;
    }

    public async Task<ExcelImportResult> ImportAsync(
        Stream excelStream,
        string stagingTable,
        string connectionString,
        string? sheetName          = null,
        IDictionary<string,string>? columnMappings = null,
        string? validationStoredProc = null,
        CancellationToken ct       = default)
    {
        // 1. Read
        var table = await _reader.ReadAsync(excelStream, sheetName, ct);

        // 2. Bulk‐insert
        await _inserter.BulkInsertAsync(table, stagingTable, columnMappings, ct);

        // 3. Validate
        var (total, rejected) = await _validator.RunValidationAsync(stagingTable, ct);

        return new ExcelImportResult(total, rejected);
    }
}
