  // <summary>
 /// Small executor that drives lifecycle enforcement runs.
 /// - Discovers storage accounts from the metadata DB and runs the enforcer per-account.
 /// - Also exposes existing account / list entry points.
 /// - When a run repository is supplied, creates a run per-account and logs a summary detail row.
 /// </summary>
 public sealed class ArchivalLifecycleExecutor(
     IArchivalFileLifecycleEnforcer enforcer,
     IArchivalTableConfigurationRepository tableConfigRepository,
     IClock clock,
     ILogger<ArchivalLifecycleExecutor> logger,
     IArchivalRunRepository? runRepository = null)
 {
     private readonly IArchivalFileLifecycleEnforcer _enforcer = enforcer ?? throw new ArgumentNullException(nameof(enforcer));
     private readonly IArchivalTableConfigurationRepository _tableConfigRepository = tableConfigRepository ?? throw new ArgumentNullException(nameof(tableConfigRepository));
     private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
     private readonly ILogger<ArchivalLifecycleExecutor> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
     private readonly IArchivalRunRepository? _runRepository = runRepository;

     public async Task<LifecycleEnforcerResult> ExecuteForAccountAsync(
         string storageAccountName,
         string? containerName = null,
         string? prefix = null,
         bool dryRun = false,
         CancellationToken ct = default)
     {
         if (string.IsNullOrWhiteSpace(storageAccountName)) throw new ArgumentNullException(nameof(storageAccountName));

         var runStartedAt = _clock.Now;
         _logger.LogInformation("Starting lifecycle enforcement executor for account={Account} container={Container} prefix={Prefix} dryRun={DryRun} at {Start}",
             storageAccountName, containerName ?? "(any)", prefix ?? "(any)", dryRun, runStartedAt);

         ArchivalRunEntity? run = null;
         long? runId = null;

         if (_runRepository != null)
         {
             try
             {
                 run = await _runRepository.StartRunAsync($"Executor: lifecycle enforcement for {storageAccountName}/{containerName ?? "*"} prefix={prefix}", ct);
                 runId = run.Id;
             }
             catch (Exception ex)
             {
                 _logger.LogWarning(ex, "Failed to create run record. Proceeding without persistent run.");
                 run = null;
                 runId = null;
             }
         }

         LifecycleEnforcerResult result;
         try
         {
             // pass the runId to the enforcer so it can record run-detail rows against this run
             result = await _enforcer.EnforceAsync(storageAccountName, containerName, prefix, dryRun, runId, ct);
         }
         catch (OperationCanceledException)
         {
             _logger.LogInformation("Lifecycle enforcement for account={Account} was cancelled.", storageAccountName);
             throw;
         }
         catch (Exception ex)
         {
             _logger.LogError(ex, "Lifecycle enforcement failed for account={Account}.", storageAccountName);

             if (runId.HasValue)
             {
                 try
                 {
                     await _runRepository.CompleteRunAsync(runId.Value, RunStatus.Failed, ex.Message, ct);
                 }
                 catch (Exception e)
                 {
                     _logger.LogWarning(e, "Failed to complete run {RunId}", runId);
                 }
             }

             return new LifecycleEnforcerResult(0, 0, 1);
         }

         _logger.LogInformation("Lifecycle enforcement finished for account={Account}. Tiered={Tiered}, Deleted={Deleted}, Failed={Failed}",
             storageAccountName, result.Tiered, result.Deleted, result.Failed);

         if (runId.HasValue)
         {
             var status = result.Failed > 0 ? RunStatus.Partial : RunStatus.Success;
             try
             {
                 await _runRepository.CompleteRunAsync(runId.Value, status, $"Tiered={result.Tiered} Deleted={result.Deleted} Failed={result.Failed}", ct);
             }
             catch (Exception ex)
             {
                 _logger.LogWarning(ex, "Failed to complete run {RunId}", runId);
             }
         }

         return result;
     }

     public async Task<Dictionary<string, LifecycleEnforcerResult>> ExecuteForAllAccountsFromDbAsync(
         string? containerName = null,
         string? prefix = null,
         bool dryRun = false,
         int maxDegreeOfParallelism = 1,
         CancellationToken ct = default)
     {
         IEnumerable<(string StorageAccountName, string ContainerName)> pairs;
         try
         {
             var pairList = await _tableConfigRepository.GetDistinctActiveStorageAccountContainerPairsAsync(ct);
             pairs = pairList ?? Enumerable.Empty<(string, string)>();
         }
         catch (MissingMethodException)
         {
             _logger.LogInformation("Repository does not provide account/container pairs; falling back to account-only discovery.");
             IEnumerable<string> accounts;
             try
             {
                 accounts = await _tableConfigRepository.GetDistinctActiveStorageAccountNamesAsync(ct);
             }
             catch (MissingMethodException)
             {
                 var configs = await _tableConfigRepository.GetAllActiveAsync(ct);
                 accounts = configs.Select(c => c.StorageAccountName).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase);
             }

             pairs = accounts.Select(a => (StorageAccountName: a, ContainerName: (string?)null!));
         }

         var result = new Dictionary<string, LifecycleEnforcerResult>(StringComparer.OrdinalIgnoreCase);
         var locker = new object();

         await Parallel.ForEachAsync(pairs, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, maxDegreeOfParallelism), CancellationToken = ct }, async (pair, token) =>
         {
             try
             {
                 token.ThrowIfCancellationRequested();

                 var effectiveContainer = string.IsNullOrWhiteSpace(containerName) ? (pair.ContainerName == null ? null : pair.ContainerName) : containerName;
                 var acctKey = effectiveContainer == null
                     ? pair.StorageAccountName
                     : $"{pair.StorageAccountName}/{effectiveContainer}";

                 var r = await ExecuteForAccountAsync(pair.StorageAccountName, effectiveContainer, prefix, dryRun, token);
                 lock (locker) result[acctKey] = r;
             }
             catch (Exception ex)
             {
                 var acctKey = pair.ContainerName == null ? pair.StorageAccountName : $"{pair.StorageAccountName}/{pair.ContainerName}";
                 _logger.LogError(ex, "Executor failed for account/container={Acct}. Storing failure result.", acctKey);
                 lock (locker) result[acctKey] = new LifecycleEnforcerResult(0, 0, 1);
             }
         });

         return result;
     }

     public async Task<Dictionary<int, LifecycleEnforcerResult>> ExecutePerTableConfigurationAsync(
         IEnumerable<int>? tableConfigurationIds = null,
         bool dryRun = false,
         int maxDegreeOfParallelism = 4,
         CancellationToken ct = default)
     {
         List<ArchivalTableConfigurationEntity> configs;
         if (tableConfigurationIds == null)
         {
             configs = await _tableConfigRepository.GetAllActiveAsync(ct);
         }
         else
         {
             configs = new List<ArchivalTableConfigurationEntity>();
             foreach (var id in tableConfigurationIds)
             {
                 var cfg = await _tableConfigRepository.GetWithRelatedAsync(id, ct);
                 if (cfg != null) configs.Add(cfg);
             }
         }

         var results = new Dictionary<int, LifecycleEnforcerResult>();
         var locker = new object();

         await Parallel.ForEachAsync(configs, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, maxDegreeOfParallelism), CancellationToken = ct }, async (cfg, token) =>
         {
             try
             {
                 var prefix = cfg.DiscoveryPathPrefix;
                 var r = await ExecuteForAccountAsync(cfg.StorageAccountName, cfg.ContainerName, prefix, dryRun, token);
                 lock (locker) results[cfg.Id] = r;
             }
             catch (Exception ex)
             {
                 _logger.LogError(ex, "Executor failed for table config id={Id}. Storing failure result.", cfg.Id);
                 lock (locker) results[cfg.Id] = new LifecycleEnforcerResult(0, 0, 1);
             }
         });

         return results;
     }
 }
public interface IArchivalFileLifecycleEnforcer
{
    /// <summary>
    /// Enforce lifecycle actions for all files in the given storage account.
    /// If containerName or prefix is provided, the scope is narrowed.
    /// dryRun = true will not apply blob changes, only log actions and return counts.
    /// Optionally supply an existing runId (executor can create the run and pass the id).
    /// When runId is provided the enforcer will only emit run-detail rows; the caller is responsible for completing the run.
    /// When runId is null the enforcer will create and complete its own run.
    /// </summary>
    Task<LifecycleEnforcerResult> EnforceAsync(
        string storageAccountName,
        string? containerName = null,
        string? prefix = null,
        bool dryRun = false,
        long? runId = null,
        CancellationToken ct = default);
}

 public sealed class ArchivalFileLifecycleEnforcer(
    IArchivalFileRepository fileRepository,
    IArchivalTableConfigurationRepository tableConfigRepository,
    IArchivalFileLifecyclePolicyRepository policyRepository,
    IBlobStorageService blobStorage,
    IArchivalRunRepository runRepository,
    ILogger<ArchivalFileLifecycleEnforcer> logger,
    IClock clock,
    int degreeOfParallelism = 8,
    TimeSpan? minAgeBetweenTierChecks = null) : IArchivalFileLifecycleEnforcer
 {
     private readonly IArchivalFileRepository _fileRepository = fileRepository ?? throw new ArgumentNullException(nameof(fileRepository));
     private readonly IArchivalTableConfigurationRepository _tableConfigRepository = tableConfigRepository ?? throw new ArgumentNullException(nameof(tableConfigRepository));
     private readonly IArchivalFileLifecyclePolicyRepository _policyRepository = policyRepository ?? throw new ArgumentNullException(nameof(policyRepository));
     private readonly IBlobStorageService _blobStorage = blobStorage ?? throw new ArgumentNullException(nameof(blobStorage));
     private readonly IArchivalRunRepository _runRepository = runRepository ?? throw new ArgumentNullException(nameof(runRepository));
     private readonly ILogger<ArchivalFileLifecycleEnforcer> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
     private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));

     // Tunables
     private readonly int _degreeOfParallelism = Math.Max(1, degreeOfParallelism);
     private readonly TimeSpan _minAgeBetweenTierChecks = minAgeBetweenTierChecks ?? TimeSpan.FromDays(1);

     public async Task<LifecycleEnforcerResult> EnforceAsync(
         string storageAccountName,
         string? containerName = null,
         string? prefix = null,
         bool dryRun = false,
         long? runId = null,
         CancellationToken ct = default)
     {
         if (string.IsNullOrWhiteSpace(storageAccountName)) throw new ArgumentNullException(nameof(storageAccountName));

         _logger.LogInformation("Starting lifecycle enforcement for account='{Account}' container='{Container}' prefix='{Prefix}' (dryRun={DryRun})",
             storageAccountName, containerName ?? "*", prefix ?? "*", dryRun);

         // Possibly create a run if caller didn't supply one
         var startedRun = (ArchivalRunEntity?)null;
         var runIdToUse = runId;
         var createdRunByUs = false;
         if (!runIdToUse.HasValue)
         {
             try
             {
                 startedRun = await _runRepository.StartRunAsync($"Lifecycle enforcement for {storageAccountName}/{containerName ?? "*"} (prefix={prefix})", ct);
                 runIdToUse = startedRun.Id;
                 createdRunByUs = true;
             }
             catch (Exception ex)
             {
                 _logger.LogWarning(ex, "Failed to create lifecycle run record. Proceeding without run; details will be logged locally.");
                 runIdToUse = null;
                 createdRunByUs = false;
             }
         }

         // 1) Load DB candidate rows (smart repository filter handles cutoff + last-checked)
         var candidates = await _fileRepository.GetFilesForStorageAccountAsync(
             storageAccountName,
             containerName,
             prefix,
             asTracking: false,
             pageSize: 5000,
             minAgeBetweenTierChecks: _minAgeBetweenTierChecks,
             ct: ct);

         if (candidates == null || candidates.Count == 0)
         {
             _logger.LogInformation("No archival files found for enforcement (account={Account}, container={Container}, prefix={Prefix}).",
                 storageAccountName, containerName ?? "(any)", prefix ?? "(any)");

             if (createdRunByUs && runIdToUse.HasValue)
             {
                 try
                 {
                     await _runRepository.CompleteRunAsync(runIdToUse.Value, RunStatus.Success, "No candidates found", ct);
                 }
                 catch (Exception ex)
                 {
                     _logger.LogWarning(ex, "Failed to complete run {RunId}", runIdToUse);
                 }
             }

             return new LifecycleEnforcerResult(0, 0, 0);
         }

         // 2) Preload table configs and override policies (per earlier implementation)
         var tableConfigIds = candidates.Select(f => f.TableConfigurationId).Distinct().ToList();
         var tableConfigs = new Dictionary<int, ArchivalTableConfigurationEntity>();
         foreach (var tid in tableConfigIds)
         {
             ct.ThrowIfCancellationRequested();
             var cfg = await _tableConfigRepository.GetWithRelatedAsync(tid, ct);
             if (cfg != null) tableConfigs[tid] = cfg;
         }

         var overridePolicyIds = candidates.Where(f => f.OverrideFileLifecyclePolicyId.HasValue)
                                           .Select(f => f.OverrideFileLifecyclePolicyId!.Value)
                                           .Distinct()
                                           .ToList();

         var overridePolicies = overridePolicyIds.Count > 0
             ? await _policyRepository.GetByIdsAsync(overridePolicyIds, ct)
             : new Dictionary<int, ArchivalFileLifecyclePolicyEntity>();

         var modifiedFiles = new List<ArchivalFileEntity>();
         var runDetails = new List<ArchivalRunDetailEntity>();
         var counters = new LifecycleCounters();

         var nowOffset = _clock.Now;
         var today = _clock.Today;
         var sync = new object();

         // 3) Process in parallel
         await Parallel.ForEachAsync(candidates, new ParallelOptions { MaxDegreeOfParallelism = _degreeOfParallelism, CancellationToken = ct }, async (file, token) =>
         {
             try
             {
                 token.ThrowIfCancellationRequested();

                 if (file.Status == ArchivalFileStatus.Deleted) return;

                 if (file.LastTierCheckAtEt.HasValue && (nowOffset.UtcDateTime - file.LastTierCheckAtEt.Value) < _minAgeBetweenTierChecks) return;

                 // resolve policy
                 ArchivalFileLifecyclePolicyEntity? policy = null;
                 if (file.OverrideFileLifecyclePolicyId.HasValue)
                     overridePolicies.TryGetValue(file.OverrideFileLifecyclePolicyId.Value, out policy);

                 if (policy == null && tableConfigs.TryGetValue(file.TableConfigurationId, out var cfg))
                     policy = cfg.FileLifecyclePolicy;

                 if (policy == null)
                 {
                     lock (sync)
                     {
                         runDetails.Add(CreateRunDetail(runIdToUse, nowOffset.UtcDateTime, file, RunDetailPhase.Lifecycle, RunDetailStatus.Failed, "No lifecycle policy resolved"));
                     }
                     counters.Failed++;
                     return;
                 }

                 var (coolDays, archiveDays, deleteDays) = GetThresholdsForPolicy(policy, file.DateType);

                 var baseDate = file.AsOfDate.HasValue
                     ? DateOnly.FromDateTime(file.AsOfDate.Value)
                     : DateOnly.FromDateTime(file.CreatedAtEt.ToUniversalTime());

                 var ageDays = today.DayNumber - baseDate.DayNumber;

                 bool shouldDelete = deleteDays.HasValue && ageDays >= deleteDays.Value;
                 string? targetTier = null;
                 if (!shouldDelete)
                 {
                     if (archiveDays.HasValue && ageDays >= archiveDays.Value) targetTier = "Cold";
                     else if (coolDays.HasValue && ageDays >= coolDays.Value) targetTier = "Cool";
                 }

                 if (shouldDelete)
                 {
                     if (!dryRun)
                     {
                         try
                         {
                             await _blobStorage.DeleteIfExistsAsync(file.StorageAccountName, file.ContainerName, file.BlobPath, token);
                             file.Status = ArchivalFileStatus.Deleted;
                         }
                         catch (Exception ex)
                         {
                             lock (sync)
                             {
                                 runDetails.Add(CreateRunDetail(runIdToUse, nowOffset.UtcDateTime, file, RunDetailPhase.Lifecycle, RunDetailStatus.Failed, ex.ToString()));
                             }
                             counters.Failed++;
                             return;
                         }
                     }

                     file.LastTierCheckAtEt = nowOffset.UtcDateTime;
                     lock (sync) modifiedFiles.Add(file);
                     lock (sync) runDetails.Add(CreateRunDetail(runIdToUse, nowOffset.UtcDateTime, file, RunDetailPhase.Lifecycle, RunDetailStatus.Success, shouldDelete ? "Deleted (or dry-run)" : "Dry-run delete"));
                     counters.Deleted++;
                     return;
                 }

                 if (targetTier != null)
                 {
                     if (!dryRun)
                     {
                         try
                         {
                             await _blobStorage.SetAccessTierAsync(file.StorageAccountName, file.ContainerName, file.BlobPath, targetTier, token);
                             file.CurrentAccessTier = targetTier;
                         }
                         catch (Exception ex)
                         {
                             lock (sync)
                             {
                                 runDetails.Add(CreateRunDetail(runIdToUse, nowOffset.UtcDateTime, file, RunDetailPhase.Lifecycle, RunDetailStatus.Failed, ex.ToString()));
                             }
                             counters.Failed++;
                             return;
                         }
                     }

                     file.LastTierCheckAtEt = nowOffset.UtcDateTime;
                     lock (sync) modifiedFiles.Add(file);
                     lock (sync) runDetails.Add(CreateRunDetail(runIdToUse, nowOffset.UtcDateTime, file, RunDetailPhase.Lifecycle, RunDetailStatus.Success, $"SetTier={targetTier} (or dry-run)"));
                     counters.Tiered++;
                     return;
                 }

                 // nothing to do
                 file.LastTierCheckAtEt = nowOffset.UtcDateTime;
                 lock (sync) modifiedFiles.Add(file);
             }
             catch (Exception ex)
             {
                 _logger.LogError(ex, "Unhandled exception while enforcing lifecycle for file {Path}", file.BlobPath);
                 lock (sync)
                 {
                     runDetails.Add(CreateRunDetail(runIdToUse, DateTime.UtcNow, file, RunDetailPhase.Lifecycle, RunDetailStatus.Failed, ex.ToString()));
                 }
                 counters.Failed++;
             }
         });

         // 4) Persist results
         if (modifiedFiles.Count > 0)
             await _fileRepository.UpdateFilesAsync(modifiedFiles, ct);

         if (runDetails.Count > 0 && runIdToUse.HasValue)
             await _runRepository.ArchivalRunDetailBulkInsertAsync(runDetails, ct);
         else if (runDetails.Count > 0)
         {
             // no runId available: still persist details (StartRun failed earlier). Insert with RunId = 0.
             foreach (var d in runDetails) d.RunId = 0;
             await _runRepository.ArchivalRunDetailBulkInsertAsync(runDetails, ct);
         }

         // If we created the run, complete it with status derived from counters
         if (createdRunByUs && runIdToUse.HasValue)
         {
             var status = counters.Failed > 0 ? RunStatus.Partial : RunStatus.Success;
             try
             {
                 await _runRepository.CompleteRunAsync(runIdToUse.Value, status, $"Tiered={counters.Tiered} Deleted={counters.Deleted} Failed={counters.Failed}", ct);
             }
             catch (Exception ex)
             {
                 _logger.LogWarning(ex, "Failed to complete run {RunId}", runIdToUse);
             }
         }

         _logger.LogInformation("Lifecycle enforcement completed for account={Account}. Tiered={Tiered}, Deleted={Deleted}, Failed={Failed}",
             storageAccountName, counters.Tiered, counters.Deleted, counters.Failed);

         return new LifecycleEnforcerResult(counters.Tiered, counters.Deleted, counters.Failed);
     }

     private static (int? cool, int? archive, int? delete) GetThresholdsForPolicy(ArchivalFileLifecyclePolicyEntity policy, DateType? dateType)
     {
         switch (dateType)
         {
             case DateType.EOD:
                 return (policy.EodCoolDays, policy.EodArchiveDays, policy.EodDeleteDays);
             case DateType.EOM:
                 return (policy.EomCoolDays, policy.EomArchiveDays, policy.EomDeleteDays);
             case DateType.EOQ:
                 return (policy.EoqCoolDays, policy.EoqArchiveDays, policy.EoqDeleteDays);
             case DateType.EOY:
                 return (policy.EoyCoolDays, policy.EoyArchiveDays, policy.EoyDeleteDays);
             case DateType.EXT:
             default:
                 return (policy.ExternalCoolDays, policy.ExternalArchiveDays, policy.ExternalDeleteDays);
         }
     }

     private static ArchivalRunDetailEntity CreateRunDetail(long? runId, DateTime now, ArchivalFileEntity file, RunDetailPhase phase, RunDetailStatus status, string? message)
         => new()
         {
             RunId = runId ?? 0,
             TableConfigurationId = file.TableConfigurationId,
             AsOfDate = file.AsOfDate,
             DateType = file.DateType,
             Phase = phase,
             Status = status,
             ArchivalFileId = file.Id,
             FilePath = file.BlobPath,
             ErrorMessage = message,
             CreatedAtEt = now
         };

     private sealed class LifecycleCounters
     {
         public int Tiered;
         public int Deleted;
         public int Failed;
     }
 }
