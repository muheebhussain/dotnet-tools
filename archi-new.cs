using ArchivalSystem.Application.Interfaces;
using ArchivalSystem.Application.Models;
using ArchivalSystem.Data;
using ArchivalSystem.Data.Entities;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace ArchivalSystem.Infrastructure;

public class SourceExporter(
    IParquetExportService parquetExportService,
    ILifecyclePolicyResolver lifecyclePolicyResolver,
    IArchivalFileRepository archivalFileRepository,
    IBlobStorageService blobStorage,
    ITaggingService taggingService,
    IClock clock,
    ILogger<SourceExporter> logger)
    : ISourceExporter
{
    private readonly IParquetExportService _parquetExportService = parquetExportService ?? throw new ArgumentNullException(nameof(parquetExportService));
    private readonly ILifecyclePolicyResolver _lifecyclePolicyResolver = lifecyclePolicyResolver ?? throw new ArgumentNullException(nameof(lifecyclePolicyResolver));
    private readonly IArchivalFileRepository _archivalFileRepository = archivalFileRepository ?? throw new ArgumentNullException(nameof(archivalFileRepository));
    private readonly IBlobStorageService _blobStorage = blobStorage ?? throw new ArgumentNullException(nameof(blobStorage));
    private readonly ITaggingService _taggingService = taggingService ?? throw new ArgumentNullException(nameof(taggingService));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly ILogger<SourceExporter> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    // Fixed rows per part as requested
    private const int MaxRowsPerPart = 50_000;

    // Simple retry settings for upload/upsert transient failures
    private const int MaxUploadAttempts = 3;
    private static readonly TimeSpan UploadRetryBaseDelay = TimeSpan.FromSeconds(2);

    public async Task<ParquetExportResult> ExportAsync(
        ArchivalTableConfigurationDto tableConfig,
        DateTime asOfDate,
        DateType dateType,
        long runId,
        CancellationToken ct = default)
    {
        if (tableConfig == null) throw new ArgumentNullException(nameof(tableConfig));
        if (string.IsNullOrWhiteSpace(tableConfig.AsOfDateColumn))
            throw new InvalidOperationException($"Table configuration {tableConfig.Id} has no as_of_date_column defined.");

        var blobPathBase = BlobStorageHelper.BuildBlobPath(tableConfig, asOfDate, dateType);

        var (policy, azurePolicyTag) = await _lifecyclePolicyResolver.ResolvePolicyForTableAsync(tableConfig.Id, ct);
        var isFileExempt = await _archivalFileRepository.IsFileExemptAsync(tableConfig.Id, asOfDate, ct);

        // Determine whether tags should be applied for this storage account
        var accountOptions = _blobStorage.GetAccountOptions(tableConfig.StorageAccountName);
        var setTagsAllowed = accountOptions?.SetTagsAllowed ?? false;

        var parts = new List<ArchivalBlobInfo>();
        long aggregatedRows = 0;
        long aggregatedSize = 0;
        int columnCount = 0;

        // Request parts from parquet service using fixed max rows per part
        var partsResult = await _parquetExportService.ExportTableToPartsAsync(
            tableConfig.DatabaseName,
            tableConfig.SchemaName,
            tableConfig.TableName,
            tableConfig.AsOfDateColumn!,
            asOfDate.Date,
            (partIndex, writer, ctPart) => UploadPartAsync(
                blobPathBase,
                tableConfig,
                asOfDate,
                dateType,
                azurePolicyTag,
                isFileExempt,
                setTagsAllowed,
                partIndex,
                writer,
                ctPart),
            maxRowsPerPart: MaxRowsPerPart,
            ct: ct);

        // After export completes we have per-part metrics in partsResult.
        // Update per-part archival_file rows with RowCount/FileSize/ETag and collect aggregates.
        var modifiedFiles = new List<ArchivalFileEntity>();
        foreach (var pr in partsResult)
        {
            var blob = pr.BlobInfo;
            if (blob == null) continue;

            // Try to find the DB row by blob path
            var fileEntity = await _archivalFileRepository.GetByBlobPathAsync(tableConfig.Id, blob.BlobPath, ct);

            if (fileEntity == null)
            {
                // If not found, create a minimal record
                fileEntity = new ArchivalFileEntity
                {
                    TableConfigurationId = tableConfig.Id,
                    AsOfDate = asOfDate.Date,
                    DateType = dateType,
                    StorageAccountName = tableConfig.StorageAccountName,
                    ContainerName = tableConfig.ContainerName,
                    BlobPath = blob.BlobPath,
                    Etag = blob.ETag,
                    ContentType = blob.ContentType,
                    FileSizeBytes = blob.ContentLength,
                    RowCount = pr.Metrics?.RowCount ?? 0,
                    Status = ArchivalFileStatus.Active,
                    CreatedAtEt = _clock.Now.UtcDateTime,
                    ArchivalPolicyTag = azurePolicyTag,
                    LastTagsSyncAtEt = setTagsAllowed ? _clock.Now.UtcDateTime : (DateTime?)null,
                    OverrideFileLifecyclePolicyId = tableConfig.FileLifecyclePolicyId
                };

                await _archivalFileRepository.UpsertFileAsync(fileEntity, ct);
            }
            else
            {
                fileEntity.Etag = blob.ETag;
                fileEntity.ContentType = blob.ContentType;
                fileEntity.FileSizeBytes = blob.ContentLength;
                fileEntity.RowCount = pr.Metrics?.RowCount ?? fileEntity.RowCount;
                fileEntity.Status = ArchivalFileStatus.Active;
                fileEntity.ArchivalPolicyTag = azurePolicyTag;
                fileEntity.LastTagsSyncAtEt = setTagsAllowed ? _clock.Now.UtcDateTime : fileEntity.LastTagsSyncAtEt;

                modifiedFiles.Add(fileEntity);
            }

            aggregatedRows += pr.Metrics?.RowCount ?? 0;
            aggregatedSize += pr.Metrics?.SizeBytes ?? 0;
            columnCount = pr.Metrics?.ColumnCount ?? columnCount;
            parts.Add(blob);
        }

        // Persist any modified files in bulk
        if (modifiedFiles.Count > 0)
        {
            await _archivalFileRepository.UpdateFilesAsync(modifiedFiles, ct);
        }

        var aggregatedMetrics = new ParquetExportMetrics
        {
            RowCount = aggregatedRows,
            ColumnCount = columnCount,
            SizeBytes = aggregatedSize
        };

        return new ParquetExportResult
        {
            Metrics = aggregatedMetrics,
            BlobInfo = parts.Count == 1 ? parts[0] : null,
            Parts = parts.ToArray(),
            AzurePolicyTag = azurePolicyTag
        };
    }

    private async Task<ArchivalBlobInfo> UploadPartAsync(
        string blobPathBase,
        ArchivalTableConfigurationDto tableConfig,
        DateTime asOfDate,
        DateType dateType,
        string? azurePolicyTag,
        bool isFileExempt,
        bool setTagsAllowed,
        int partIndex,
        Func<Stream, CancellationToken, Task> writer,
        CancellationToken ct)
    {
        var partPath = $"{blobPathBase}/part-{partIndex:D5}.parquet";

        // Use IClock for UTC timestamp as requested
        var now = _clock.Now.DateTime;

        // Create initial DB row for idempotency
        var partEntity = new ArchivalFileEntity
        {
            TableConfigurationId = tableConfig.Id,
            AsOfDate = asOfDate.Date,
            DateType = dateType,
            StorageAccountName = tableConfig.StorageAccountName,
            ContainerName = tableConfig.ContainerName,
            BlobPath = partPath,
            Etag = null,
            ContentType = "application/octet-stream",
            FileSizeBytes = null,
            RowCount = null,
            Status = ArchivalFileStatus.Created,
            CreatedAtEt = now,
            ArchivalPolicyTag = azurePolicyTag,
            LastTagsSyncAtEt = null,
            OverrideFileLifecyclePolicyId = tableConfig.FileLifecyclePolicyId
        };

        // Upsert initial entity (get Id)
        var savedEntity = await _archivalFileRepository.UpsertFileAsync(partEntity, ct);

        IDictionary<string, string>? tags = null;
        if (setTagsAllowed)
        {
            tags = _tagging_service_buildsafe(tableConfig.Id, asOfDate, dateType, azurePolicyTag, isFileExempt);
            tags["archival_part_index"] = partIndex.ToString();
        }

        // Upload with simple retry for transient failures
        ArchivalBlobInfo blobInfo = null!;
        Exception? lastEx = null;
        for (int attempt = 1; attempt <= MaxUploadAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                blobInfo = await _blobStorage.UploadFromStreamAsync(
                    tableConfig.StorageAccountName,
                    tableConfig.ContainerName,
                    partPath,
                    contentType: "application/octet-stream",
                    writer: writer,
                    tags: tags,
                    overwrite: true,
                    ct: ct);

                lastEx = null;
                break;
            }
            catch (Exception ex) when (attempt < MaxUploadAttempts)
            {
                lastEx = ex;
                var delay = TimeSpan.FromMilliseconds(UploadRetryBaseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));
                _logger.LogWarning(ex, "Upload attempt {Attempt} failed for {Part}. Retrying after {Delay}.", attempt, partPath, delay);
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
        }

        if (lastEx != null) // all attempts failed
            throw lastEx;

        // Update saved entity with final properties and mark tags sync time if tags were applied
        savedEntity.Etag = blobInfo.ETag;
        savedEntity.ContentType = blobInfo.ContentType;
        savedEntity.FileSizeBytes = blobInfo.ContentLength;
        savedEntity.RowCount = null;
        savedEntity.LastTagsSyncAtEt = setTagsAllowed ? _clock.Now.DateTime : (DateTime?)null;
        savedEntity.ArchivalPolicyTag = azurePolicyTag;
        savedEntity.Status = ArchivalFileStatus.Active;

        // Upsert final values
        await _archivalFileRepository.UpsertFileAsync(savedEntity, ct);

        return blobInfo;
    }

    // wrapper to call taggingService.BuildTags but defensively catch user errors in tagging logic
    private IDictionary<string, string> _tagging_service_buildsafe(int tableConfigurationId, DateTime asOfDate, DateType dateType, string? azurePolicyTag, bool isFileExempt)
    {
        try
        {
            return _taggingService.BuildTags(tableConfigurationId, asOfDate, dateType, azurePolicyTag, isFileExempt) ?? new Dictionary<string, string>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tagging service failed to build tags for table {TableId} date {Date}. Proceeding without tags.", tableConfigurationId, asOfDate.ToString("yyyy-MM-dd"));
            return new Dictionary<string, string>();
        }
    }
}
