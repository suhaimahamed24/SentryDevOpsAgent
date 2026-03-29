using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SentryIssuesAgent;

public static class AzureAIExtensions
{
    public static IServiceCollection AddAzureAI(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<AzureAIOptions>(
            configuration.GetSection(AzureAIOptions.Section)
        );

        services.AddOptions<AzureAIOptions>()
            .Bind(configuration.GetSection(AzureAIOptions.Section))
            .Validate(o => !string.IsNullOrEmpty(o.Endpoint),
                "AzureAI:Endpoint is required")
            .Validate(o => !string.IsNullOrEmpty(o.ApiKey),
                "AzureAI:ApiKey is required")
            .ValidateOnStart();

        return services;
    }
}