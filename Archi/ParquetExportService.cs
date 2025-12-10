using ArchivalSystem.Application.Interfaces;
using ArchivalSystem.Application.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Parquet;
using Parquet.Schema;
using System.Data;
using System.Diagnostics;

namespace ArchivalSystem.Infrastructure;

/// <summary>
/// Streams Parquet parts directly from the source database.
/// Producer/consumer with spill-to-disk for parts (keeps memory bounded on AKS).
/// Logs per-part metrics for telemetry.
/// </summary>
public class ParquetExportService : IParquetExportService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<ParquetExportService> _logger;

    // Target bytes per parquet row-group (tunable). 8 MiB is a reasonable default.
    private const int RowGroupTargetBytes = 8 * 1024 * 1024;

    // Default memory threshold before spilling a part to disk (16 MiB).
    private const int DefaultSpillThresholdBytes = 16 * 1024 * 1024;

    // Degree of parallelism for concurrent uploads; populated from configuration (BlobStorageOptions.DegreeOfParallelism) when available.
    private readonly int _uploadDegreeOfParallelism;

    public ParquetExportService(IConfiguration configuration, ILogger<ParquetExportService> logger)
        : this(configuration, 4, logger)
    { }

    public ParquetExportService(IConfiguration configuration, int fallbackUploadDegree, ILogger<ParquetExportService> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        try
        {
            var bs = _configuration.GetSection(nameof(BlobStorageOptions)).Get<BlobStorageOptions>();
            _uploadDegreeOfParallelism = (bs?.DegreeOfParallelism > 0) ? bs.DegreeOfParallelism : Math.Max(1, fallbackUploadDegree);
        }
        catch
        {
            _uploadDegreeOfParallelism = Math.Max(1, fallbackUploadDegree);
        }
    }

    public async Task<IReadOnlyList<ParquetExportPartResult>> ExportTableToPartsAsync(
        string databaseName,
        string schemaName,
        string tableName,
        string asOfDateColumn,
        DateTime asOfDate,
        Func<int, Func<Stream, CancellationToken, Task>, CancellationToken, Task<ArchivalBlobInfo>> uploadPartAsync,
        int maxRowsPerPart = 50_000,
        CancellationToken ct = default)
    {
        if (uploadPartAsync == null) throw new ArgumentNullException(nameof(uploadPartAsync));
        if (maxRowsPerPart <= 0) throw new ArgumentOutOfRangeException(nameof(maxRowsPerPart));

        var connStr = _configuration.GetConnectionString(databaseName);
        if (string.IsNullOrWhiteSpace(connStr))
            throw new InvalidOperationException($"Connection string for source database '{databaseName}' not found in configuration.");

        var results = new List<ParquetExportPartResult>();

        await using var conn = new SqlConnection(connStr);
        if (conn.State != ConnectionState.Open)
            await conn.OpenAsync(ct).ConfigureAwait(false);

        // Build safe identifiers
        string qSchema = QuoteIdentifier(schemaName ?? "dbo");
        string qTable = QuoteIdentifier(tableName);
        string qAsOf = QuoteIdentifier(asOfDateColumn);

        var sql = $"SELECT * FROM {qSchema}.{qTable} WHERE {qAsOf} = @asOfDate";
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add(new SqlParameter("@asOfDate", SqlDbType.DateTime2) { Value = asOfDate });

        // Use SequentialAccess to stream large columns (VARBINARY, XML, etc.) without buffering entire row
        await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess | CommandBehavior.CloseConnection, ct).ConfigureAwait(false);

        var fieldCount = reader.FieldCount;
        var fields = BuildParquetFields(reader);
        var schema = new ParquetSchema(fields.ToArray());
        var columnCount = fields.Count;

        // If no rows, create an empty single part (produce synchronously)
        if (!reader.HasRows)
        {
            ArchivalBlobInfo emptyBlob = await uploadPartAsync(1, async (s, token) =>
            {
                await using var writer = await ParquetWriter.CreateAsync(schema, s, cancellationToken: token).ConfigureAwait(false);
                writer.CompressionMethod = CompressionMethod.Snappy;
                // no rows written
            }, ct).ConfigureAwait(false);

            var emptyResult = new ParquetExportPartResult
            {
                PartIndex = 1,
                Metrics = new ParquetExportMetrics { RowCount = 0, ColumnCount = columnCount, SizeBytes = emptyBlob.ContentLength ?? -1 },
                BlobInfo = emptyBlob,
                WriteDuration = TimeSpan.Zero,
                UploadDuration = TimeSpan.Zero,
                TotalDuration = TimeSpan.Zero
            };

            _logger.LogInformation("ExportTableToPartsAsync: produced empty part {PartIndex} rows={RowCount} size={SizeBytes}",
                emptyResult.PartIndex, emptyResult.Metrics.RowCount, emptyResult.Metrics.SizeBytes);

            results.Add(emptyResult);
            return results;
        }

        // Producer/consumer: write part into SpillableStream (pooled memory + spill) and upload concurrently.
        var uploadSemaphore = new SemaphoreSlim(_uploadDegreeOfParallelism, _uploadDegreeOfParallelism);
        var uploadTasks = new List<Task<ParquetExportPartResult>>();

        int partIndex = 1;
        bool moreRows = true;

        try
        {
            while (moreRows)
            {
                ct.ThrowIfCancellationRequested();

                int rowsInPart = 0;
                long partSizeBytes = -1;
                TimeSpan writeDuration;

                using var spill = new SpillableStream(DefaultSpillThresholdBytes);

                var swWrite = Stopwatch.StartNew();

                // Write part into spillable stream
                await using (var parquetWriter = await ParquetWriter.CreateAsync(schema, spill, cancellationToken: ct).ConfigureAwait(false))
                {
                    parquetWriter.CompressionMethod = CompressionMethod.Snappy;

                    var columnBuffers = InitColumnBuffers(fieldCount);
                    int rowsInRowGroup = 0;
                    long bytesInRowGroup = 0;

                    while (rowsInPart < maxRowsPerPart)
                    {
                        ct.ThrowIfCancellationRequested();

                        var hasRow = await reader.ReadAsync(ct).ConfigureAwait(false);
                        if (!hasRow)
                        {
                            moreRows = false;
                            break;
                        }

                        for (int i = 0; i < fieldCount; i++)
                        {
                            object? val = reader.IsDBNull(i) ? null : reader.GetValue(i);
                            columnBuffers[i].Add(val);
                            bytesInRowGroup += EstimateObjectSize(val);
                        }

                        rowsInRowGroup++;
                        rowsInPart++;

                        if (bytesInRowGroup >= RowGroupTargetBytes || rowsInRowGroup >= Math.Max(1, RowGroupTargetBytes / 1024))
                        {
                            await WriteRowGroupAsync(parquetWriter, fields, columnBuffers, ct).ConfigureAwait(false);
                            columnBuffers = InitColumnBuffers(fieldCount);
                            rowsInRowGroup = 0;
                            bytesInRowGroup = 0;
                        }
                    }

                    if (rowsInRowGroup > 0)
                    {
                        await WriteRowGroupAsync(parquetWriter, fields, columnBuffers, ct).ConfigureAwait(false);
                    }

                    // flush
                    await spill.FlushAsync(ct).ConfigureAwait(false);
                }

                swWrite.Stop();
                writeDuration = swWrite.Elapsed;

                // Obtain read stream for upload (ownership transferred to caller)
                var readStream = spill.GetReadStream();
                partSizeBytes = readStream.CanSeek ? readStream.Length : -1;

                // Start upload task (consumer). Bounded concurrency via semaphore.
                await uploadSemaphore.WaitAsync(ct).ConfigureAwait(false);

                var indexForClosure = partIndex;
                var rowsForClosure = rowsInPart;
                var writeDurationForClosure = writeDuration;
                var readStreamForClosure = readStream; // will be disposed by upload task

                var uploadTask = Task.Run(async () =>
                {
                    var swUpload = Stopwatch.StartNew();
                    try
                    {
                        // Writer that copies the readStream into the provided out stream
                        async Task Writer(Stream outStream, CancellationToken token)
                        {
                            if (readStreamForClosure.CanSeek) readStreamForClosure.Position = 0;
                            await readStreamForClosure.CopyToAsync(outStream, 81920, token).ConfigureAwait(false);
                        }

                        // Call caller-provided uploadPartAsync which will invoke our Writer
                        var blob = await uploadPartAsync(indexForClosure, Writer, ct).ConfigureAwait(false);

                        swUpload.Stop();
                        var uploadDuration = swUpload.Elapsed;
                        var totalDuration = writeDurationForClosure + uploadDuration;

                        var metrics = new ParquetExportMetrics
                        {
                            RowCount = rowsForClosure,
                            ColumnCount = columnCount,
                            SizeBytes = blob.ContentLength ?? partSizeBytes
                        };

                        var partResult = new ParquetExportPartResult
                        {
                            PartIndex = indexForClosure,
                            Metrics = metrics,
                            BlobInfo = blob,
                            WriteDuration = writeDurationForClosure,
                            UploadDuration = uploadDuration,
                            TotalDuration = totalDuration
                        };

                        // Log per-part telemetry
                        _logger.LogInformation("Exported part {PartIndex}: rows={RowCount} size={SizeBytes} writeMs={WriteMs} uploadMs={UploadMs} totalMs={TotalMs}",
                            partResult.PartIndex,
                            partResult.Metrics.RowCount,
                            partResult.Metrics.SizeBytes,
                            partResult.WriteDuration.TotalMilliseconds,
                            partResult.UploadDuration.TotalMilliseconds,
                            partResult.TotalDuration.TotalMilliseconds);

                        return partResult;
                    }
                    finally
                    {
                        // Ensure we dispose the readStream (returns pooled buffers or closes file)
                        try { await readStreamForClosure.DisposeAsync().ConfigureAwait(false); } catch { /* ignore */ }
                        uploadSemaphore.Release();
                    }
                }, ct);

                uploadTasks.Add(uploadTask);

                partIndex++;
            } // end while producer loop

            // Wait for all uploads to finish and collect results
            var completed = await Task.WhenAll(uploadTasks).ConfigureAwait(false);
            results.AddRange(completed.OrderBy(r => r.PartIndex));
            return results;
        }
        finally
        {
            // Nothing required; read streams and spills are disposed by upload tasks.
        }
    }

    // Helpers

    private static string QuoteIdentifier(string id) => "[" + id.Replace("]", "]]") + "]";

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

    private static async Task WriteRowGroupAsync(ParquetWriter parquetWriter, List<DataField> fields,
        List<List<object?>> columnBuffers, CancellationToken ct)
    {
        using var rowGroup = parquetWriter.CreateRowGroup();

        for (int i = 0; i < fields.Count; i++)
        {
            var field = fields[i];
            var buffer = columnBuffers[i];

            // Convert buffered values to object?[] then produce a strongly-typed DataColumn
            var columnData = buffer.ToArray();
            var dataColumn = GetColumn(field, columnData);

            await rowGroup.WriteColumnAsync(dataColumn, ct).ConfigureAwait(false);
        }
    }

    private static Parquet.Data.DataColumn GetColumn(DataField columnSchema, object?[] columnData)
    {
        // Resolve underlying CLR type (handle nullable<T>)
        var clrType = Nullable.GetUnderlyingType(columnSchema.ClrType) ?? columnSchema.ClrType;
        var typeCode = Type.GetTypeCode(clrType);

        // Use LINQ Cast<T?> to create correctly-typed arrays the Parquet library expects.
        return typeCode switch
        {
            TypeCode.String => new Parquet.Data.DataColumn(columnSchema, columnData.Cast<string?>().ToArray()),
            TypeCode.Int32 => new Parquet.Data.DataColumn(columnSchema, columnData.Cast<int?>().ToArray()),
            TypeCode.Int64 => new Parquet.Data.DataColumn(columnSchema, columnData.Cast<long?>().ToArray()),
            TypeCode.Decimal => new Parquet.Data.DataColumn(columnSchema, columnData.Cast<decimal?>().ToArray()),
            TypeCode.Double => new Parquet.Data.DataColumn(columnSchema, columnData.Cast<double?>().ToArray()),
            TypeCode.Single => new Parquet.Data.DataColumn(columnSchema, columnData.Cast<float?>().ToArray()),
            TypeCode.DateTime => new Parquet.Data.DataColumn(columnSchema, columnData.Cast<DateTime?>().ToArray()),
            TypeCode.Boolean => new Parquet.Data.DataColumn(columnSchema, columnData.Cast<bool?>().ToArray()),
            TypeCode.Byte => new Parquet.Data.DataColumn(columnSchema, columnData.Cast<byte?>().ToArray()),
            _ => new Parquet.Data.DataColumn(columnSchema, columnData)
        };
    }
}