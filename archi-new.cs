csharp ArchivalSystem.Infrastructure\ArchivalFileLifecycleEnforcer.cs
using ArchivalSystem.Application.Interfaces;
using ArchivalSystem.Data;
using ArchivalSystem.Data.Entities;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ArchivalSystem.Application.Models;
using Azure;

private static async Task WriteRowGroupAsync(ParquetWriter parquetWriter, List<DataField> fields, List<List<object?>> columnBuffers, CancellationToken ct)
{
    using var rowGroup = parquetWriter.CreateRowGroup();

    for (int i = 0; i < fields.Count; i++)
    {
        var field = fields[i];
        var buffer = columnBuffers[i];

        // Convert to object?[] to avoid CLR typed-array mismatches (nullable vs non-nullable)
        // Parquet.NET accepts object arrays and will handle definition levels / nulls.
        var arr = buffer.ToArray(); // object?[]

        var dataColumn = new Parquet.Data.DataColumn(field, arr);
        await rowGroup.WriteColumnAsync(dataColumn, ct).ConfigureAwait(false);
    }
}

namespace ArchivalSystem.Infrastructure
{
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
        private readonly IArchivalFileLifecyclePolicyRepository _policy_repository = policyRepository ?? throw new ArgumentNullException(nameof(policyRepository));
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
                var cfg = await _tableConfig_repository.GetWithRelatedAsync(tid, ct);
                if (cfg != null) tableConfigs[tid] = cfg;
            }

            var overridePolicyIds = candidates.Where(f => f.OverrideFileLifecyclePolicyId.HasValue)
                                              .Select(f => f.OverrideFileLifecyclePolicyId!.Value)
                                              .Distinct()
                                              .ToList();

            var overridePolicies = overridePolicyIds.Count > 0
                ? await _policy_repository.GetByIdsAsync(overridePolicyIds, ct)
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
                        if (archiveDays.HasValue && ageDays >= archiveDays.Value) targetTier = "Archive";
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
                            string appliedTier;
                            try
                            {
                                appliedTier = await TrySetAccessTierWithFallbackAsync(file.StorageAccountName, file.ContainerName, file.BlobPath, targetTier, token);
                                file.CurrentAccessTier = appliedTier;
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
                        lock (sync) runDetails.Add(CreateRunDetail(runIdToUse, nowOffset.UtcDateTime, file, RunDetailPhase.Lifecycle, RunDetailStatus.Success, $"SetTier={file.CurrentAccessTier} (or dry-run)"));
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
                await _run_repository.ArchivalRunDetailBulkInsertAsync(runDetails, ct);
            }

            // If we created the run, complete it with status derived from counters
            if (createdRunByUs && runIdToUse.HasValue)
            {
                var status = counters.Failed > 0 ? RunStatus.Partial : RunStatus.Success;
                try
                {
                    await _run_repository.CompleteRunAsync(runIdToUse.Value, status, $"Tiered={counters.Tiered} Deleted={counters.Deleted} Failed={counters.Failed}", ct);
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

        /// <summary>
        /// Try setting desired access tier. If Archive is requested but not supported by the account,
        /// fall back to Cool. Returns the actually-applied tier string.
        /// </summary>
        private async Task<string> TrySetAccessTierWithFallbackAsync(string storageAccountName, string containerName, string blobPath, string desiredTier, CancellationToken ct)
        {
            if (string.Equals(desiredTier, "Archive", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    await _blobStorage.SetAccessTierAsync(storageAccountName, containerName, blobPath, "Archive", ct);
                    return "Archive";
                }
                catch (Exception ex)
                {
                    // Inspect Azure RequestFailedException if available
                    var rfe = ex as RequestFailedException ?? ex.InnerException as RequestFailedException;
                    var message = ex.Message ?? string.Empty;
                    var unsupported = false;

                    if (rfe != null)
                    {
                        // Common error codes/messages vary; check known patterns
                        if (string.Equals(rfe.ErrorCode, "AccessTierNotSupported", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(rfe.ErrorCode, "FeatureNotSupported", StringComparison.OrdinalIgnoreCase))
                            unsupported = true;
                    }

                    if (!unsupported)
                    {
                        if (message.IndexOf("not supported", StringComparison.OrdinalIgnoreCase) >= 0
                            || message.IndexOf("The specified access tier is not supported", StringComparison.OrdinalIgnoreCase) >= 0
                            || message.IndexOf("Archive tier is not supported", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            unsupported = true;
                        }
                    }

                    if (unsupported)
                    {
                        _logger.LogInformation(ex, "Archive tier not supported for {Account}/{Container}/{Blob}; falling back to Cool tier.", storageAccountName, containerName, blobPath);
                        // Try Cool
                        await _blobStorage.SetAccessTierAsync(storageAccountName, containerName, blobPath, "Cool", ct);
                        return "Cool";
                    }

                    // Unknown failure -> rethrow so caller logs and records failure
                    throw;
                }
            }
            else
            {
                // Non-Archive requested — just try to apply it.
                await _blobStorage.SetAccessTierAsync(storageAccountName, containerName, blobPath, desiredTier, ct);
                return desiredTier;
            }
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
}
