using Azure.Storage.Blobs;
using System;
using System.IO;
using System.IO.Compression; // for GZipStream
using System.Text;
using System.Threading.Tasks;

namespace LargeFileIngestion
{
    public class FileIngestor
    {
        private readonly string _blobConnectionString = "<YourAzureBlobConnectionString>";
        private readonly string _blobContainerName    = "<YourContainerName>";
        private readonly string _blobFileName         = "<YourBlobFileName.txt>";

        private readonly string _postgresConnectionString = 
            "Host=<host>;Database=<db>;Username=<user>;Password=<pass>";

        // We'll store the archived file in the same container, under "archive/"
        private readonly string _archiveFileName; // e.g. "archive/myfile_20250225.gz"

        public FileIngestor()
        {
            // Example archive name:
            var dateSuffix = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var baseName   = Path.GetFileNameWithoutExtension(_blobFileName);
            _archiveFileName = $"archive/{baseName}_{dateSuffix}.gz";
        }

        public async Task RunAsync()
        {
            Console.WriteLine($"[{DateTime.Now}] Starting single-pass ingestion + archiving...");

            // 1. Create blob clients for source & destination
            var sourceBlob = new BlobClient(_blobConnectionString, _blobContainerName, _blobFileName);
            var archiveBlob = new BlobClient(_blobConnectionString, _blobContainerName, _archiveFileName);

            // We'll keep references so we can delete the archive if something fails
            BlobClient? partialArchive = archiveBlob;

            Stream? sourceStream  = null;
            Stream? archiveStream = null;
            GZipStream? gzipStream = null;
            TeeStream? teeStream  = null;

            try
            {
                // 2. Open the source blob for reading
                sourceStream = await sourceBlob.OpenReadAsync();

                // 3. Open the archive blob for writing
                archiveStream = await archiveBlob.OpenWriteAsync(overwrite: true);

                // 4. Wrap that in a GZipStream for compression
                //    "leaveOpen: true" so we can control closing order
                gzipStream = new GZipStream(archiveStream, CompressionMode.Compress, leaveOpen: true);

                // 5. Create a TeeStream that reads from 'sourceStream'
                //    and copies each byte into 'gzipStream'
                teeStream = new TeeStream(sourceStream, gzipStream);

                // 6. Run ingestion reading from teeStream
                //    This call might throw if ingestion fails
                await IngestFromStreamAsync(teeStream);

                // If we reach here, ingestion succeeded. 
                // The archive is fully compressed once we dispose everything in 'finally' below.
                partialArchive = null; // null out to skip deletion
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Ingestion failed: {ex.Message}");
                
                // If partialArchive is not null, we delete the partial file
                if (partialArchive != null)
                {
                    try
                    {
                        Console.WriteLine("Deleting partial archive...");
                        await partialArchive.DeleteIfExistsAsync();
                    }
                    catch (Exception delEx)
                    {
                        Console.WriteLine($"[WARN] Could not delete partial archive: {delEx.Message}");
                    }
                }

                // Rethrow so the caller knows ingestion failed
                throw;
            }
            finally
            {
                // 7. Clean up streams in correct order
                //    Typically:
                //    1) TeeStream
                //    2) GZipStream (finalizes compression)
                //    3) archiveStream (finalizes upload)
                //    4) sourceStream
                teeStream?.Dispose(); 
                gzipStream?.Dispose();
                archiveStream?.Dispose();
                sourceStream?.Dispose();
            }

            Console.WriteLine($"[{DateTime.Now}] Ingestion succeeded. Archive created at '{_archiveFileName}'");
        }

        /// <summary>
        /// Reads lines from the provided stream, splits by '|', and does ingestion (COPY).
        /// If any exception is thrown here, the catch block in RunAsync() triggers deletion of partial archive.
        /// </summary>
        private async Task IngestFromStreamAsync(Stream stream)
        {
            using var reader = new StreamReader(stream, Encoding.UTF8);

            using var repository = new FileDataIngestionRepository(_postgresConnectionString);
            await repository.OpenConnectionAsync();

            var productBuffer = new StringBuilder();
            // ... other buffers for inventory, trade, deals, etc.

            const int BATCH_SIZE = 10_000;
            int productCount = 0;
            long totalLines  = 0;

            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                totalLines++;
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var columns    = line.Split('|');
                var recordType = columns[0];

                // Example logic
                if (recordType == "06")
                {
                    AppendLine(productBuffer, columns, 5, '|');
                    productCount++;
                    if (productCount >= BATCH_SIZE)
                    {
                        await repository.CopyChunkAsync("product", "(col1, col2, col3, col4, col5)", productBuffer, '|');
                        productBuffer.Clear();
                        productCount = 0;
                    }
                }
                // ... handle other record types ...
            }

            // Flush leftover
            if (productCount > 0)
            {
                await repository.CopyChunkAsync("product", "(col1, col2, col3, col4, col5)", productBuffer, '|');
            }

            Console.WriteLine($"Total lines ingested: {totalLines}");
        }

        private void AppendLine(StringBuilder buffer, string[] columns, int maxCols, char delim)
        {
            for (int i = 0; i < maxCols; i++)
            {
                if (i > 0) buffer.Append(delim);
                if (i < columns.Length) buffer.Append(columns[i]);
            }
            buffer.Append('\n');
        }
    }
}
using Npgsql;
using System;
using System.Text;
using System.Threading.Tasks;

namespace LargeFileIngestion
{
    public class FileDataIngestionRepository : IDisposable
    {
        private readonly string _connectionString;
        private NpgsqlConnection? _connection;

        public FileDataIngestionRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task OpenConnectionAsync()
        {
            _connection = new NpgsqlConnection(_connectionString);
            await _connection.OpenAsync();
        }

        public async Task CopyChunkAsync(string tableName, string columnList, StringBuilder chunkData, char delimiter)
        {
            if (_connection == null)
                throw new InvalidOperationException("Connection not opened. Call OpenConnectionAsync() first.");

            var copyCommand = 
                $"COPY {tableName} {columnList} FROM STDIN WITH (FORMAT text, DELIMITER '{delimiter}')";

            try
            {
                using var writer = await _connection.BeginTextImportAsync(copyCommand);
                await writer.WriteAsync(chunkData.ToString());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during COPY for {tableName}: {ex.Message}");
                throw;
            }
        }

        public void Dispose()
        {
            _connection?.Dispose();
        }
    }
}
using System;
using System.IO;

namespace LargeFileIngestion
{
    /// <summary>
    /// A read-only stream that duplicates all read bytes to a second stream.
    /// As you read from TeeStream, it writes the same bytes into _copyStream.
    /// </summary>
    public class TeeStream : Stream
    {
        private readonly Stream _sourceStream;
        private readonly Stream _copyStream;

        public TeeStream(Stream source, Stream copy)
        {
            _sourceStream = source ?? throw new ArgumentNullException(nameof(source));
            _copyStream   = copy   ?? throw new ArgumentNullException(nameof(copy));
        }

        public override bool CanRead  => _sourceStream.CanRead;
        public override bool CanSeek  => false;
        public override bool CanWrite => false;
        public override long Length   => _sourceStream.Length;
        public override long Position
        {
            get => _sourceStream.Position;
            set => throw new NotSupportedException();
        }

        public override void Flush() => _sourceStream.Flush();

        public override int Read(byte[] buffer, int offset, int count)
        {
            var bytesRead = _sourceStream.Read(buffer, offset, count);
            if (bytesRead > 0)
            {
                _copyStream.Write(buffer, offset, bytesRead);
            }
            return bytesRead;
        }

        public override long Seek(long offset, SeekOrigin origin)
            => throw new NotSupportedException();

        public override void SetLength(long value)
            => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
            => throw new NotSupportedException("TeeStream is read-only.");

        protected override void Dispose(bool disposing)
        {
            // Usually we do NOT dispose the child streams here,
            // because the caller might want to control their lifetimes explicitly.
            // But you could do so if you own them completely.

            base.Dispose(disposing);
        }
    }
}
