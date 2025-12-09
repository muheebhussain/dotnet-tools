using ArchivalSystem.Application.Interfaces;
using ArchivalSystem.Application.Models;
using ArchivalSystem.Data;
using Microsoft.Extensions.Logging;

namespace ArchivalSystem.Infrastructure;

/// <summary>
/// Deletes source rows for a table/date in small batches and records run-detail entries for delete outcomes.
/// This implementation does not throw on delete failures; it logs the failure and records a failed run-detail so the
/// overall archival export is not undone by a deletion problem.
/// </summary>
public class SourceDataDeleter(
    ITargetTableRepository targetTableRepository,
    IArchivalRunRepository archivalRunRepository,
    ILogger<SourceDataDeleter> logger)
    : ISourceDataDeleter
{
    private readonly ITargetTableRepository _targetTableRepository = targetTableRepository ?? throw new ArgumentNullException(nameof(targetTableRepository));
    private readonly IArchivalRunRepository _archivalRunRepository = archivalRunRepository ?? throw new ArgumentNullException(nameof(archivalRunRepository));
    private readonly ILogger<SourceDataDeleter> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    // Default batch size for deletes; tune this per-environment (IO/log/lock characteristics).
    private const int DefaultDeleteBatchSize = 10_000;

    public async Task<long> DeleteAsync(
        ArchivalTableConfigurationDto tableConfig,
        DateTime asOfDate,
        long expectedRowCount,
        long runId,
        DateType dateType,
        CancellationToken ct = default)
    {
        if (tableConfig == null) throw new ArgumentNullException(nameof(tableConfig));
        if (string.IsNullOrWhiteSpace(tableConfig.AsOfDateColumn))
            throw new InvalidOperationException($"Table configuration {tableConfig.Id} has no as_of_date_column defined.");

        // Choose batch size. Could be made configurable later.
        var batchSize = DefaultDeleteBatchSize;

        long totalDeleted = 0;
        string? errorMessage = null;

        try
        {
            // Delegate the batching loop to target repository implementation.
            totalDeleted = await _targetTableRepository.DeleteByAsOfInBatchesAsync(
                tableConfig.DatabaseName,
                tableConfig.SchemaName,
                tableConfig.TableName,
                tableConfig.AsOfDateColumn!,
                asOfDate,
                batchSize,
                ct);

            // If expectedRowCount provided, validate and produce warning if mismatch.
            if (expectedRowCount > 0 && totalDeleted != expectedRowCount)
            {
                errorMessage = $"ExpectedDeleteCount={expectedRowCount},ActualDeleted={totalDeleted}";
                _logger.LogWarning(
                    "Delete-from-source row count mismatch for TableConfig {Id}, Date {Date}: expected {Expected}, deleted {Deleted}.",
                    tableConfig.Id,
                    asOfDate.ToString("yyyy-MM-dd"),
                    expectedRowCount,
                    totalDeleted);
            }

            // Record delete success (may include an error message if counts mismatched)
            await _archivalRunRepository.LogDetailAsync(
                runId,
                tableConfig.Id,
                asOfDate,
                dateType,
                RunDetailPhase.Delete,
                RunDetailStatus.Success,
                archivalFileId: null,
                rowsAffected: totalDeleted,
                filePath: null,
                errorMessage: errorMessage,
                ct: ct);
        }
        catch (OperationCanceledException)
        {
            // propagate cancellation so caller can stop
            throw;
        }
        catch (Exception ex)
        {
            // Log and record failure run-detail. Do not rethrow to avoid undoing a successful export.
            _logger.LogWarning(ex, "Source deletion failed for TableConfig {TableConfigId} date {AsOf}.", tableConfig.Id, asOfDate.ToString("yyyy-MM-dd"));

            await _archivalRunRepository.LogDetailAsync(
                runId,
                tableConfig.Id,
                asOfDate,
                dateType,
                RunDetailPhase.Delete,
                RunDetailStatus.Failed,
                archivalFileId: null,
                rowsAffected: null,
                filePath: null,
                errorMessage: ex.ToString(),
                ct: ct);

            // Return 0 to indicate no rows confirmed deleted due to failure.
            totalDeleted = 0;
        }

        return totalDeleted;
    }
}