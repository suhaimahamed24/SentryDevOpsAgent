using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Extensions.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace SentryIssuesAgent
{
    public static class SentryServiceExtensions
    {
        public static IServiceCollection AddSentryService(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<SentryOptions>(configuration.GetSection(SentryOptions.Section));

            services.AddOptions<SentryOptions>()
            .Bind(configuration.GetSection(SentryOptions.Section))
            .Validate(o => !string.IsNullOrEmpty(o.AuthToken),
                "Sentry:AuthToken is required in appsettings.json")
            .Validate(o => !string.IsNullOrEmpty(o.OrganizationSlug),
                "Sentry:OrganizationSlug is required in appsettings.json")
            .Validate(o => !string.IsNullOrEmpty(o.ProjectSlug),
                "Sentry:ProjectSlug is required in appsettings.json")
            .ValidateOnStart();

            services.AddHttpClient<ISentryService, SentryService>((sp, client) =>
            {
                var opts = sp.GetRequiredService<IOptions<SentryOptions>>().Value;
                client.BaseAddress = new Uri(opts.BaseUrl);
                client.Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds);
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {opts.AuthToken}");
            });
            //.AddPolicyHandler((sp, _) =>
            //{
            //    var opts = sp.GetRequiredService<IOptions<SentryOptions>>().Value;
            //    var log = sp.GetRequiredService<ILogger<SentryService>>();

            //    // Retry: 3x with exponential backoff (1s, 2s, 4s)
            //    return HttpPolicyExtensions
            //        .HandleTransientHttpError()
            //        .OrResult(r => r.StatusCode == HttpStatusCode.TooManyRequests)
            //        .WaitAndRetryAsync(
            //            retryCount: opts.MaxRetries,
            //            sleepDurationProvider: attempt =>
            //                TimeSpan.FromSeconds(Math.Pow(2, attempt)),
            //            onRetry: (outcome, timespan, attempt, _) =>
            //                log.LogWarning(
            //                    "Sentry retry {Attempt}/{Max} after {Delay}s — {Reason}",
            //                    attempt, opts.MaxRetries,
            //                    timespan.TotalSeconds,
            //                    outcome.Exception?.Message
            //                    ?? outcome.Result?.StatusCode.ToString()));
            //})
            //.AddPolicyHandler(
            //    // Circuit breaker: open after 5 failures, reset after 30s
            //    HttpPolicyExtensions
            //        .HandleTransientHttpError()
            //        .CircuitBreakerAsync(
            //            handledEventsAllowedBeforeBreaking: 5,
            //            durationOfBreak: TimeSpan.FromSeconds(30)));

            services.Configure<SentryMcpOptions>(configuration.GetSection(SentryMcpOptions.SectionName));


            return services;
        }
    }
}
