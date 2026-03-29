using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace SentryIssuesAgent
{
    public class DuplicateBugCheckTool
    {
        private readonly string _organizationName;
        private readonly string _projectName;
        private readonly string _authToken;

        private static readonly HttpClient Http = new();

        public DuplicateBugCheckTool(string organizationName, string projectName, string authToken)
        {
            _organizationName = organizationName;
            _projectName = projectName;
            _authToken = authToken;
        }

        /// <summary>
        /// Custom AITool that runs a WIQL query against Azure DevOps REST API
        /// to check if a bug already exists whose Description contains the Sentry issue ID.
        /// Returns the existing ADO work item ID, or -1 if no duplicate is found.
        /// </summary>
        public AITool CheckDuplicateTool => AIFunctionFactory.Create(
            async ([Description("The Sentry issue ID to check for duplicates, e.g. 'ARF-FRONTEND-3D1'")] string sentryIssueId) =>
            {
                // WIQL: find any Bug in the project whose Description contains the Sentry issue ID
                var wiql = $"""
                    SELECT [System.Id]
                    FROM WorkItems
                    WHERE [System.TeamProject] = '{_projectName}'
                      AND [System.WorkItemType] = 'Bug'
                      AND [System.Tags] CONTAINS 'SentryIssueId_{sentryIssueId}'
                    ORDER BY [System.CreatedDate] DESC
                    """;

                var requestBody = JsonSerializer.Serialize(new { query = wiql });

                var url = $"https://dev.azure.com/{_organizationName}/{Uri.EscapeDataString(_projectName)}/_apis/wit/wiql?api-version=7.1";

                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Headers.Authorization = new AuthenticationHeaderValue(
                    "Basic",
                    Convert.ToBase64String(Encoding.ASCII.GetBytes($":{_authToken}")));
                request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");

                using var response = await Http.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                    return -1;

                var json = await response.Content.ReadAsStringAsync();
                var doc = JsonNode.Parse(json);

                var firstId = doc?["workItems"]?
                    .AsArray()
                    .FirstOrDefault()?["id"]?
                    .GetValue<int>();

                return firstId ?? -1;
            },
            "check_duplicate_bug",
            "Check if an Azure DevOps Bug already exists for the given Sentry issue ID by querying via WIQL. Returns the existing work item ID or -1 if not found.");
    }
}
