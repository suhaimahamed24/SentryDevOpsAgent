using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using static System.Net.WebRequestMethods;

namespace SentryIssuesAgent
{
    public class SentryService : ISentryService
    {
        private readonly HttpClient httpClient;
        private readonly SentryOptions sentryOptions;
        private readonly ILogger<SentryService> logger;

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public SentryService(
        HttpClient httpClient,
        IOptions<SentryOptions> sentryOptions,
        ILogger<SentryService> logger)
        {
            this.httpClient = httpClient;
            this.sentryOptions = sentryOptions.Value;
            this.logger = logger;
        }

        public async Task<PagedResult<SentryIssue>> GetIssuesAsync(
        SentryIssueFilter? filter = null,
        string? cursor = null,
        CancellationToken ct = default)
        {
            filter ??= new SentryIssueFilter();

            //var url = BuildIssuesUrl(filter, cursor);  // Currently not working with this url as we get 404 response.
            var url = "https://de.sentry.io/api/0/organizations/exact-software/issues/?collapse=stats&collapse=unhandled&environment=development&expand=owners&expand=inbox&limit=25&project=4507300057841744&query=is%3Aunresolved&shortIdLookup=1&statsPeriod=14d&sort=date";
            logger.LogInformation("Fetching Sentry issues: {Url}", url);

            var response = await httpClient.GetAsync(url, ct);
            await EnsureSuccessAsync(response);

            var items = await response.Content
                .ReadFromJsonAsync<List<SentryIssue>>(JsonOpts, ct)
                ?? new List<SentryIssue>();

            var (nextCursor, hasMore) = ParseLinkHeader(response);

            LogRateLimits(response);

            var result = new PagedResult<SentryIssue>(items, nextCursor, hasMore);

            logger.LogInformation(
                "Fetched {Count} Sentry issues (hasMore: {HasMore})", items.Count, hasMore);

            return result;
        }

        public async IAsyncEnumerable<SentryIssue> GetAllIssuesAsync(SentryIssueFilter filter, [EnumeratorCancellation] CancellationToken ct = default)
        {
            string? cursor = null;
            int page = 1;
            int total = 0;

            do
            {
                logger.LogDebug("Fetching page {Page} of Sentry issues...", page);

                var result = await GetIssuesAsync(filter, cursor, ct);

                foreach (var issue in result.Items)
                {
                    total++;
                    yield return issue;
                }

                cursor = result.NextCursor;
                page++;

            } while (cursor != null);

            logger.LogInformation("GetAllIssues complete — {Total} total issues fetched", total);
        }

        public async Task<SentryIssue> GetIssueAsync(string issueId, CancellationToken ct = default)
        {
            logger.LogInformation("Fetching Sentry issue {IssueId}", issueId);

            var url = BuildIssueUrl(issueId);
            //var url = "https://de.sentry.io/api/0/organizations/exact-software/issues/684800/?collapse=release&collapse=tags&expand=inbox&expand=owners";

            var response = await httpClient.GetAsync(url, ct);
            await EnsureSuccessAsync(response);

            var issue = await response.Content
                .ReadFromJsonAsync<SentryIssue>(JsonOpts, ct)
                    ?? throw new InvalidOperationException(
                        $"Sentry issue {issueId} returned empty response");

            return issue;
        }

        public async Task<IReadOnlyList<SentryEvent>> GetIssueEventsAsync(string issueId, CancellationToken ct = default)
        {
            var url = $"{sentryOptions.BaseUrl}/issues/{issueId}/events/";
            logger.LogInformation("Fetching events for Sentry issue {IssueId}", issueId);

            var response = await httpClient.GetAsync(url, ct);
            await EnsureSuccessAsync(response);

            var events = await response.Content
                .ReadFromJsonAsync<List<SentryEvent>>(JsonOpts, ct)
                    ?? new List<SentryEvent>();

            return events;
        }

        public async Task<SentryEvent?> GetLatestIssueEventAsync(string issueId, CancellationToken ct = default)
        {
            var url = $"{sentryOptions.BaseUrl}/issues/{issueId}/events/latest/";
            logger.LogInformation("Fetching latest event for Sentry issue {IssueId}", issueId);

            using var response = await httpClient.GetAsync(url, ct);
            await EnsureSuccessAsync(response);

            var sentryEvent = await response.Content
                .ReadFromJsonAsync<SentryEvent>(JsonOpts, ct);

            return sentryEvent;
        }

        public string GetStackTraceAsString(SentryEvent sentryEvent)
        {
            var sb = new StringBuilder();

            if (sentryEvent.Entries is not JsonElement entriesElement || entriesElement.ValueKind != JsonValueKind.Array)
                return string.Empty;

            foreach (var entry in entriesElement.EnumerateArray())
            {
                if (!entry.TryGetProperty("type", out var type))
                    continue;

                if (type.GetString() != "exception")
                    continue;

                var values = entry
                    .GetProperty("data")
                    .GetProperty("values");

                foreach (var exception in values.EnumerateArray())
                {
                    var exceptionType = exception.GetProperty("type").GetString();
                    var exceptionValue = exception.GetProperty("value").GetString();

                    sb.AppendLine($"Exception: {exceptionType} - {exceptionValue}");

                    if (!exception.TryGetProperty("stacktrace", out var stacktrace))
                        continue;

                    if (!stacktrace.TryGetProperty("frames", out var frames))
                        continue;

                    foreach (var frame in frames.EnumerateArray())
                    {
                        var function = frame.TryGetProperty("function", out var f) ? f.GetString() : "";
                        var file = frame.TryGetProperty("filename", out var fileProp) ? fileProp.GetString() : "";
                        var line = frame.TryGetProperty("lineno", out var lineProp) ? lineProp.GetInt32() : 0;

                        sb.AppendLine($"   at {function} in {file}:{line}");
                    }

                    sb.AppendLine(); // spacing between exceptions
                }
            }

            return sb.ToString();
        }

        private string BuildIssuesUrl(SentryIssueFilter filter, string? cursor)
        {
            var qs = new List<string>
        {
            $"query={Uri.EscapeDataString(filter.Query)}",
            $"limit={filter.Limit}",
            $"sort={filter.SortBy ?? "date"}",
            $"project={Uri.EscapeDataString(sentryOptions.ProjectSlug)}",
            $"shortIdLookup=1"
        };

            if (!string.IsNullOrEmpty(filter.Level))
                qs.Add($"level={filter.Level}");

            if (!string.IsNullOrEmpty(filter.Environment))
                qs.Add($"environment={Uri.EscapeDataString(filter.Environment)}");

            if (cursor != null)
                qs.Add($"cursor={Uri.EscapeDataString(cursor)}");

            return $"{sentryOptions.BaseUrl}/organizations/{sentryOptions.OrganizationSlug}/" +
                   $"issues/?{string.Join("&", qs)}";
        }

        private string BuildIssueUrl(string issueId)
        {
            var qs = new List<string>
        {
            $"collapse=release",
            $"collapse=tags",
            $"expand=inbox",
            $"expand=owners",
        };

            return $"{sentryOptions.BaseUrl}/organizations/{sentryOptions.OrganizationSlug}/" +
                   $"issues/{issueId}/?{string.Join("&", qs)}";
        }

        private static (string? NextCursor, bool HasMore) ParseLinkHeader(
            HttpResponseMessage response)
        {
            if (!response.Headers.TryGetValues("Link", out var linkValues))
                return (null, false);

            // Link header format:
            // <url&cursor=X>; rel="next"; results="true"; cursor="X",
            // <url&cursor=Y>; rel="previous"; results="true"; cursor="Y"
            foreach (var part in linkValues.SelectMany(v => v.Split(',')))
            {
                if (!part.Contains("rel=\"next\"")) continue;

                var hasMore = part.Contains("results=\"true\"");
                if (!hasMore) return (null, false);

                var cursorMatch = System.Text.RegularExpressions
                    .Regex.Match(part, @"cursor=""([^""]+)""");

                if (cursorMatch.Success)
                    return (cursorMatch.Groups[1].Value, true);
            }

            return (null, false);
        }

        private void LogRateLimits(HttpResponseMessage response)
        {
            if (response.Headers.TryGetValues(
                    "X-Sentry-Rate-Limit-Remaining", out var remaining))
                logger.LogDebug("Sentry rate limit remaining: {Remaining}",
                    remaining.FirstOrDefault());
        }

        private async Task EnsureSuccessAsync(HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode) return;

            var body = await response.Content.ReadAsStringAsync();

            var message = response.StatusCode switch
            {
                HttpStatusCode.Unauthorized =>
                    "Sentry auth failed — check AuthToken in appsettings.json",
                HttpStatusCode.Forbidden =>
                    "Sentry token lacks required scopes (need: project:read, event:read)",
                HttpStatusCode.NotFound =>
                    "Sentry resource not found — check OrganizationSlug and ProjectSlug",
                HttpStatusCode.TooManyRequests =>
                    "Sentry rate limit hit — reduce request frequency",
                _ =>
                    $"Sentry API error {(int)response.StatusCode}: {body}"
            };

            logger.LogError("Sentry API failed: {Message}", message);
            throw new HttpRequestException(message, null, response.StatusCode);
        }
    }
}
