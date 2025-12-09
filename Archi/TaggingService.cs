using ArchivalSystem.Application.Interfaces;
using ArchivalSystem.Application.Models;
using ArchivalSystem.Data;

namespace ArchivalSystem.Infrastructure;

public interface ITaggingService
{
    IDictionary<string, string> BuildTags(int tableConfigurationId, DateTime? asOfDate, DateType? dateType, string? azurePolicyTag, bool isExempt);
    Task SetTagsAsync(ArchivalBlobInfo blob, IDictionary<string, string> tags, CancellationToken ct = default);
}
public sealed class TaggingService : ITaggingService
{
    private readonly IBlobStorageService _blobStorage;

    public TaggingService(IBlobStorageService blobStorage)
    {
        _blobStorage = blobStorage ?? throw new ArgumentNullException(nameof(blobStorage));
    }

    public IDictionary<string, string> BuildTags(int tableConfigurationId, DateTime? asOfDate, DateType? dateType, string? azurePolicyTag, bool isExempt)
    {
        var tags = new Dictionary<string, string>
        {
            ["archival_table_configuration_id"] = tableConfigurationId.ToString(),
            ["archival_policy"] = azurePolicyTag ?? string.Empty,
            ["archival_exempt"] = isExempt ? "true" : "false"
        };

        if (asOfDate.HasValue)
            tags["archival_date"] = asOfDate.Value.ToString("yyyy-MM-dd");

        if (dateType.HasValue)
            tags["archival_date_type"] = dateType.Value.ToString();

        return tags;
    }

    public Task SetTagsAsync(ArchivalBlobInfo blob, IDictionary<string, string> tags, CancellationToken ct = default)
        => _blobStorage.SetTagsAsync(blob.StorageAccountName, blob.ContainerName, blob.BlobPath, tags, ct);
}