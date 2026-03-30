using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using SentryIssuesAgent;

// var host = new HostBuilder()
//   .ConfigureFunctionsWorkerDefaults()
//    .Build();

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureAppConfiguration((context, config) =>
    {
        config.AddJsonFile("appsettings.json", optional: true)
              .AddEnvironmentVariables();
    })
    .ConfigureServices((context, services) =>
    {
        var configuration = context.Configuration;
        services.AddApplicationInsightsTelemetryWorkerService();
        services.AddSentryService(configuration);
        services.AddAzureDevOps(configuration);
        services.AddAzureAI(configuration);
    })
    .Build();

host.Run();
