# README.md

## Berlin.CommunicationHub Library Integration Guide

The `Berlin.CommunicationHub` library provides a robust and flexible solution to send emails via SMTP in .NET Core. This guide will walk you through integrating the library into your project to send emails.

### 1. Installation

Ensure you've referenced the `Berlin.CommunicationHub` library in your project.

### 2. Service Collection Extension

To use the `Berlin.CommunicationHub`, you need to add it to your service collection. This is typically done in the `Startup.cs` or `Program.cs` (for newer versions of .NET) of your application.

```csharp
using Berlin.CommunicationHub;

public void ConfigureServices(IServiceCollection services)
{
    services.AddCommunicationHub(config =>
    {
        config.Host = "your-smtp-host.com";
        config.Port = 587;  // Typically 587 or 465 for SSL
        config.Username = "your-username";
        config.Password = "your-password";
        config.UseSSL = true;  // Set to false if not using SSL
    });
}
```

### 3. Configuration in `appsettings.json`

To manage the SMTP settings dynamically, you can add them to your `appsettings.json`:

```json
{
    "SmtpConfiguration": {
        "Host": "your-smtp-host.com",
        "Port": 587,
        "Username": "your-username",
        "Password": "your-password",
        "UseSSL": true
    }
}
```

You can then update the `ConfigureServices` method to read from `appsettings.json`:

```csharp
public void ConfigureServices(IServiceCollection services)
{
    var smtpConfig = Configuration.GetSection("SmtpConfiguration").Get<SmtpConfiguration>();
    services.AddCommunicationHub(config =>
    {
        config.Host = smtpConfig.Host;
        config.Port = smtpConfig.Port;
        config.Username = smtpConfig.Username;
        config.Password = smtpConfig.Password;
        config.UseSSL = smtpConfig.UseSSL;
    });
}
```

### 4. Using the Service in Your Application

After registering the services, you can inject and use the `ICommunicationService<MessageDetail>` in your classes:

```csharp
using Berlin.CommunicationHub.Interfaces;
using Berlin.CommunicationHub.Models;

public class SomeService
{
    private readonly ICommunicationService<MessageDetail> _communicationService;

    public SomeService(ICommunicationService<MessageDetail> communicationService)
    {
        _communicationService = communicationService;
    }

    public async Task SendSampleEmail()
    {
        var messageDetail = new MessageDetail
        {
            FromAddress = "sender@example.com",
            ToAddress = "recipient1@example.com,recipient2@example.com",
            Subject = "Hello, Berlin!",
            Body = "Welcome to Berlin.CommunicationHub!"
        };

        await _communicationService.SendAsync(messageDetail);
    }
}
```

Make sure that `MessageDetail` is populated with the necessary data before calling the `SendAsync` method.

---

With the steps above, you're all set to send emails using the `Berlin.CommunicationHub` library!
