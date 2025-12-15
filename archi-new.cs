/// <summary>
/// Returns files for a single table configuration that are plausible candidates for lifecycle actions.
/// Similar conservative pruning as GetFilesForStorageAccountAsync but scoped to one table configuration.
/// </summary>
public async Task<List<ArchivalFileEntity>> GetFilesForTableConfigurationAsync(
    int tableConfigurationId,
    bool asTracking = false,
    int pageSize = 5000,
    TimeSpan? minAgeBetweenTierChecks = null,
    CancellationToken ct = default)
{
    if (tableConfigurationId <= 0) throw new ArgumentOutOfRangeException(nameof(tableConfigurationId));
    var minCheckSpan = minAgeBetweenTierChecks ?? TimeSpan.FromDays(1);
    var now = DateTime.UtcNow;

    // Resolve the table configuration and its resolved lifecycle policy (primary and override)
    var tableConfig = await _db.ArchivalTableConfigurations
        .Include(t => t.FileLifecyclePolicy)
        .Include(t => t.TableRetentionPolicy)
        .FirstOrDefaultAsync(t => t.Id == tableConfigurationId, ct);

    if (tableConfig == null)
        return new List<ArchivalFileEntity>(0);

    // Collect threshold day values from the table's primary policy and any override policy referenced by files (conservative)
    var candidateDayValues = new List<int>();

    void AddIf(int? v) { if (v.HasValue) candidateDayValues.Add(v.Value); }

    var primary = tableConfig.FileLifecyclePolicy;
    if (primary != null && primary.IsActive)
    {
        AddIf(primary.EodCoolDays); AddIf(primary.EodArchiveDays); AddIf(primary.EodDeleteDays);
        AddIf(primary.EomCoolDays); AddIf(primary.EomArchiveDays); AddIf(primary.EomDeleteDays);
        AddIf(primary.EoqCoolDays); AddIf(primary.EoqArchiveDays); AddIf(primary.EoqDeleteDays);
        AddIf(primary.EoyCoolDays); AddIf(primary.EoyArchiveDays); AddIf(primary.EoyDeleteDays);
        AddIf(primary.ExternalCoolDays); AddIf(primary.ExternalArchiveDays); AddIf(primary.ExternalDeleteDays);
    }

    // If no thresholds discovered on the table primary policy, fall back to global active policies (conservative)
    if (!candidateDayValues.Any())
    {
        var activeGlobal = await _db.ArchivalFileLifecyclePolicies
            .Where(p => p.IsActive)
            .Select(p => new
            {
                p.EodCoolDays, p.EodArchiveDays, p.EodDeleteDays,
                p.EomCoolDays, p.EomArchiveDays, p.EomDeleteDays,
                p.EoqCoolDays, p.EoqArchiveDays, p.EoqDeleteDays,
                p.EoyCoolDays, p.EoyArchiveDays, p.EoyDeleteDays,
                p.ExternalCoolDays, p.ExternalArchiveDays, p.ExternalDeleteDays
            })
            .ToListAsync(ct);

        foreach (var p in activeGlobal)
        {
            void AddG(int? v) { if (v.HasValue) candidateDayValues.Add(v.Value); }
            AddG(p.EodCoolDays); AddG(p.EodArchiveDays); AddG(p.EodDeleteDays);
            AddG(p.EomCoolDays); AddG(p.EomArchiveDays); AddG(p.EomDeleteDays);
            AddG(p.EoqCoolDays); AddG(p.EoqArchiveDays); AddG(p.EoqDeleteDays);
            AddG(p.EoyCoolDays); AddG(p.EoyArchiveDays); AddG(p.EoyDeleteDays);
            AddG(p.ExternalCoolDays); AddG(p.ExternalArchiveDays); AddG(p.ExternalDeleteDays);
        }
    }

    if (!candidateDayValues.Any())
        return new List<ArchivalFileEntity>(0);

    var minThresholdDays = candidateDayValues.Min();
    var cutoffDate = now.Date.AddDays(-minThresholdDays);

    IQueryable<ArchivalFileEntity> q = _db.ArchivalFiles;

    if (!asTracking) q = q.AsNoTracking();

    q = q.Where(f => f.TableConfigurationId == tableConfigurationId && f.Status != ArchivalFileStatus.Deleted);

    // Conservative filter similar to storage-account method:
    q = q.Where(f =>
        (f.AsOfDate.HasValue && f.AsOfDate.Value.Date <= cutoffDate)
        || (!f.AsOfDate.HasValue && f.CreatedAtEt.Date <= cutoffDate)
        || f.LastTierCheckAtEt == null
        || f.LastTierCheckAtEt <= now.Subtract(minCheckSpan));

    // include related table configuration and override policy to avoid N+1 in enforcer
    q = q.Include(f => f.TableConfiguration)
         .ThenInclude(t => t.FileLifecyclePolicy)
         .Include(f => f.OverrideFileLifecyclePolicy);

    var list = await q.OrderBy(f => f.LastTierCheckAtEt ?? DateTime.MinValue)
                      .ThenBy(f => f.AsOfDate)
                      .Take(pageSize)
                      .ToListAsync(ct);

    return list;
}
/// <summary>
/// Lifecycle enforcer updated to ask the repository for files per table configuration.
/// This reduces scanning of unrelated files and allows per-table policy resolution to be applied efficiently.
/// </summary>
public class ArchivalFileLifecycleEnforcer(
    IArchivalFileRepository archivalFileRepository,
    IArchivalTableConfigurationRepository tableConfigRepository,
    IBlobStorageService blobStorage,
    ILifecyclePolicyResolver lifecyclePolicyResolver,
    ILogger<ArchivalFileLifecycleEnforcer> logger)
    : IArchivalFileLifecycleEnforcer
{
    private readonly IArchivalFileRepository _fileRepository = archivalFileRepository ?? throw new ArgumentNullException(nameof(archivalFileRepository));
    private readonly IArchivalTableConfigurationRepository _tableConfigRepository = tableConfigRepository ?? throw new ArgumentNullException(nameof(tableConfigRepository));
    private readonly IBlobStorageService _blobStorage = blobStorage ?? throw new ArgumentNullException(nameof(blobStorage));
    private readonly ILifecyclePolicyResolver _lifecyclePolicyResolver = lifecyclePolicyResolver ?? throw new ArgumentNullException(nameof(lifecyclePolicyResolver));
    private readonly ILogger<ArchivalFileLifecycleEnforcer> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Enforce lifecycle actions for all files in the given storage account (optionally narrowed).
    /// Implementation now iterates the table-configurations that target the storage account and requests
    /// candidate files per-table using GetFilesForTableConfigurationAsync.
    /// </summary>
    public async Task<LifecycleEnforcerResult> EnforceAsync(
        string storageAccountName,
        string? containerName = null,
        string? prefix = null,
        bool dryRun = false,
        long? runId = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(storageAccountName)) throw new ArgumentNullException(nameof(storageAccountName));

        // Get table configs that reference this storage account (conservative set).
        // We rely on the table configuration repository to expose a method that returns table configs by storage account.
        // If your repository method has a different name, adapt the call accordingly.
        var tableConfigs = await _tableConfigRepository.GetByStorageAccountAsync(storageAccountName, containerName, ct);

        var totalProcessed = 0;
        var actions = new List<string>();

        foreach (var tc in tableConfigs)
        {
            ct.ThrowIfCancellationRequested();

            // Fetch candidate files for this table using the new repository API.
            var candidates = await _fileRepository.GetFilesForTableConfigurationAsync(
                tc.Id,
                asTracking: false,
                pageSize: 5000,
                minAgeBetweenTierChecks: TimeSpan.FromDays(1),
                ct: ct);

            if (candidates == null || candidates.Count == 0) continue;

            // Resolve policy for the table once
            var (policyDto, azurePolicyTag) = await _lifecyclePolicyResolver.ResolvePolicyForTableAsync(tc.Id, ct);

            foreach (var f in candidates)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    // Determine desired action (example: set access tier or tag). Keep logic small here.
                    // This sample simply logs and optionally sets the access tier according to resolved policy.
                    var desiredTier = policyDto?.GetDesiredTierForFile(f) ?? null;

                    if (!dryRun && !string.IsNullOrWhiteSpace(desiredTier))
                    {
                        await _blobStorage.SetAccessTierAsync(f.StorageAccountName, f.ContainerName, f.BlobPath, desiredTier, ct);
                    }

                    totalProcessed++;
                    actions.Add($"Table={tc.Id} File={f.Id} Action=TierSet({desiredTier ?? "none"})");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed enforcing lifecycle for file {FileId} (table {TableId}). Continuing.", f.Id, tc.Id);
                }
            }
        }

        // Summarize
        _logger.LogInformation("Lifecycle enforcement completed for storage account {Account}. Files processed={Count}", storageAccountName, totalProcessed);

        return new LifecycleEnforcerResult
        {
            FilesProcessed = totalProcessed,
            Actions = actions
        };
    }
}
