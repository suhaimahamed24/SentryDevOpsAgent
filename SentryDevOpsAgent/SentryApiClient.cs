using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;

namespace SentryIssuesAgent
{
    public class SentryApiClient
    {
        private readonly HttpClient _http;
        private const string BaseUrl = "https://sentry.io/api/0";

        public SentryApiClient(string authToken)
        {
            _http = new HttpClient();
            _http.DefaultRequestHeaders.Add(
                "Authorization", $"Bearer {authToken}");
        }

        // Fetch issues — full control over filters, pagination, ordering
        public async Task<List<SentryIssue>> GetIssuesAsync(
            string organizationSlug,
            string projectSlug,
            string? query = "is:unresolved",
            int limit = 25,
            string? cursor = null)
        {
            var url = $"{BaseUrl}/projects/{organizationSlug}" +
                      $"/{projectSlug}/issues/" +
                      $"?query={Uri.EscapeDataString(query ?? "")}" +
                      $"&limit={limit}" +
                      (cursor != null ? $"&cursor={cursor}" : "");

            var response = await _http.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var remaining = response.Headers
                .GetValues("X-Sentry-Rate-Limit-Remaining")
                .FirstOrDefault();

            var nextCursor = response.Headers
                .GetValues("Link")
                .FirstOrDefault();

            return await response.Content
                .ReadFromJsonAsync<List<SentryIssue>>()
                   ?? new List<SentryIssue>();
        }

        // Fetch single issue detail
        public async Task<SentryIssue> GetIssueAsync(string issueId)
        {
            var response = await _http.GetAsync($"{BaseUrl}/issues/{issueId}/");
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<SentryIssue>()
                   ?? throw new Exception($"Issue {issueId} not found");
        }

        // Fetch events/stacktrace for an issue
        public async Task<List<SentryEvent>> GetIssueEventsAsync(string issueId)
        {
            var response = await _http.GetAsync(
                $"{BaseUrl}/issues/{issueId}/events/");
            response.EnsureSuccessStatusCode();
            return await response.Content
                .ReadFromJsonAsync<List<SentryEvent>>()
                   ?? new List<SentryEvent>();
        }
    }
}
