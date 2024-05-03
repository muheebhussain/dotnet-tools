using Hangfire;
using Hangfire.SqlServer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using Microsoft.Extensions.Configuration;

class Program
{
    static void Main(string[] args)
    {
        var host = CreateHostBuilder(args).Build();

        // Start Hangfire Server
        using (var serviceScope = host.Services.CreateScope())
        {
            var services = serviceScope.ServiceProvider;
            try
            {
                // Here we can start our jobs or trigger them manually
                var jobManager = services.GetRequiredService<MyJobs>();
                RecurringJob.AddOrUpdate("LongRunningJob", () => jobManager.LongRunningJob(), "*/2 * * * *");
            }
            catch (Exception ex)
            {
                Console.WriteLine("An error occurred while starting the jobs: " + ex.Message);
            }
        }

        host.Run();  // Runs the host and Hangfire server
    }

    static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureServices((hostContext, services) =>
            {
                services.AddLogging();

                // Configuration setup for Hangfire
                services.AddHangfire(configuration => configuration
                    .SetDataCompatibilityLevel(CompatibilityLevel.Version_170)
                    .UseSimpleAssemblyNameTypeSerializer()
                    .UseRecommendedSerializerSettings()
                    .UseSqlServerStorage(hostContext.Configuration.GetConnectionString("HangfireConnection"), new SqlServerStorageOptions
                    {
                        CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
                        SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
                        QueuePollInterval = TimeSpan.Zero,
                        UseRecommendedIsolationLevel = true,
                        DisableGlobalLocks = true
                    }));

                // Hangfire server is hosted within this console application
                services.AddHangfireServer();

                // Registering the class that contains job methods
                services.AddTransient<MyJobs>();
            });
}

public class MyJobs
{
    public void LongRunningJob()
    {
        Console.WriteLine("Executing LongRunningJob...");
        // Simulate job execution time
        System.Threading.Thread.Sleep(10000);
        Console.WriteLine("Finished executing LongRunningJob.");
    }
}
{
  "ConnectionStrings": {
    "HangfireConnection": "Server=(localdb)\\mssqllocaldb;Database=HangfireDb;Trusted_Connection=True;"
  }
}

