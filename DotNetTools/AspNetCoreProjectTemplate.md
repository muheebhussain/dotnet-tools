# ASP.NET Core Web API Custom Visual Studio Project Template

This Visual Studio 2022 project template is designed to provide a quick start for developing ASP.NET Core Web API applications. It comes pre-configured with the following features:

1. Connect Authentication (Authorization - Policy Scopes)
2. Monitoring (Serilog, Elastic Search, Zipkin)
3. Swagger UI configuration
4. Health Check Endpoints
5. CORS

## Setup

To set up this project template, follow these steps:

1. Download the `template.zip` file from this repository.
2. Place the `template.zip` file at the following location on your PC: `C:\Users\{Your_User_Name}\Documents\Visual Studio 2022\Templates\ProjectTemplates`

## Configuration

After setting up the project template, you'll need to update the `appsettings.json` file to customize the template for your specific project.

```json
{
  "ApiInfo": {
    "Code": "Your_API_Code",
    "Title": "Your_API_Title",
    "ResourceCode": "Your_Resource_Code",
    "ScopeV1": "Your_ScopeV1"
  },
  "SwaggerUI": {
    "ClientId": "Your_ClientId"
  },
  "Monitoring": {
    "ZipkinCollectorEndpoint": "Your_Zipkin_Collector_Endpoint",
    "Realm": "Your_Realm",
    "ServiceName": "Your_Service_Name",
    "SamplingRate": 1.0,
    "IndexFormat": "Your_Index_Format",
    "Component": "Your_Component",
    "ElkUrl": "Your_Elk_Url",
    "ElkUserName": "Your_Elk_Username",
    "ElkPassword": "Your_Elk_Password"
  },
  "ClientCredentials": {
    "ClientId": "Your_ClientId",
    "ClientSecret": "Your_ClientSecret"
  },
  "Cors": {
    "AllowOrigins": ["https://your-allowed-origin.com"]
  }
}
```
Replace the placeholders (e.g., "Your_API_Code", "Your_ClientId", etc.) with your actual values. After updating the appsettings.json file, your custom ASP.NET Core Web API project template should be ready for use. Happy coding!

