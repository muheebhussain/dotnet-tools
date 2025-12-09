using ArchivalSystem.Abstraction;
using Microsoft.Data.SqlClient;
using System.Data;

namespace ArchivalSystem.Data
{
    public interface ITargetTableRepository
    {
        /// <summary>
        /// Returns distinct as_of_date values from the target table (as DateOnly).
        /// The caller supplies the connection string for the target database.
        /// </summary>
        Task<IReadOnlyCollection<DateOnly>> GetDistinctAsOfDatesAsync(
            string databaseName,
            string schemaName,
            string tableName,
            string asOfDateColumn,
            CancellationToken ct = default);

        /// <summary>
        /// Delete rows for the provided asOfDate from the target table in repeated batches.
        /// Returns the total number of rows deleted.
        /// Implementations SHOULD delete in small transactions (per-batch) and avoid long-running locks.
        /// </summary>
        Task<long> DeleteByAsOfInBatchesAsync(
            string databaseName,
            string schemaName,
            string tableName,
            string asOfColumn,
            DateTime asOfDate,
            int batchSize,
            CancellationToken ct = default);

        /// <summary>
        /// Execute a SELECT * FROM [schema].[table] WHERE [asOfDateColumn] = @asOfDate
        /// and return a TableQueryResult that owns the open connection and reader.
        /// Caller must dispose the result (await using).
        /// </summary>
        Task<TableQueryResult> ExecuteQueryAsync(
            string databaseName,
            string schemaName,
            string tableName,
            string asOfDateColumn,
            DateTime asOfDate,
            CancellationToken ct = default);
    }

    public class TargetTableRepository(IConnectionProvider connectionProvider) : ITargetTableRepository
    {
        private readonly IConnectionProvider _connectionProvider = connectionProvider ?? throw new ArgumentNullException(nameof(connectionProvider));
        public async Task<IReadOnlyCollection<DateOnly>> GetDistinctAsOfDatesAsync(
            string databaseName,
            string schemaName,
            string tableName,
            string asOfDateColumn,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(databaseName)) throw new ArgumentNullException(nameof(databaseName));
            if (string.IsNullOrWhiteSpace(schemaName)) throw new ArgumentNullException(nameof(schemaName));
            if (string.IsNullOrWhiteSpace(tableName)) throw new ArgumentNullException(nameof(tableName));
            if (string.IsNullOrWhiteSpace(asOfDateColumn)) throw new ArgumentNullException(nameof(asOfDateColumn));

            var sql = $@"
SELECT DISTINCT CAST([{asOfDateColumn}] AS date) AS as_of_date
FROM [{schemaName}].[{tableName}];";

            var results = new List<DateOnly>();

            await using var conn = await _connectionProvider.CreateOpenConnectionAsync(databaseName, ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var dt = reader.GetFieldValue<DateTime>(0); // CAST(... AS date) => DateTime
                results.Add(DateOnly.FromDateTime(dt));
            }

            return results.Distinct().ToArray();
        }

        /// <summary>
        /// Delete rows matching asOfColumn = asOfDate in repeated batches using individual short transactions.
        /// This uses DELETE TOP (@batchSize) pattern (SQL Server). It's robust and avoids huge transactions/locks.
        /// Returns total rows deleted.
        /// </summary>
        public async Task<long> DeleteByAsOfInBatchesAsync(
            string databaseName,
            string schemaName,
            string tableName,
            string asOfColumn,
            DateTime asOfDate,
            int batchSize,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(databaseName)) throw new ArgumentNullException(nameof(databaseName));
            if (string.IsNullOrWhiteSpace(schemaName)) schemaName = "dbo";
            if (string.IsNullOrWhiteSpace(tableName)) throw new ArgumentNullException(nameof(tableName));
            if (string.IsNullOrWhiteSpace(asOfColumn)) throw new ArgumentNullException(nameof(asOfColumn));
            if (batchSize <= 0) throw new ArgumentOutOfRangeException(nameof(batchSize));

            var totalDeleted = 0L;

            // Safe quoting of identifiers
            string qSchema = QuoteIdentifier(schemaName);
            string qTable = QuoteIdentifier(tableName);
            string qColumn = QuoteIdentifier(asOfColumn);

            var deleteSql = $"DELETE TOP (@batchSize) FROM {qSchema}.{qTable} WHERE {qColumn} = @asOfDate;";

            await using var conn = await _connectionProvider.CreateOpenConnectionAsync(databaseName, ct);
            await conn.OpenAsync(ct).ConfigureAwait(false);

            while (!ct.IsCancellationRequested)
            {
                await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = deleteSql;
                cmd.Parameters.Add(new SqlParameter("@batchSize", SqlDbType.Int) { Value = batchSize });
                cmd.Parameters.Add(new SqlParameter("@asOfDate", SqlDbType.DateTime2) { Value = asOfDate });

                int deleted;
                try
                {
                    deleted = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                    await tx.CommitAsync(ct);
                }
                catch
                {
                    try { await tx.RollbackAsync(ct); } catch { /* swallow */ }
                    throw;
                }

                if (deleted <= 0) break;

                totalDeleted += deleted;

                // If number deleted less than batchSize, likely no more matching rows.
                if (deleted < batchSize) break;
            }

            return totalDeleted;
        }

        private static string QuoteIdentifier(string name) => "[" + name.Replace("]", "]]") + "]";


        public async Task<TableQueryResult> ExecuteQueryAsync(
            string databaseName,
            string schemaName,
            string tableName,
            string asOfDateColumn,
            DateTime asOfDate,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(databaseName)) throw new ArgumentNullException(nameof(databaseName));
            if (string.IsNullOrWhiteSpace(schemaName)) throw new ArgumentNullException(nameof(schemaName));
            if (string.IsNullOrWhiteSpace(tableName)) throw new ArgumentNullException(nameof(tableName));
            if (string.IsNullOrWhiteSpace(asOfDateColumn)) throw new ArgumentNullException(nameof(asOfDateColumn));

            var conn = await _connectionProvider.CreateOpenConnectionAsync(databaseName, ct);

            var sql = $@"
SELECT *
FROM [{schemaName}].[{tableName}]
WHERE [{asOfDateColumn}] = @asOfDate;";

            var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandType = CommandType.Text;

            var p = cmd.CreateParameter();
            p.ParameterName = "@asOfDate";
            p.DbType = DbType.Date;
            p.Value = asOfDate.Date;
            cmd.Parameters.Add(p);

            // ExecuteReaderAsync with SequentialAccess; caller must dispose the returned TableQueryResult (owns connection + reader)
            var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct);

            return new TableQueryResult(conn, reader);
        }
    }
}
