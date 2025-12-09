using ArchivalSystem.Application.Interfaces;
using ArchivalSystem.Application.Models;
using ArchivalSystem.Data;
using ArchivalSystem.Data.Entities;
using Microsoft.Extensions.Logging;

namespace ArchivalSystem.Infrastructure;

/// <summary>
/// Orchestrates a single table/date archival:
/// - skips when archival rows already exist for that table/date,
/// - delegates export to ISourceExporter,
/// - updates run repository with a minimal result (skip / success / failure),
/// - optionally triggers source-row deletion via ISourceDataDeleter (deleter is responsible for its own logging).
/// </summary>
public class DbArchiver(
    IArchivalTableConfigurationRepository tableConfigRepository,
    IArchivalFileRepository fileRepository,
    ISourceExporter exporter,
    ISourceDataDeleter deleter,
    IArchivalRunRepository runRepository,
    ILogger<DbArchiver> logger)
{
    private readonly IArchivalTableConfigurationRepository _tableConfigRepository = tableConfigRepository ?? throw new ArgumentNullException(nameof(tableConfigRepository));
    private readonly IArchivalFileRepository _fileRepository = fileRepository ?? throw new ArgumentNullException(nameof(fileRepository));
    private readonly ISourceExporter _exporter = exporter ?? throw new ArgumentNullException(nameof(exporter));
    private readonly ISourceDataDeleter _deleter = deleter ?? throw new ArgumentNullException(nameof(deleter));
    private readonly IArchivalRunRepository _runRepository = runRepository ?? throw new ArgumentNullException(nameof(runRepository));
    private readonly ILogger<DbArchiver> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Archive the given table configuration for a single asOfDate.
    /// Records one minimal run-detail row describing the outcome (Skipped/Success/Failed).
    /// Deletion of source rows is delegated to ISourceDataDeleter which performs its own logging.
    /// </summary>
    public async Task ArchiveTableForDateAsync(
        ArchivalTableConfigurationDto tableConfig,
        DateTime asOfDate,
        DateType dateType,
        long runId,
        CancellationToken ct = default)
    {
        if (tableConfig == null) throw new ArgumentNullException(nameof(tableConfig));
        if (string.IsNullOrWhiteSpace(tableConfig.AsOfDateColumn))
            throw new InvalidOperationException($"Table configuration {tableConfig.Id} has no AsOfDateColumn configured.");

        // 1) Skip if there are already archival rows for this table/date
        var exists = await _fileRepository.ExistsForTableDateAsync(tableConfig.Id, asOfDate, dateType, ct);
        if (exists)
        {
            await _runRepository.LogDetailAsync(
                runId,
                tableConfig.Id,
                asOfDate,
                dateType,
                RunDetailPhase.Export,
                RunDetailStatus.Skipped,
                archivalFileId: null,
                rowsAffected: 0,
                filePath: null,
                errorMessage: "Already archived (archival_file exists).",
                ct: ct);
            return;
        }

        // 2) Check table/date exemption quickly (export will also check, but do a cheap check here)
        var isExempt = await _fileRepository.IsTableExemptAsync(tableConfig.Id, asOfDate, ct);
        if (isExempt)
        {
            await _runRepository.LogDetailAsync(
                runId,
                tableConfig.Id,
                asOfDate,
                dateType,
                RunDetailPhase.Export,
                RunDetailStatus.Skipped,
                archivalFileId: null,
                rowsAffected: 0,
                filePath: null,
                errorMessage: "Table/date is exempt from archival.",
                ct: ct);
            return;
        }

        // 3) Perform export (may throw). Exporter handles part uploads and per-part file rows.
        ParquetExportResult exportResult;
        try
        {
            exportResult = await _exporter.ExportAsync(tableConfig, asOfDate, dateType, runId, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Export failed for TableConfig {TableConfigId} date {AsOf}.", tableConfig.Id, asOfDate.ToString("yyyy-MM-dd"));

            await _runRepository.LogDetailAsync(
                runId,
                tableConfig.Id,
                asOfDate,
                dateType,
                RunDetailPhase.Export,
                RunDetailStatus.Failed,
                archivalFileId: null,
                rowsAffected: null,
                filePath: null,
                errorMessage: ex.ToString(),
                ct: ct);

            throw;
        }

        // 4) If exporter returned no rows, record skipped and exit.
        var rowsExported = exportResult?.Metrics?.RowCount ?? 0;
        if (rowsExported <= 0)
        {
            await _runRepository.LogDetailAsync(
                runId,
                tableConfig.Id,
                asOfDate,
                dateType,
                RunDetailPhase.Export,
                RunDetailStatus.Skipped,
                archivalFileId: null,
                rowsAffected: 0,
                filePath: null,
                errorMessage: "Export produced no rows.",
                ct: ct);
            return;
        }

        // 5) If configured, delegate deletion to ISourceDataDeleter.
        if (tableConfig.DeleteFromSource)
        {
            // Pass expected row count so deleter can tune its batching and validate counts.
            _ = await _deleter.DeleteAsync(tableConfig, asOfDate, rowsExported, runId, dateType, ct);
        }

        // 6) Record success summary for export (single run-detail entry)
        await _runRepository.LogDetailAsync(
            runId,
            tableConfig.Id,
            asOfDate,
            dateType,
            RunDetailPhase.Export,
            RunDetailStatus.Success,
            archivalFileId: null,
            rowsAffected: rowsExported,
            filePath: exportResult?.BlobInfo?.BlobPath,
            errorMessage: null,
            ct: ct);
    }
}
