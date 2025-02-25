using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using CsvHelper;
using Npgsql;

public static class ProviderDealsProcessor
{
    /// <summary>
    /// Download FileA and FileB separately from given URLs, saving to local paths.
    /// </summary>
    public static async Task DownloadFilesAsync(string fileAUrl, string fileBUrl, string fileALocalPath, string fileBLocalPath)
    {
        using var httpClient = new HttpClient();

        // Download FileA
        var fileABytes = await httpClient.GetByteArrayAsync(fileAUrl);
        await File.WriteAllBytesAsync(fileALocalPath, fileABytes);

        // Download FileB
        var fileBBytes = await httpClient.GetByteArrayAsync(fileBUrl);
        await File.WriteAllBytesAsync(fileBLocalPath, fileBBytes);
    }

    /// <summary>
    /// Reads FileB to build a dictionary of the best MeasureValue for each TradeId.
    /// Non-null settleDatePosition overrides OriginalTradeAmount (i.e., always prefer a non-empty settleDatePosition).
    /// If settleDatePosition is null/empty, use OriginalTradeAmount if it exists.
    /// </summary>
    public static Dictionary<string, string> BuildMeasureValueDictionary(string fileBPath)
    {
        // Temporary store of (settleVal, originalVal) for each TradeId
        var intermediate = new Dictionary<string, (string settleVal, string originalVal)>(StringComparer.OrdinalIgnoreCase);

        using var reader = new StreamReader(fileBPath);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

        // Adjust column names below if they differ in your CSV
        while (csv.Read())
        {
            var tradeId = csv.GetField<string>("TradeId")?.Trim();
            var indicator = csv.GetField<string>("IndicatorSubTypeCode")?.Trim();
            var measureVal = csv.GetField<string>("MeasureValue")?.Trim();

            if (string.IsNullOrEmpty(tradeId))
                continue;

            if (!intermediate.TryGetValue(tradeId, out var tuple))
                tuple = (null, null);

            // If this is a settleDatePosition row...
            if (indicator.Equals("settleDatePosition", StringComparison.OrdinalIgnoreCase))
            {
                // Store or override settleVal if measureVal is non-null
                // i.e., non-empty settleVal always overrides
                if (!string.IsNullOrWhiteSpace(measureVal))
                {
                    tuple.settleVal = measureVal;
                }
                else
                {
                    // Keep existing settleVal if any, otherwise it remains null
                    // do nothing special here
                }
            }
            else if (indicator.Equals("OriginalTradeAmount", StringComparison.OrdinalIgnoreCase))
            {
                // Store originalTradeAmount
                if (!string.IsNullOrWhiteSpace(measureVal))
                {
                    tuple.originalVal = measureVal;
                }
            }

            intermediate[tradeId] = tuple;
        }

        // Build final dictionary picking the best measure value
        var final = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in intermediate)
        {
            var tradeId = kvp.Key;
            var (settleVal, originalVal) = kvp.Value;

            // If settleVal is not empty, use it; else use originalVal (could be null, so handle accordingly)
            if (!string.IsNullOrWhiteSpace(settleVal))
            {
                final[tradeId] = settleVal;
            }
            else
            {
                final[tradeId] = originalVal; // could be null if originalVal is missing
            }
        }

        return final;
    }

    /// <summary>
    /// Batches final records into ProviderDeals via PostgreSQL COPY BINARY for speed.
    /// </summary>
    public static async Task BulkInsertProviderDealsAsync(string connectionString, IEnumerable<ProviderDeal> deals)
    {
        var list = deals.ToList();
        if (!list.Any()) return;  // no records to insert

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        // The columns must match your ProviderDeals table definition.
        var copySql = @"
        COPY ProviderDeals (
            DealId,
            TradeId,
            BookCode,
            Tsip,
            TradeDate,
            BusinessDate,
            SettlementDate,
            BuyPrice,
            ProviderId,
            MeasureValue,
            CreatedOn
        )
        FROM STDIN (FORMAT BINARY)";

        using var writer = conn.BeginBinaryImport(copySql);

        foreach (var deal in list)
        {
            writer.StartRow();
            writer.Write(deal.DealId, NpgsqlTypes.NpgsqlDbType.Varchar);
            writer.Write(deal.TradeId, NpgsqlTypes.NpgsqlDbType.Varchar);
            writer.Write(deal.BookCode, NpgsqlTypes.NpgsqlDbType.Varchar);
            writer.Write(deal.Tsip, NpgsqlTypes.NpgsqlDbType.Varchar);
            writer.Write(deal.TradeDate, NpgsqlTypes.NpgsqlDbType.Date);
            writer.Write(deal.BusinessDate, NpgsqlTypes.NpgsqlDbType.Date);
            writer.Write(deal.SettlementDate, NpgsqlTypes.NpgsqlDbType.Date);
            writer.Write(deal.BuyPrice, NpgsqlTypes.NpgsqlDbType.Numeric);
            writer.Write(deal.ProviderId, NpgsqlTypes.NpgsqlDbType.Varchar);
            writer.Write(deal.MeasureValue, NpgsqlTypes.NpgsqlDbType.Varchar);
            writer.Write(deal.CreatedOn, NpgsqlTypes.NpgsqlDbType.Date);
        }

        writer.Complete();
    }

    /// <summary>
    /// Reads FileA row by row, joins measureValue from dictionary, creates final records, and batch-inserts them.
    /// </summary>
    public static async Task ProcessFileAAndInsertAsync(
        string fileAPath,
        Dictionary<string, string> bMeasureValueDict,
        string connectionString)
    {
        const int batchSize = 1000;
        var buffer = new List<ProviderDeal>(batchSize);

        using var reader = new StreamReader(fileAPath);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

        while (csv.Read())
        {
            // Extract columns from FileA
            var dealId = csv.GetField<string>("DealId")?.Trim();
            var tradeId = csv.GetField<string>("TradeId")?.Trim();
            var bookCode = csv.GetField<string>("BookCode")?.Trim();
            var tsip = csv.GetField<string>("Tsip")?.Trim();
            var tradeDate = csv.GetField<DateTime>("TradeDate");
            var businessDate = csv.GetField<DateTime>("BusinessDate");
            var settlementDate = csv.GetField<DateTime>("SettlementDate");
            var buyPrice = csv.GetField<decimal>("BuyPrice");
            var providerId = csv.GetField<string>("ProviderId")?.Trim();

            if (string.IsNullOrEmpty(tradeId)) 
                continue; // skip invalid row

            // Lookup measure value from dictionary
            bMeasureValueDict.TryGetValue(tradeId, out var measureValue);

            // Build final object
            var deal = new ProviderDeal
            {
                DealId = dealId,
                TradeId = tradeId,
                BookCode = bookCode,
                Tsip = tsip,
                TradeDate = tradeDate,
                BusinessDate = businessDate,
                SettlementDate = settlementDate,
                BuyPrice = buyPrice,
                ProviderId = providerId,
                MeasureValue = measureValue,
                CreatedOn = DateTime.Today
            };

            buffer.Add(deal);

            // If buffer is full, write to DB
            if (buffer.Count >= batchSize)
            {
                await BulkInsertProviderDealsAsync(connectionString, buffer);
                buffer.Clear();
            }
        }

        // Insert any leftover
        if (buffer.Count > 0)
        {
            await BulkInsertProviderDealsAsync(connectionString, buffer);
            buffer.Clear();
        }
    }

    /// <summary>
    /// If the process fails, we remove any rows created on the given date (coarse cleanup).
    /// Adjust logic as needed if you want a more precise rollback.
    /// </summary>
    public static async Task BulkDeleteByDateAsync(string connectionString, DateTime createdOnDate)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        // Example: removing rows that match CreatedOn exactly
        var sql = "DELETE FROM ProviderDeals WHERE CreatedOn = @createdOn;";
        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("createdOn", createdOnDate.Date); // be sure to match date column type
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Archive a file to Azure Blob Storage.
    /// </summary>
    public static async Task ArchiveFileToAzureBlobAsync(
        string blobConnectionString,
        string containerName,
        string localFilePath,
        string blobFileName)
    {
        var serviceClient = new BlobServiceClient(blobConnectionString);
        var containerClient = serviceClient.GetBlobContainerClient(containerName);
        await containerClient.CreateIfNotExistsAsync();

        var blobClient = containerClient.GetBlobClient(blobFileName);
        await blobClient.UploadAsync(localFilePath, overwrite: true);
    }

    /// <summary>
    /// Orchestrates the entire workflow:
    /// 1) Download FileA and FileB
    /// 2) Build dictionary from FileB
    /// 3) Stream FileA + insert results
    /// 4) If fail, bulk-delete today's records
    /// 5) If success, archive CSVs to Azure Blob
    /// </summary>
    public static async Task RunProcessAsync(
        string fileAUrl, 
        string fileBUrl,
        string fileALocalPath,
        string fileBLocalPath,
        string connectionString,
        string blobConnectionString,
        string containerName)
    {
        try
        {
            // 1) Download both files
            await DownloadFilesAsync(fileAUrl, fileBUrl, fileALocalPath, fileBLocalPath);

            // 2) Build dictionary from FileB
            var measureValueDict = BuildMeasureValueDictionary(fileBLocalPath);

            // 3) Process FileA and insert
            await ProcessFileAAndInsertAsync(fileALocalPath, measureValueDict, connectionString);

            // (No single transaction used => partial data may exist if we crash mid-way.)

            // 4) If we got here, success: archive files
            // This can be to a folder structure using date/time, e.g. "archive/FileA_2025-02-25.csv"
            var dateStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var archiveAName = $"archive/FileA_{dateStamp}.csv";
            var archiveBName = $"archive/FileB_{dateStamp}.csv";

            await ArchiveFileToAzureBlobAsync(blobConnectionString, containerName, fileALocalPath, archiveAName);
            await ArchiveFileToAzureBlobAsync(blobConnectionString, containerName, fileBLocalPath, archiveBName);

            Console.WriteLine("Process completed successfully.");
        }
        catch (Exception ex)
        {
            // 5) If fail, remove today's rows to revert partial insert
            Console.WriteLine($"Process failed: {ex.Message}");
            Console.WriteLine("Performing bulk delete of today's records in ProviderDeals...");

            await BulkDeleteByDateAsync(connectionString, DateTime.Today);
            // Log the exception or rethrow as needed.
        }
    }
}
