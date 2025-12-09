using ArchivalSystem.Application.Interfaces;
using ArchivalSystem.Application.Models;
using ArchivalSystem.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Parquet;
using Parquet.Schema;
using System.Data;

namespace ArchivalSystem.Infrastructure;

/// <summary>
/// Streams Parquet parts directly from the source database.
/// - Reads rows using SequentialAccess to avoid provider buffering large columns.
/// - Writes row-groups sized by approximate bytes (target ~8MB) to balance memory and compression.
/// - Produces parts of up to maxRowsPerPart rows; the writer callback is invoked synchronously during uploadPartAsync.
/// </summary>
public class ParquetExportService(IConfiguration configuration) : IParquetExportService
{
    private readonly IConfiguration _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

    // Target bytes per parquet row-group (tunable). 8 MiB is a reasonable default.
    private const int RowGroupTargetBytes = 8 * 1024 * 1024;

    public async Task<ParquetExportMetrics> ExportTableToStreamAsync(
        string databaseName,
        string schemaName,
        string tableName,
        string asOfDateColumn,
        DateTime asOfDate,
        Stream output,
        CancellationToken ct = default)
    {
        if (output == null) throw new ArgumentNullException(nameof(output));
        var parts = await ExportTableToPartsAsync(
            databaseName, schemaName, tableName, asOfDateColumn, asOfDate,
            async (ix, writer, token) =>
            {
                // For a single-stream export, call writer with the same output stream
                await writer(output, token).ConfigureAwait(false);

                // After writer returns, we cannot get ETag/length here. Return a minimal ArchivalBlobInfo with size if possible.
                return new ArchivalBlobInfo { StorageAccountName = string.Empty, ContainerName = string.Empty, BlobPath = string.Empty, ContentLength = output.CanSeek ? output.Length : -1 };
            },
            maxRowsPerPart: int.MaxValue, // no parting; let writer stream all rows
            ct: ct);

        // Aggregate metrics
        long rows = 0, size = 0;
        int cols = 0;
        foreach (var p in parts)
        {
            rows += p.Metrics?.RowCount ?? 0;
            size += p.Metrics?.SizeBytes ?? 0;
            cols = p.Metrics?.ColumnCount ?? cols;
        }

        return new ParquetExportMetrics
        {
            RowCount = rows,
            ColumnCount = cols,
            SizeBytes = size
        };
    }

    public async Task<IReadOnlyList<ParquetExportPartResult>> ExportTableToPartsAsync(
        string databaseName,
        string schemaName,
        string tableName,
        string asOfDateColumn,
        DateTime asOfDate,
        Func<int, Func<Stream, CancellationToken, Task>, CancellationToken, Task<ArchivalBlobInfo>> uploadPartAsync,
        int maxRowsPerPart = 250_000,
        CancellationToken ct = default)
    {
        if (uploadPartAsync == null) throw new ArgumentNullException(nameof(uploadPartAsync));
        if (maxRowsPerPart <= 0) throw new ArgumentOutOfRangeException(nameof(maxRowsPerPart));

        var connStr = _configuration.GetConnectionString(databaseName);
        if (string.IsNullOrWhiteSpace(connStr))
            throw new InvalidOperationException($"Connection string for source database '{databaseName}' not found in configuration.");

        var results = new List<ParquetExportPartResult>();

        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        // Build safe identifiers
        string qSchema = QuoteIdentifier(schemaName ?? "dbo");
        string qTable = QuoteIdentifier(tableName);
        string qAsOf = QuoteIdentifier(asOfDateColumn);

        var sql = $"SELECT * FROM {qSchema}.{qTable} WHERE {qAsOf} = @asOfDate"; // No ORDER BY to avoid heavy sorts; acceptable for exports keyed by asOf.
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add(new SqlParameter("@asOfDate", SqlDbType.DateTime2) { Value = asOfDate });
        // Use SequentialAccess to stream large columns (VARBINARY, XML, etc.) without buffering entire row
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess | CommandBehavior.CloseConnection, ct).ConfigureAwait(false);

        var fieldCount = reader.FieldCount;
        var fields = BuildParquetFields(reader);
        var schema = new ParquetSchema(fields.ToArray());
        var columnCount = fields.Count;

        // If no rows, create an empty single part.
        if (!reader.HasRows)
        {
            ArchivalBlobInfo emptyBlob = await uploadPartAsync(0, async (s, token) =>
            {
                await using var writer = await ParquetWriter.CreateAsync(schema, s, cancellationToken: token).ConfigureAwait(false);
                writer.CompressionMethod = CompressionMethod.Snappy;
                // no rows written
            }, ct).ConfigureAwait(false);

            results.Add(new ParquetExportPartResult
            {
                PartIndex = 0,
                Metrics = new ParquetExportMetrics { RowCount = 0, ColumnCount = columnCount, SizeBytes = emptyBlob.ContentLength ?? -1 },
                BlobInfo = emptyBlob
            });

            return results;
        }

        int partIndex = 0;
        long totalRows = 0;
        bool moreRows = true;

        while (moreRows)
        {
            int rowsInPart = 0;
            long estimatedBytesForPart = 0;

            async Task Writer(Stream output, CancellationToken token)
            {
                await using var parquetWriter = await ParquetWriter.CreateAsync(schema, output, cancellationToken: token).ConfigureAwait(false);
                parquetWriter.CompressionMethod = CompressionMethod.Snappy;

                // Column buffers as lists of object? We will store per-column list and flush as row-groups
                var columnBuffers = InitColumnBuffers(fieldCount);
                int rowsInRowGroup = 0;
                long bytesInRowGroup = 0;

                while (rowsInPart < maxRowsPerPart)
                {
                    token.ThrowIfCancellationRequested();

                    var hasRow = await reader.ReadAsync(token).ConfigureAwait(false);
                    if (!hasRow)
                    {
                        moreRows = false;
                        break;
                    }

                    // read row values sequentially
                    for (int i = 0; i < fieldCount; i++)
                    {
                        object? val = reader.IsDBNull(i) ? null : reader.GetValue(i);
                        columnBuffers[i].Add(val);
                        bytesInRowGroup += EstimateObjectSize(val);
                    }

                    rowsInRowGroup++;
                    rowsInPart++;
                    totalRows++;

                    // flush if row-group bytes exceeded target or row-group row count large
                    if (bytesInRowGroup >= RowGroupTargetBytes || rowsInRowGroup >= Math.Max(1, RowGroupTargetBytes / 1024))
                    {
                        await WriteRowGroupAsync(parquetWriter, fields, columnBuffers, token).ConfigureAwait(false);
                        columnBuffers = InitColumnBuffers(fieldCount);
                        rowsInRowGroup = 0;
                        bytesInRowGroup = 0;
                    }
                }

                // flush any remaining rows in current part
                if (rowsInRowGroup > 0)
                {
                    await WriteRowGroupAsync(parquetWriter, fields, columnBuffers, token).ConfigureAwait(false);
                }
            }

            // The uploadPartAsync is expected to call our writer; it returns final blob info (ETag/Length)
            ArchivalBlobInfo blob = await uploadPartAsync(partIndex, Writer, ct).ConfigureAwait(false);

            var metrics = new ParquetExportMetrics
            {
                RowCount = rowsInPart,
                ColumnCount = columnCount,
                SizeBytes = blob.ContentLength ?? -1
            };

            results.Add(new ParquetExportPartResult
            {
                PartIndex = partIndex,
                Metrics = metrics,
                BlobInfo = blob
            });

            partIndex++;
        }

        return results;
    }

    // Helpers

    private static string QuoteIdentifier(string id)
        => "[" + id.Replace("]", "]]") + "]";

    private static List<DataField> BuildParquetFields(IDataRecord reader)
    {
        var list = new List<DataField>();
        for (int i = 0; i < reader.FieldCount; i++)
        {
            var name = reader.GetName(i);
            var t = reader.GetFieldType(i);

            if (t == typeof(int) || t == typeof(int?)) list.Add(new DataField<int?>(name));
            else if (t == typeof(long) || t == typeof(long?)) list.Add(new DataField<long?>(name));
            else if (t == typeof(decimal) || t == typeof(decimal?)) list.Add(new DataField<decimal?>(name));
            else if (t == typeof(double) || t == typeof(double?)) list.Add(new DataField<double?>(name));
            else if (t == typeof(float) || t == typeof(float?)) list.Add(new DataField<float?>(name));
            else if (t == typeof(bool) || t == typeof(bool?)) list.Add(new DataField<bool?>(name));
            else if (t == typeof(DateTime) || t == typeof(DateTime?)) list.Add(new DataField<DateTime?>(name));
            else if (t == typeof(Guid) || t == typeof(Guid?)) list.Add(new DataField<string>(name));
            else list.Add(new DataField<string>(name));
        }
        return list;
    }

    private static List<List<object?>> InitColumnBuffers(int fieldCount)
    {
        var cols = new List<List<object?>>(fieldCount);
        for (int i = 0; i < fieldCount; i++) cols.Add(new List<object?>());
        return cols;
    }

    private static long EstimateObjectSize(object? o)
    {
        if (o == null) return 8; // small overhead
        if (o is string s) return s.Length * 2 + 8;
        if (o is byte[] b) return b.Length;
        if (o is int || o is long || o is double || o is decimal || o is float) return 8;
        if (o is DateTime) return 8;
        return 16;
    }

    private static async Task WriteRowGroupAsync(ParquetWriter parquetWriter, List<DataField> fields, List<List<object?>> columnBuffers, CancellationToken ct)
    {
        using var rowGroup = parquetWriter.CreateRowGroup();
        for (int i = 0; i < fields.Count; i++)
        {
            var field = fields[i];
            var buffer = columnBuffers[i];
            var clrType = field.ClrType;

            // Create typed array and copy values
            var arr = Array.CreateInstance(clrType, buffer.Count);
            for (int r = 0; r < buffer.Count; r++)
            {
                object? v = buffer[r];
                if (v == null)
                    arr.SetValue(null, r);
                else
                {
                    // Handle GUIDs mapped to string
                    if (clrType == typeof(string) && v is Guid g) arr.SetValue(g.ToString(), r);
                    else arr.SetValue(Convert.ChangeType(v, Nullable.GetUnderlyingType(clrType) ?? clrType), r);
                }
            }

            var dataColumn = new Parquet.Data.DataColumn(field, arr);
            await rowGroup.WriteColumnAsync(dataColumn, ct).ConfigureAwait(false);
        }
    }
}