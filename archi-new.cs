 /// <summary>
 /// Returns distinct storage account names referenced by active table configurations.
 /// </summary>
 Task<List<string>> GetDistinctActiveStorageAccountNamesAsync(CancellationToken ct = default);

 /// <summary>
 /// Returns distinct (storageAccount, container) pairs referenced by active table configurations.
 /// Use this when you need to scope work to a container rather than the whole account.
 /// </summary>
 Task<List<(string StorageAccountName, string ContainerName)>> GetDistinctActiveStorageAccountContainerPairsAsync(CancellationToken ct = default);

 /// <summary>
 /// Returns all active table configurations (including related policy entities).
 /// </summary>
 Task<List<ArchivalTableConfigurationEntity>> GetAllActiveAsync(CancellationToken ct = default);

public async Task<List<string>> GetDistinctActiveStorageAccountNamesAsync(CancellationToken ct = default)
{
    return await _db.ArchivalTableConfigurations
        .AsNoTracking()
        .Where(tc => tc.IsActive && !string.IsNullOrEmpty(tc.StorageAccountName))
        .Select(tc => tc.StorageAccountName!)
        .Distinct()
        .ToListAsync(ct);
}

public async Task<List<(string StorageAccountName, string ContainerName)>> GetDistinctActiveStorageAccountContainerPairsAsync(CancellationToken ct = default)
{
    var pairs = await _db.ArchivalTableConfigurations
        .AsNoTracking()
        .Where(tc => tc.IsActive && !string.IsNullOrEmpty(tc.StorageAccountName) && !string.IsNullOrEmpty(tc.ContainerName))
        .Select(tc => new { tc.StorageAccountName, tc.ContainerName })
        .Distinct()
        .ToListAsync(ct);

    return pairs.Select(p => (p.StorageAccountName, p.ContainerName)).ToList();
}

public async Task<List<ArchivalTableConfigurationEntity>> GetAllActiveAsync(CancellationToken ct = default)
{
    return await _db.ArchivalTableConfigurations
        .AsNoTracking()
        .Include(tc => tc.TableRetentionPolicy)
        .Include(tc => tc.FileLifecyclePolicy)
        .Where(tc => tc.IsActive)
        .ToListAsync(ct);
}


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

     /// <summary>
     /// Execute enforcement for a single storage account. Returns the enforcement result.
     /// (unchanged behavior, kept for direct calls)
     /// </summary>
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

         LifecycleEnforcerResult result;
         try
         {
             result = await _enforcer.EnforceAsync(storageAccountName, containerName, prefix, dryRun, ct);
         }
         catch (OperationCanceledException)
         {
             _logger.LogInformation("Lifecycle enforcement for account={Account} was cancelled.", storageAccountName);
             throw;
         }
         catch (Exception ex)
         {
             _logger.LogError(ex, "Lifecycle enforcement failed for account={Account}.", storageAccountName);
             return new LifecycleEnforcerResult(0, 0, 1);
         }

         _logger.LogInformation("Lifecycle enforcement finished for account={Account}. Tiered={Tiered}, Deleted={Deleted}, Failed={Failed}",
             storageAccountName, result.Tiered, result.Deleted, result.Failed);

         if (_runRepository != null)
         {
             try
             {
                 var note = $"Enforcer run for account={storageAccountName} container={containerName} prefix={prefix}";
                 var run = await _runRepository.StartRunAsync(note, ct);

                 var rd = new ArchivalRunDetailEntity
                 {
                     RunId = run.Id,
                     TableConfigurationId = 0,
                     AsOfDate = null,
                     DateType = null,
                     Phase = RunDetailPhase.Lifecycle,
                     Status = RunDetailStatus.Success,
                     ArchivalFileId = null,
                     FilePath = note,
                     ErrorMessage = $"Tiered={result.Tiered} Deleted={result.Deleted} Failed={result.Failed}",
                     CreatedAtEt = _clock.Now.UtcDateTime
                 };

                 await _runRepository.ArchivalRunDetailBulkInsertAsync(new[] { rd }, ct);
                 await _runRepository.CompleteRunAsync(run.Id, RunStatus.Success, $"Completed: {rd.ErrorMessage}", ct);
             }
             catch (Exception ex)
             {
                 _logger.LogWarning(ex, "Failed to persist executor run summary to run repository for account={Account}. Ignoring.", storageAccountName);
             }
         }

         return result;
     }

     /// <summary>
     /// Discover distinct (storageAccount, container) pairs referenced by active table configurations in the DB,
     /// and run lifecycle enforcement for each pair. Uses bounded concurrency.
     /// </summary>
     public async Task<Dictionary<string, LifecycleEnforcerResult>> ExecuteForAllAccountsFromDbAsync(
         string? containerName = null,
         string? prefix = null,
         bool dryRun = false,
         int maxDegreeOfParallelism = 1,
         CancellationToken ct = default)
     {
         // Try to obtain distinct (account,container) pairs from repository.
         // If not available, fall back to distinct account names (existing behavior).
         IEnumerable<(string StorageAccountName, string ContainerName)> pairs;
         try
         {
             var pairList = await _tableConfigRepository.GetDistinctActiveStorageAccountContainerPairsAsync(ct);
             pairs = pairList ?? Enumerable.Empty<(string, string)>();
         }
         catch (MissingMethodException)
         {
             // Fallback: only accounts available
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

             // map accounts to pairs with null container (executor will treat container param as null)
             pairs = accounts.Select(a => (StorageAccountName: a, ContainerName: (string?)null!));
         }

         var result = new Dictionary<string, LifecycleEnforcerResult>(StringComparer.OrdinalIgnoreCase);
         var locker = new object();

         await Parallel.ForEachAsync(pairs, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, maxDegreeOfParallelism), CancellationToken = ct }, async (pair, token) =>
         {
             try
             {
                 token.ThrowIfCancellationRequested();

                 // if caller supplied a containerName, override the pair's container (caller intent)
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


     /// <summary>
     /// Execute enforcement for all tables in the DB that reference a storage account.
     /// Runs enforcement per-table-config (narrow scope) rather than per-account.
     /// Useful when you want to limit enforcement to specific table-level configuration semantics.
     /// </summary>
     public async Task<Dictionary<int, LifecycleEnforcerResult>> ExecutePerTableConfigurationAsync(
         IEnumerable<int>? tableConfigurationIds = null,
         bool dryRun = false,
         int maxDegreeOfParallelism = 4,
         CancellationToken ct = default)
     {
         // load targeted table configurations
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
                 // run enforcer scoped to the storage account and prefix derived from the table config
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

internal class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            // Expected usage:
            //   ArchivalLifecycleCli --subscriptionId <sub> --resourceGroup <rg> --storageAccount <account>
            if (args.Length == 0)
            {
                Console.Error.WriteLine(
                    "Usage: ArchivalLifecycleCli " +
                    "--subscriptionId <sub> --resourceGroup <rg> --storageAccount <account>");
                return 1;
            }

            string? subscriptionId = null;
            string? resourceGroup = null;
            string? storageAccount = null;

            for (int i = 0; i < args.Length - 1; i++)
            {
                switch (args[i])
                {
                    case "--subscriptionId":
                        subscriptionId = args[i + 1];
                        break;
                    case "--resourceGroup":
                        resourceGroup = args[i + 1];
                        break;
                    case "--storageAccount":
                        storageAccount = args[i + 1];
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(subscriptionId) ||
                string.IsNullOrWhiteSpace(resourceGroup) ||
                string.IsNullOrWhiteSpace(storageAccount))
            {
                Console.Error.WriteLine(
                    "Usage: ArchivalLifecycleCli " +
                    "--subscriptionId <sub> --resourceGroup <rg> --storageAccount <account>");
                return 1;
            }

            using var cts = new CancellationTokenSource();

            var host = Host.CreateDefaultBuilder()
                .ConfigureAppConfiguration((context, config) =>
                {
                    config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
                    config.AddEnvironmentVariables();
                })
                .ConfigureServices((ctx, services) =>
                {
                    var configuration = ctx.Configuration;

                    // Archival metadata DB
                    services.AddDbContext<ArchivalDbContext>(options =>
                        options.UseSqlServer(configuration.GetConnectionString("ArchivalDb")));

                    // HTTP client for Azure Management API
                    services.AddHttpClient("AzureMgmt");

                    // Our updater
                    services.AddScoped<ArchivalLifecycleExecutor>();

                })
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddConsole();
                })
                .Build();

            using (host)
            {
                await host.StartAsync(cts.Token);

                // Execute lifecycle enforcement across accounts discovered in DB
                using var scope = host.Services.CreateScope();
                var executor = scope.ServiceProvider.GetRequiredService<ArchivalLifecycleExecutor>();

                // You can pass containerName/prefix/dryRun/maxParallel as desired.
                var results = await executor.ExecuteForAllAccountsFromDbAsync(
                    containerName: null,
                    prefix: null,
                    dryRun: false,
                    maxDegreeOfParallelism: 4,
                    ct: cts.Token);

                // Log summary
                var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
                logger.LogInformation("Lifecycle executor completed. Accounts processed: {Count}", results.Count);
                foreach (var kv in results)
                {
                    logger.LogInformation("Account/Container: {Key} => Tiered={Tiered} Deleted={Deleted} Failed={Failed}",
                        kv.Key, kv.Value.Tiered, kv.Value.Deleted, kv.Value.Failed);
                }

                await host.StopAsync(cts.Token);
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Fatal error in ArchivalLifecycleCli: " + ex);
            return 1;
        }
    }
}
