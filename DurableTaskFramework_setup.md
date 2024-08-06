### Durable Task Framework: How to Trigger

#### Triggering DurableTask.AzureStorage

**Setup and Trigger:**
1. **Create a .NET Web App:**
   - Use the .NET CLI to create a web app:
     ```bash
     dotnet new webapi -n DurableTaskAzureStorageDemo
     ```
2. **NuGet Packages:**
   - Install DurableTask.AzureStorage package:
     ```bash
     dotnet add package DurableTask.AzureStorage
     ```
3. **Configuration:**
   - In `Startup.cs`, configure Durable Task with Azure Storage:
     ```csharp
     public void ConfigureServices(IServiceCollection services)
     {
         services.AddDurableTask(config =>
         {
             config.UseAzureStorage(new AzureStorageOrchestrationServiceSettings
             {
                 TaskHubName = "YourTaskHubName",
                 StorageConnectionString = "YourAzureStorageConnectionString"
             });
         });
     }

     public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
     {
         app.UseEndpoints(endpoints =>
         {
             endpoints.MapControllers();
         });
     }
     ```

4. **Usage:**
   - Create an orchestration and activity:
     ```csharp
     public class SampleOrchestration : TaskOrchestration<string, string>
     {
         public override async Task<string> RunTask(OrchestrationContext context, string input)
         {
             var result = await context.ScheduleTask<string>("SampleActivity", input);
             return result;
         }
     }

     public class SampleActivity : TaskActivity<string, string>
     {
         protected override string Execute(TaskContext context, string input)
         {
             return $"Processed: {input}";
         }
     }
     ```

5. **Triggering the Orchestration:**
   - Create an API controller to trigger the orchestration:
     ```csharp
     [ApiController]
     [Route("api/[controller]")]
     public class OrchestrationsController : ControllerBase
     {
         private readonly IOrchestrationClient _orchestrationClient;

         public OrchestrationsController(IOrchestrationClient orchestrationClient)
         {
             _orchestrationClient = orchestrationClient;
         }

         [HttpPost("start")]
         public async Task<IActionResult> StartOrchestration([FromBody] string input)
         {
             var instanceId = await _orchestrationClient.CreateOrchestrationInstanceAsync(typeof(SampleOrchestration), input);
             return Ok(instanceId);
         }
     }
     ```

#### Triggering DurableTask.SqlServer

**Setup and Trigger:**
1. **Create a .NET Web App:**
   - Use the .NET CLI to create a web app:
     ```bash
     dotnet new webapi -n DurableTaskSqlServerDemo
     ```
2. **NuGet Packages:**
   - Install DurableTask.SqlServer package:
     ```bash
     dotnet add package DurableTask.SqlServer
     ```
3. **Configuration:**
   - In `Startup.cs`, configure Durable Task with SQL Server:
     ```csharp
     public void ConfigureServices(IServiceCollection services)
     {
         services.AddDurableTask(config =>
         {
             config.UseSqlServer(new SqlServerOrchestrationServiceSettings
             {
                 TaskHubName = "YourTaskHubName",
                 ConnectionString = "YourSqlServerConnectionString"
             });
         });
     }

     public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
     {
         app.UseEndpoints(endpoints =>
         {
             endpoints.MapControllers();
         });
     }
     ```

4. **Usage:**
   - Create an orchestration and activity similar to the Azure Storage example:
     ```csharp
     public class SampleOrchestration : TaskOrchestration<string, string>
     {
         public override async Task<string> RunTask(OrchestrationContext context, string input)
         {
             var result = await context.ScheduleTask<string>("SampleActivity", input);
             return result;
         }
     }

     public class SampleActivity : TaskActivity<string, string>
     {
         protected override string Execute(TaskContext context, string input)
         {
             return $"Processed: {input}";
         }
     }
     ```

5. **Triggering the Orchestration:**
   - Create an API controller to trigger the orchestration:
     ```csharp
     [ApiController]
     [Route("api/[controller]")]
     public class OrchestrationsController : ControllerBase
     {
         private readonly IOrchestrationClient _orchestrationClient;

         public OrchestrationsController(IOrchestrationClient orchestrationClient)
         {
             _orchestrationClient = orchestrationClient;
         }

         [HttpPost("start")]
         public async Task<IActionResult> StartOrchestration([FromBody] string input)
         {
             var instanceId = await _orchestrationClient.CreateOrchestrationInstanceAsync(typeof(SampleOrchestration), input);
             return Ok(instanceId);
         }
     }
     ```

---

### Deployment Environment

**Deployment Environment for Durable Task Framework:**

- **Windows Server:**
  - Can be hosted as a Windows Service or deployed to IIS.
- **Linux:**
  - Can be hosted as a systemd service or deployed to a web server like Nginx or Apache.
- **Private Cloud Container:**
  - Deploy using Docker or Kubernetes.
- **Public Cloud Container:**
  - Suitable for deployment in Azure AKS, AWS EKS, or Google Kubernetes Engine (GKE).
  - For Azure Storage backend, ensure the Azure Storage account is accessible.
  - For SQL Server backend, ensure the SQL Server instance is accessible from the deployment environment.

---

These sections provide detailed guidance on setting up, triggering, and deploying the Durable Task Framework in .NET applications across various environments.



### Durable Task Framework: How to Set It Up in .NET Console App

#### DurableTask.AzureStorage in Console App

**How to Set It Up:**
1. **Create a .NET Console App:**
   - Use the .NET CLI to create a console app:
     ```bash
     dotnet new console -n DurableTaskAzureStorageConsoleApp
     ```
2. **NuGet Packages:**
   - Install DurableTask.AzureStorage package:
     ```bash
     dotnet add package DurableTask.AzureStorage
     ```

3. **Configuration:**
   - Configure Durable Task with Azure Storage in `Program.cs`:
     ```csharp
     using DurableTask.Core;
     using DurableTask.AzureStorage;
     using Microsoft.Extensions.DependencyInjection;
     using Microsoft.Extensions.Hosting;

     class Program
     {
         static async Task Main(string[] args)
         {
             var host = new HostBuilder()
                 .ConfigureServices((hostContext, services) =>
                 {
                     services.AddDurableTask(config =>
                     {
                         config.UseAzureStorage(new AzureStorageOrchestrationServiceSettings
                         {
                             TaskHubName = "YourTaskHubName",
                             StorageConnectionString = "YourAzureStorageConnectionString"
                         });
                     });
                 })
                 .Build();

             await host.RunAsync();
         }
     }
     ```

4. **Usage:**
   - Create an orchestration and activity:
     ```csharp
     public class SampleOrchestration : TaskOrchestration<string, string>
     {
         public override async Task<string> RunTask(OrchestrationContext context, string input)
         {
             var result = await context.ScheduleTask<string>("SampleActivity", input);
             return result;
         }
     }

     public class SampleActivity : TaskActivity<string, string>
     {
         protected override string Execute(TaskContext context, string input)
         {
             return $"Processed: {input}";
         }
     }
     ```

5. **Triggering the Orchestration:**
   - Add code to trigger the orchestration in `Program.cs`:
     ```csharp
     using DurableTask.Core;

     class Program
     {
         static async Task Main(string[] args)
         {
             var host = new HostBuilder()
                 .ConfigureServices((hostContext, services) =>
                 {
                     services.AddDurableTask(config =>
                     {
                         config.UseAzureStorage(new AzureStorageOrchestrationServiceSettings
                         {
                             TaskHubName = "YourTaskHubName",
                             StorageConnectionString = "YourAzureStorageConnectionString"
                         });
                     });
                 })
                 .Build();

             await host.StartAsync();

             var client = host.Services.GetRequiredService<IOrchestrationClient>();
             var instanceId = await client.CreateOrchestrationInstanceAsync(typeof(SampleOrchestration), "Sample Input");

             Console.WriteLine($"Started orchestration with ID = {instanceId}");

             await host.WaitForShutdownAsync();
         }
     }
     ```

#### DurableTask.SqlServer in Console App

**How to Set It Up:**
1. **Create a .NET Console App:**
   - Use the .NET CLI to create a console app:
     ```bash
     dotnet new console -n DurableTaskSqlServerConsoleApp
     ```
2. **NuGet Packages:**
   - Install DurableTask.SqlServer package:
     ```bash
     dotnet add package DurableTask.SqlServer
     ```

3. **Configuration:**
   - Configure Durable Task with SQL Server in `Program.cs`:
     ```csharp
     using DurableTask.Core;
     using DurableTask.SqlServer;
     using Microsoft.Extensions.DependencyInjection;
     using Microsoft.Extensions.Hosting;

     class Program
     {
         static async Task Main(string[] args)
         {
             var host = new HostBuilder()
                 .ConfigureServices((hostContext, services) =>
                 {
                     services.AddDurableTask(config =>
                     {
                         config.UseSqlServer(new SqlServerOrchestrationServiceSettings
                         {
                             TaskHubName = "YourTaskHubName",
                             ConnectionString = "YourSqlServerConnectionString"
                         });
                     });
                 })
                 .Build();

             await host.RunAsync();
         }
     }
     ```

4. **Usage:**
   - Create an orchestration and activity similar to the Azure Storage example:
     ```csharp
     public class SampleOrchestration : TaskOrchestration<string, string>
     {
         public override async Task<string> RunTask(OrchestrationContext context, string input)
         {
             var result = await context.ScheduleTask<string>("SampleActivity", input);
             return result;
         }
     }

     public class SampleActivity : TaskActivity<string, string>
     {
         protected override string Execute(TaskContext context, string input)
         {
             return $"Processed: {input}";
         }
     }
     ```

5. **Triggering the Orchestration:**
   - Add code to trigger the orchestration in `Program.cs`:
     ```csharp
     using DurableTask.Core;

     class Program
     {
         static async Task Main(string[] args)
         {
             var host = new HostBuilder()
                 .ConfigureServices((hostContext, services) =>
                 {
                     services.AddDurableTask(config =>
                     {
                         config.UseSqlServer(new SqlServerOrchestrationServiceSettings
                         {
                             TaskHubName = "YourTaskHubName",
                             ConnectionString = "YourSqlServerConnectionString"
                         });
                     });
                 })
                 .Build();

             await host.StartAsync();

             var client = host.Services.GetRequiredService<IOrchestrationClient>();
             var instanceId = await client.CreateOrchestrationInstanceAsync(typeof(SampleOrchestration), "Sample Input");

             Console.WriteLine($"Started orchestration with ID = {instanceId}");

             await host.WaitForShutdownAsync();
         }
     }
     ```

---

### Deployment Environment

**Deployment Environment for Durable Task Framework in Console Apps:**

- **Windows Server:**
  - Can be hosted as a Windows Service using tools like NSSM (Non-Sucking Service Manager) or Windows Service Wrapper.
- **Linux:**
  - Can be hosted as a systemd service.
  - Create a service file in `/etc/systemd/system` and enable it:
    ```ini
    [Unit]
    Description=Durable Task Framework Console App

    [Service]
    ExecStart=/usr/bin/dotnet /path/to/DurableTaskAzureStorageConsoleApp.dll
    Restart=always
    User=your-username
    Group=your-group
    Environment=ASPNETCORE_ENVIRONMENT=Production

    [Install]
    WantedBy=multi-user.target
    ```
  - Enable and start the service:
    ```bash
    sudo systemctl enable your-service-name
    sudo systemctl start your-service-name
    ```
- **Private Cloud Container:**
  - Deploy using Docker or Kubernetes.
  - Create a Dockerfile:
    ```Dockerfile
    FROM mcr.microsoft.com/dotnet/aspnet:5.0 AS base
    WORKDIR /app
    COPY . .
    ENTRYPOINT ["dotnet", "DurableTaskAzureStorageConsoleApp.dll"]
    ```
  - Build and run the Docker image:
    ```bash
    docker build -t durable-task-console-app .
    docker run -d durable-task-console-app
    ```
- **Public Cloud Container:**
  - Suitable for deployment in Azure AKS, AWS EKS, or Google Kubernetes Engine (GKE).
  - Ensure the Azure Storage account or SQL Server instance is accessible from the deployment environment.

---

These sections provide detailed guidance on setting up, triggering, and deploying the Durable Task Framework in .NET console applications across various environments.
