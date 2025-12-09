using ArchivalSystem.Abstraction;
using ArchivalSystem.Application.Interfaces;
using ArchivalSystem.Application.Models;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;


namespace ArchivalSystem.Infrastructure;

public class BlobStorageService(IStorageConnectionProvider connectionProvider, BlobStorageOptions? options = null)
    : IBlobStorageService
{

    private readonly IStorageConnectionProvider _connectionProvider = connectionProvider ?? throw new ArgumentNullException(nameof(connectionProvider));

    // Backward-compatible constructor for places that register options directly.
    public BlobStorageService(IOptions<BlobStorageOptions> options)
        : this(new StorageConnectionProvider(options ?? throw new ArgumentNullException(nameof(options))), options.Value)
    {
    }

    private BlobServiceClient GetServiceClient(string storageAccountName)
    {
        var connectionString = _connectionProvider.GetConnectionString(storageAccountName);
        return new BlobServiceClient(connectionString);
    }

    private BlobContainerClient GetContainerClient(string storageAccountName, string containerName)
    {
        var serviceClient = GetServiceClient(storageAccountName);
        return serviceClient.GetBlobContainerClient(containerName);
    }

    // Expose configured degree of parallelism (defaults to 16)
    public int GetDegreeOfParallelism() => options?.DegreeOfParallelism > 0 ? options.DegreeOfParallelism : 16;

    // Return account-specific options (throws if not configured)
    public BlobStorageAccountOptions GetAccountOptions(string storageAccountName)
    {
        if (options?.Accounts == null || !options.Accounts.Any())
            throw new InvalidOperationException("Blob storage accounts not configured.");

        var acct = options.Accounts.FirstOrDefault(a => a.StorageAccountName.Equals(storageAccountName, StringComparison.OrdinalIgnoreCase));
        if (acct == null) throw new InvalidOperationException($"No configuration found for storage account '{storageAccountName}'.");
        return acct;
    }


    public async Task<IReadOnlyList<ArchivalBlobInfo>> ListBlobsAsync(
        string storageAccountName,
        string containerName,
        string? prefix,
        CancellationToken ct = default)
    {
        var containerClient = GetContainerClient(storageAccountName, containerName);

        var result = new List<ArchivalBlobInfo>();

        await foreach (var blobItem in containerClient
                           .GetBlobsAsync(prefix: prefix, cancellationToken: ct))
        {
            result.Add(new ArchivalBlobInfo
            {
                StorageAccountName = storageAccountName,
                ContainerName = containerName,
                BlobPath = blobItem.Name,
                ETag = blobItem.Properties.ETag?.ToString(),
                ContentType = blobItem.Properties.ContentType,
                ContentLength = blobItem.Properties.ContentLength
            });
        }

        return result;
    }

    public async Task<ArchivalBlobInfo> UploadAsync(
        string storageAccountName,
        string containerName,
        string blobPath,
        string contentType,
        byte[] content,
        IDictionary<string, string> tags,
        CancellationToken ct = default)
    {
        var containerClient = GetContainerClient(storageAccountName, containerName);
        await containerClient.CreateIfNotExistsAsync(cancellationToken: ct);

        var blobClient = containerClient.GetBlobClient(blobPath);

        using var stream = new System.IO.MemoryStream(content, writable: false);
        var uploadOptions = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders
            {
                ContentType = contentType
            }
        };

        await blobClient.UploadAsync(stream, uploadOptions, ct);

        if (tags != null && tags.Count > 0)
        {
            await blobClient.SetTagsAsync(tags, cancellationToken: ct);
        }

        var properties = await blobClient.GetPropertiesAsync(cancellationToken: ct);

        return new ArchivalBlobInfo
        {
            StorageAccountName = storageAccountName,
            ContainerName = containerName,
            BlobPath = blobPath,
            ETag = properties.Value.ETag.ToString(),
            ContentType = properties.Value.ContentType,
            ContentLength = properties.Value.ContentLength
        };
    }

    public async Task SetTagsAsync(
        string storageAccountName,
        string containerName,
        string blobPath,
        IDictionary<string, string> tags,
        CancellationToken ct = default)
    {
        var containerClient = GetContainerClient(storageAccountName, containerName);
        var blobClient = containerClient.GetBlobClient(blobPath);

        await blobClient.SetTagsAsync(tags, cancellationToken: ct);
    }

    public async Task<IDictionary<string, string>> GetTagsAsync(
        string storageAccountName,
        string containerName,
        string blobPath,
        CancellationToken ct = default)
    {
        var containerClient = GetContainerClient(storageAccountName, containerName);
        var blobClient = containerClient.GetBlobClient(blobPath);

        try
        {
            var response = await blobClient.GetTagsAsync(cancellationToken: ct);
            return response.Value.Tags;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return new Dictionary<string, string>();
        }
    }

    public async Task<string?> GetAccessTierAsync(
        string storageAccountName,
        string containerName,
        string blobPath,
        CancellationToken ct = default)
    {
        var containerClient = GetContainerClient(storageAccountName, containerName);
        var blobClient = containerClient.GetBlobClient(blobPath);

        try
        {
            var properties = await blobClient.GetPropertiesAsync(cancellationToken: ct);
            return properties.Value.AccessTier?.ToString();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<Stream> OpenWriteStreamAsync(
        string storageAccountName,
        string containerName,
        string blobPath,
        bool overwrite = true,
        string? contentType = null,
        CancellationToken ct = default)
    {
        var containerClient = GetContainerClient(storageAccountName, containerName);
        await containerClient.CreateIfNotExistsAsync(cancellationToken: ct);

        var blobClient = containerClient.GetBlobClient(blobPath);

        var openOptions = new BlobOpenWriteOptions
        {
            HttpHeaders = new BlobHttpHeaders
            {
                ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType
            }
        };

        // OpenWriteAsync returns a stream that writes directly to the blob.
        // Caller is responsible for disposing/flushing the stream.
        var writeStream =
            await blobClient.OpenWriteAsync(overwrite: overwrite, options: openOptions, cancellationToken: ct);
        return writeStream;
    }

    public async Task<ArchivalBlobInfo> UploadFromStreamAsync(
            string storageAccountName,
            string containerName,
            string blobPath,
            string contentType,
            Func<Stream, CancellationToken, Task> writer,
            IDictionary<string, string>? tags = null,
            bool overwrite = true,
            CancellationToken ct = default)
    {
        if (writer == null) throw new ArgumentNullException(nameof(writer));
        if (string.IsNullOrWhiteSpace(storageAccountName))
            throw new ArgumentException("Storage account name required.", nameof(storageAccountName));
        if (string.IsNullOrWhiteSpace(containerName))
            throw new ArgumentException("Container name required.", nameof(containerName));
        if (string.IsNullOrWhiteSpace(blobPath))
            throw new ArgumentException("Blob path required.", nameof(blobPath));

        var containerClient = GetContainerClient(storageAccountName, containerName);
        await containerClient.CreateIfNotExistsAsync(cancellationToken: ct).ConfigureAwait(false);

        var blobClient = containerClient.GetBlobClient(blobPath);

        var openOptions = new BlobOpenWriteOptions
        {
            HttpHeaders = new BlobHttpHeaders
            {
                ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType
            }
        };

        // IMPORTANT: stream directly to storage via OpenWriteAsync - do not buffer the whole content locally.
        await using (var writeStream = await blobClient.OpenWriteAsync(overwrite: overwrite, options: openOptions, cancellationToken: ct).ConfigureAwait(false))
        {
            // Invoke the provided writer which writes directly into the blob stream.
            await writer(writeStream, ct).ConfigureAwait(false);

            // Ensure any buffered data in the stream is flushed to the service
            await writeStream.FlushAsync(ct).ConfigureAwait(false);
        }

        // Apply tags if provided (separate call). Setting tags is a separate REST call in Azure Blob Storage.
        if (tags != null && tags.Count > 0)
        {
            await blobClient.SetTagsAsync(tags, cancellationToken: ct).ConfigureAwait(false);
        }

        // Get final properties from the service (ETag, ContentLength, ContentType)
        var properties = await blobClient.GetPropertiesAsync(cancellationToken: ct).ConfigureAwait(false);

        return new ArchivalBlobInfo
        {
            StorageAccountName = storageAccountName,
            ContainerName = containerName,
            BlobPath = blobPath,
            ETag = properties.Value.ETag.ToString(),
            ContentType = properties.Value.ContentType,
            ContentLength = properties.Value.ContentLength
        };
    }


    public async Task DeleteIfExistsAsync(string storageAccountName, string containerName, string blobPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(storageAccountName)) throw new ArgumentNullException(nameof(storageAccountName));
        if (string.IsNullOrWhiteSpace(containerName)) throw new ArgumentNullException(nameof(containerName));
        if (string.IsNullOrWhiteSpace(blobPath)) throw new ArgumentNullException(nameof(blobPath));

        var container = GetContainerClient(storageAccountName, containerName);
        var blob = container.GetBlobClient(blobPath);

        try
        {
            await blob.DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: ct);
        }
        catch (RequestFailedException rfe)
        {
            throw new InvalidOperationException($"Failed deleting blob '{containerName}/{blobPath}': {rfe.Message}", rfe);
        }
    }

    public async Task SetAccessTierAsync(string storageAccountName, string containerName, string blobPath, string tier, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(storageAccountName)) throw new ArgumentNullException(nameof(storageAccountName));
        if (string.IsNullOrWhiteSpace(containerName)) throw new ArgumentNullException(nameof(containerName));
        if (string.IsNullOrWhiteSpace(blobPath)) throw new ArgumentNullException(nameof(blobPath));
        if (string.IsNullOrWhiteSpace(tier)) throw new ArgumentNullException(nameof(tier));

        var container = GetContainerClient(storageAccountName, containerName);
        var blob = container.GetBlobClient(blobPath);

        AccessTier accessTier;
        if (string.Equals(tier, "Archive", StringComparison.OrdinalIgnoreCase)) accessTier = AccessTier.Archive;
        else if (string.Equals(tier, "Cool", StringComparison.OrdinalIgnoreCase)) accessTier = AccessTier.Cool;
        else if (string.Equals(tier, "Hot", StringComparison.OrdinalIgnoreCase)) accessTier = AccessTier.Hot;
        else accessTier = new AccessTier(tier);

        try
        {
            await blob.SetAccessTierAsync(accessTier, cancellationToken: ct);
        }
        catch (RequestFailedException rfe)
        {
            throw new InvalidOperationException($"Failed setting access tier '{tier}' on blob '{containerName}/{blobPath}': {rfe.Message}", rfe);
        }
    }
}
