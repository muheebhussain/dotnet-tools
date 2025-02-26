using Azure.Storage.Blobs;
using Npgsql;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace LargeFileIngestion
{
    /// <summary>
    /// Example class to be invoked by Hangfire as a background job.
    /// You might have a Hangfire job like:
    /// BackgroundJob.Enqueue(() => new FileIngestor().RunAsync());
    /// </summary>
    public class FileIngestor
    {
        // Update with your actual connection strings and blob info
        private readonly string _blobConnectionString = "<YourAzureBlobConnectionString>";
        private readonly string _blobContainerName    = "<YourContainerName>";
        private readonly string _blobFileName         = "<YourLargePipeDelimitedFile.txt>";

        private readonly string _postgresConnectionString = 
            "Host=<host>;Database=<db>;Username=<user>;Password=<pass>";

        // Adjust to a suitable chunk size. Larger chunks = fewer COPY calls, but more memory usage.
        private const int BATCH_SIZE = 10_000;

        public async Task RunAsync()
        {
            Console.WriteLine($"[{DateTime.Now}] Starting ingestion job via Hangfire...");

            // 1. Open the blob for streaming
            var blobClient = new BlobClient(_blobConnectionString, _blobContainerName, _blobFileName);
            using var blobStream = await blobClient.OpenReadAsync();  // streaming download
            using var reader = new StreamReader(blobStream);

            // 2. Open PostgreSQL connection
            using var pgConnection = new NpgsqlConnection(_postgresConnectionString);
            await pgConnection.OpenAsync();

            // 3. We will collect lines for each table (based on record type) in a StringBuilder
            var productBuffer   = new StringBuilder();
            var inventoryBuffer = new StringBuilder();
            var tradeBuffer     = new StringBuilder();
            var dealsBuffer     = new StringBuilder();

            // Counters for how many lines are currently in each buffer
            int productCount   = 0;
            int inventoryCount = 0;
            int tradeCount     = 0;
            int dealsCount     = 0;

            // For logging
            long totalLinesRead = 0;

            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                totalLinesRead++;
                if (string.IsNullOrWhiteSpace(line)) 
                    continue;

                // Split the line by '|'
                var columns = line.Split('|');

                // Identify record type in first column
                var recordType = columns[0];

                switch (recordType)
                {
                    case "06":
                        AppendLineToBuffer(productBuffer, columns, 5, '|');
                        productCount++;
                        if (productCount >= BATCH_SIZE)
                        {
                            await CopyChunkAsync(pgConnection, "product", "(col1, col2, col3, col4, col5)", productBuffer, '|');
                            productBuffer.Clear();
                            productCount = 0;
                        }
                        break;

                    case "07":
                        AppendLineToBuffer(inventoryBuffer, columns, 4, '|');
                        inventoryCount++;
                        if (inventoryCount >= BATCH_SIZE)
                        {
                            await CopyChunkAsync(pgConnection, "inventory", "(col1, col2, col3, col4)", inventoryBuffer, '|');
                            inventoryBuffer.Clear();
                            inventoryCount = 0;
                        }
                        break;

                    case "09":
                        AppendLineToBuffer(tradeBuffer, columns, 3, '|');
                        tradeCount++;
                        if (tradeCount >= BATCH_SIZE)
                        {
                            await CopyChunkAsync(pgConnection, "trade", "(col1, col2, col3)", tradeBuffer, '|');
                            tradeBuffer.Clear();
                            tradeCount = 0;
                        }
                        break;

                    case "12":
                        AppendLineToBuffer(dealsBuffer, columns, 6, '|');
                        dealsCount++;
                        if (dealsCount >= BATCH_SIZE)
                        {
                            await CopyChunkAsync(pgConnection, "deals", "(col1, col2, col3, col4, col5, col6)", dealsBuffer, '|');
                            dealsBuffer.Clear();
                            dealsCount = 0;
                        }
                        break;

                    default:
                        // Ignore or handle other types
                        break;
                }
            }

            // 4. Flush any remaining rows in each buffer
            if (productCount > 0)
            {
                await CopyChunkAsync(pgConnection, "product", "(col1, col2, col3, col4, col5)", productBuffer, '|');
            }
            if (inventoryCount > 0)
            {
                await CopyChunkAsync(pgConnection, "inventory", "(col1, col2, col3, col4)", inventoryBuffer, '|');
            }
            if (tradeCount > 0)
            {
                await CopyChunkAsync(pgConnection, "trade", "(col1, col2, col3)", tradeBuffer, '|');
            }
            if (dealsCount > 0)
            {
                await CopyChunkAsync(pgConnection, "deals", "(col1, col2, col3, col4, col5, col6)", dealsBuffer, '|');
            }

            Console.WriteLine($"[{DateTime.Now}] Finished ingestion. Total lines read: {totalLinesRead}");
        }

        /// <summary>
        /// Appends a single row (as pipe-delimited string) to the specified StringBuilder.
        /// Ensures we only take up to 'maxColumns' from the split array; any missing columns become empty.
        /// 'delimiter' is used between columns; we also append a newline at the end.
        /// </summary>
        private void AppendLineToBuffer(StringBuilder buffer, string[] columns, int maxColumns, char delimiter)
        {
            // We'll accumulate exactly 'maxColumns' columns. If the input has fewer, fill with empty; if more, ignore extras
            for (int i = 0; i < maxColumns; i++)
            {
                if (i > 0)
                    buffer.Append(delimiter);

                if (i < columns.Length)
                {
                    // If you need special escaping (in case the data can contain delimiter/newlines),
                    // you can implement quoting here. 
                    // For simplicity, we assume no special escaping is required or the data is "clean".
                    buffer.Append(columns[i]);
                }
            }
            buffer.Append('\n'); // End of row
        }

        /// <summary>
        /// Performs a COPY import into a specified table and column list
        /// using the data currently in 'chunkData'.
        /// The data is assumed to be text with a known delimiter.
        /// </summary>
        private async Task CopyChunkAsync(NpgsqlConnection connection, string tableName, string columnList, 
            StringBuilder chunkData, char delimiter)
        {
            // For example:
            // COPY product (col1,col2,col3,col4,col5) FROM STDIN WITH (FORMAT csv, DELIMITER '|')
            // or use a text format and pass raw lines.

            // If you want to do standard CSV with quoting, you'd do: FORMAT csv, DELIMITER '|', QUOTE '"'
            // If your data is known to be pipe-delimited and safe, you can do text format with DELIMITER '|'.
            // For demonstration, let's do CSV with pipe delimiter:
            var copyCommand = $"COPY {tableName} {columnList} FROM STDIN WITH (FORMAT csv, DELIMITER '{delimiter}')";

            try
            {
                using var writer = await connection.BeginTextImportAsync(copyCommand);
                await writer.WriteAsync(chunkData.ToString());
            }
            catch (Exception ex)
            {
                // Handle logging / errors as appropriate. 
                // In production code, you might want retry logic depending on your requirements.
                Console.WriteLine($"Error during COPY for table {tableName}: {ex.Message}");
                throw;
            }
        }
    }
}
