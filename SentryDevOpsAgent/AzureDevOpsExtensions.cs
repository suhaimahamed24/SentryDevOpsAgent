using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SentryIssuesAgent;

public static class AzureDevOpsExtensions
{
    public static IServiceCollection AddAzureDevOps(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<AzureDevopsOptions>(
            configuration.GetSection(AzureDevopsOptions.Section)
        );

        services.AddOptions<AzureDevopsOptions>()
            .Bind(configuration.GetSection(AzureDevopsOptions.Section))
            .Validate(o => !string.IsNullOrEmpty(o.OrganizationName),
                "AzureDevOps:OrganizationName is required")
            .Validate(o => !string.IsNullOrEmpty(o.AuthToken),
                "AzureDevOps:AuthToken is required")
            .Validate(o => !string.IsNullOrEmpty(o.ProjectName),
                "AzureDevOps:ProjectName is required")
            .ValidateOnStart();

        services.AddSingleton<IMcpService, McpService>();
        services.AddSingleton<AzureDevOpsService>();

        return services;
    }
}