using ArchivalSystem.Application.Interfaces;
using ArchivalSystem.Application.Models;
using ArchivalSystem.Data;
using Microsoft.Extensions.Logging;

namespace ArchivalSystem.Infrastructure;

/// <summary>
/// Coordinates retention-driven archival for a single self-managed table.
/// - computes retention candidate dates,
/// - sequentially invokes the DbArchiver for each candidate date (to avoid DB/IO storms),
/// - writes a single skipped run-detail if there are no candidates.
/// Logging and run-detail recording is intentionally minimal; per-date results are recorded by DbArchiver.
/// </summary>
public class SelfManagedTableArchiver(
        IRetentionService retentionService,
        IDbArchiver dbArchiver,
        IArchivalRunRepository archivalRunRepository,
        ILogger<SelfManagedTableArchiver> logger)
        : ISelfManagedTableArchiver
{
    private readonly IRetentionService _retentionService = retentionService ?? throw new ArgumentNullException(nameof(retentionService));
    private readonly IDbArchiver _dbArchiver = dbArchiver ?? throw new ArgumentNullException(nameof(dbArchiver));
    private readonly IArchivalRunRepository _archivalRunRepository = archivalRunRepository ?? throw new ArgumentNullException(nameof(archivalRunRepository));
    private readonly ILogger<SelfManagedTableArchiver> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task ArchiveAsync(
        ArchivalTableConfigurationDto tableConfig,
        long runId,
        CancellationToken ct = default)
    {
        if (tableConfig == null) throw new ArgumentNullException(nameof(tableConfig));
        if (string.IsNullOrWhiteSpace(tableConfig.AsOfDateColumn))
            throw new InvalidOperationException($"Table configuration {tableConfig.Id} has no AsOfDateColumn configured.");

        _logger.LogInformation(
            "Processing SelfManaged table {Id} ({Db}.{Schema}.{Table}).",
            tableConfig.Id,
            tableConfig.DatabaseName,
            tableConfig.SchemaName,
            tableConfig.TableName);

        // 1) Compute KEEP set + candidate dates
        var retention = await _retentionService.ComputeRetentionAsync(tableConfig, ct);

        var candidateDates = (retention?.CandidateDates ?? Array.Empty<DateOnly>()).OrderBy(d => d).ToList();
        if (!candidateDates.Any())
        {
            _logger.LogInformation("No candidate dates for archival for TableConfig {Id}. Skipping.", tableConfig.Id);
            return;
        }

        // 2) Resolve date types for candidate dates once
        var dateTypeMap = await _retentionService.GetDateTypesAsync(candidateDates, ct);

        // 3) Process sequentially to avoid spikes
        foreach (var d in candidateDates)
        {
            ct.ThrowIfCancellationRequested();

            var dateType = dateTypeMap.TryGetValue(d, out var dt) ? dt : DateType.EOD;
            var asOfDateTime = d.ToDateTime(TimeOnly.MinValue);

            try
            {
                await _dbArchiver.ArchiveTableForDateAsync(tableConfig, asOfDateTime, dateType, runId, ct);
            }
            catch (OperationCanceledException)
            {
                // bubble up cancellation
                throw;
            }
            catch (Exception ex)
            {
                // Minimal logging — DbArchiver emits per-date run-detail rows; this log is only for operator diagnosis.
                _logger.LogError(ex, "Archiving failed for TableConfig {TableConfigId} date {Date}. Continuing.", tableConfig.Id, d.ToString("yyyy-MM-dd"));
            }
        }
    }
}

