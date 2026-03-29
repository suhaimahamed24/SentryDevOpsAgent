using Azure;
using Azure.AI.OpenAI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Ms_Agent_Demo;
using OpenAI.Chat;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace SentryIssuesAgent
{
    public class SentryAgentClient : IAsyncDisposable
    {
        private readonly SentryMcpOptions _sentrySettings;
        private readonly McpClient _mcpClient;
        private readonly AIAgent _fetchAgent;
        private readonly AIAgent _fetchStructuringAgent;
        private readonly AIAgent _detailAgent;
        private readonly AIAgent _detailStructuringAgent;

        private SentryAgentClient(
        SentryMcpOptions sentrySettings,
        McpClient mcpClient,
        AIAgent fetchAgent,
        AIAgent fetchStructuringAgent,
        AIAgent detailAgent,
        AIAgent detailStructuringAgent)
        {
            _sentrySettings = sentrySettings;
            _mcpClient = mcpClient;
            _fetchAgent = fetchAgent;
            _fetchStructuringAgent = fetchStructuringAgent;
            _detailAgent = detailAgent;
            _detailStructuringAgent = detailStructuringAgent;
        }

        public static async Task<SentryAgentClient> CreateAsync(
        IOptions<AzureAIOptions> azureOptions,
        IOptions<SentryMcpOptions> sentryOptions)
        {
            var azure = azureOptions.Value;
            var sentry = sentryOptions.Value;

            var mcpClient = await McpClient.CreateAsync(new StdioClientTransport(new()
            {
                Name = "MCPServer",
                Command = "npx",
                Arguments = ["-y", "mcp-remote@latest", sentry.McpServerUrl],
                EnvironmentVariables = new Dictionary<string, string?>
                {
                    ["SENTRY_ACCESS_TOKEN"] = sentry.AccessToken,
                    ["SENTRY_HOST"] = sentry.Host,
                }
            }));

            var azureClient = new AzureOpenAIClient(
                new Uri(azure.Endpoint),
                new AzureKeyCredential(azure.ApiKey));

            ChatClient chatClient = azureClient.GetChatClient(azure.DeploymentName);

            AIFunction listIssues = BuildListIssuesFunction(mcpClient, sentry);
            AIFunction getIssueDetails = BuildGetIssueDetailsFunction(mcpClient, sentry);

            // ── Fetch agent: calls list_sentry_issues, returns raw markdown
            AIAgent fetchAgent = chatClient.AsAIAgent(
                instructions: $"""
                You are a helpful assistant with access to Sentry error tracking.

                Important:
                - Organization and project values are "slugs" — lowercase, hyphen-separated identifiers.
                - Default organization slug: "{sentry.DefaultOrganizationSlug}"
                - Default project slug: "{sentry.DefaultProjectSlug}"
                - Always call the tool with explicit slug values extracted from the user's message.
                """,
                name: "SentryFetchAgent",
                tools: [listIssues]);

            // Fetch structuring agent: converts issue list markdown to JSON
            AIAgent fetchStructuringAgent = chatClient.AsAIAgent(
                new ChatClientAgentOptions
                {
                    Name = "IssueFetchStructuringAgent",
                    ChatOptions = new ChatOptions
                    {
                        ResponseFormat = Microsoft.Extensions.AI.ChatResponseFormat.ForJsonSchema<SentryResponse>(),
                        Instructions = """
                        You are a JSON converter. You receive raw markdown text from a Sentry issue search result.
                        Extract ALL issues and return ONLY a valid JSON object matching the schema exactly.
                        No markdown, no explanation, no code fences.
                        Schema:
                        {
                          "totalCount": <number from "Found X issues">,
                          "issues": [
                            {
                              "id": "<issue short id>",
                              "url": "<sentry issue url>",
                              "title": "<error message>",
                              "status": "<unresolved|resolved|ignored>",
                              "users": <number>,
                              "events": <number>,
                              "assignedTo": "<name or null>",
                              "firstSeen": "<relative time>",
                              "lastSeen": "<relative time>",
                              "culprit": "<culprit string>",
                              "actionability": null
                            }
                          ]
                        }
                        """
                    }
                });

            // Detail agent: calls get_sentry_issue_details, returns raw markdown
            AIAgent detailAgent = chatClient.AsAIAgent(
                instructions: $"""
                You are a helpful assistant with access to Sentry error tracking.
                When asked for issue details, call the get_sentry_issue_details tool using:
                - organizationSlug: "{sentry.DefaultOrganizationSlug}" (unless specified otherwise)
                - issueId: the exact issue ID provided by the user
                """,
                name: "SentryDetailAgent",
                tools: [getIssueDetails]);

            // Detail structuring agent: converts detail markdown to JSON
            AIAgent detailStructuringAgent = chatClient.AsAIAgent(
                new ChatClientAgentOptions
                {
                    Name = "IssueDetailStructuringAgent",
                    ChatOptions = new ChatOptions
                    {
                        ResponseFormat = Microsoft.Extensions.AI.ChatResponseFormat.ForJsonSchema<SentryIssueDetail>(),
                        Instructions = """
                        You are a JSON converter. You receive raw markdown text with Sentry issue details.
                        Extract the information and return ONLY a valid JSON object. No markdown, no explanation, no code fences.
                        Schema:
                        {
                          "id": "<issue short id>",
                          "title": "<error title>",
                          "culprit": "<culprit>",
                          "status": "<status>",
                          "firstSeen": "<first seen>",
                          "lastSeen": "<last seen>",
                          "exceptionType": "<exception class name>",
                          "exceptionValue": "<exception message>",
                          "stackTrace": [
                            {
                              "filename": "<file path>",
                              "function": "<function or method name>",
                              "lineNumber": <number or 0 if unknown>,
                              "module": "<module or namespace>",
                              "context": "<relevant code line or empty string>"
                            }
                          ]
                        }
                        """
                    }
                });

            return new SentryAgentClient(
                sentry, mcpClient,
                fetchAgent, fetchStructuringAgent,
                detailAgent, detailStructuringAgent);
        }

        public async Task<SentryResponse?> SearchIssuesAsync(string prompt)
        {
            var fetchResp = await _fetchAgent.RunAsync(prompt);
            string? rawMarkdown = fetchResp?.Text;

            if (string.IsNullOrWhiteSpace(rawMarkdown))
                return null;

            var structuredResp = await _fetchStructuringAgent.RunAsync(rawMarkdown);

            return JsonSerializer.Deserialize<SentryResponse>(
                structuredResp.Text,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }

        /// <summary>
        /// Fetches full details including the stack trace for a single Sentry issue by its ID.
        /// </summary>
        public async Task<SentryIssueDetailWithMarkdown?> GetIssueDetailsAsync(string issueId, string organizationSlug)
        {
            var fetchResp = await _detailAgent.RunAsync(
                $"Get full details for Sentry issue '{issueId}' in the '{organizationSlug}' organization.");

            string? rawMarkdown = fetchResp?.Text;

            if (string.IsNullOrWhiteSpace(rawMarkdown))
                return null;

            var structuredResp = await _detailStructuringAgent.RunAsync(rawMarkdown);

            SentryIssueDetail issueDetails = JsonSerializer.Deserialize<SentryIssueDetail>(
                structuredResp.Text,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if(issueDetails == null)
                return null;

            SentryIssueDetailWithMarkdown issueDetailsWithMarkdown = new SentryIssueDetailWithMarkdown
            {
                Id = issueDetails.Id,
                Title = issueDetails.Title,
                Culprit = issueDetails.Culprit,
                Status = issueDetails.Status,
                FirstSeen = issueDetails.FirstSeen,
                LastSeen = issueDetails.LastSeen,
                StackTrace = issueDetails.StackTrace,
                ExceptionType = issueDetails.ExceptionType,
                ExceptionValue = issueDetails.ExceptionValue,
                Markdown = rawMarkdown
            };
            return issueDetailsWithMarkdown;
        }

        public string GetStackTraceAsString(SentryIssueDetailWithMarkdown issueDetail)
        {
            var stackTraceBuilder = new StringBuilder();

            stackTraceBuilder.AppendLine($"  Exception : {issueDetail.ExceptionType}: {issueDetail.ExceptionValue}");
            stackTraceBuilder.AppendLine($"  Culprit   : {issueDetail.Culprit}");
            stackTraceBuilder.AppendLine($"  Status    : {issueDetail.Status}");
            stackTraceBuilder.AppendLine($"  First seen: {issueDetail.FirstSeen}  |  Last seen: {issueDetail.LastSeen}");
            if (issueDetail.StackTrace.Count > 0)
            {
                stackTraceBuilder.AppendLine("  Stack trace:");
                foreach (var frame in issueDetail.StackTrace)
                {
                    stackTraceBuilder.AppendLine($"    at {frame.Function}  ({frame.Filename}:{frame.LineNumber})");
                    if (!string.IsNullOrWhiteSpace(frame.Context))
                    {
                        stackTraceBuilder.AppendLine($"       > {frame.Context}");
                    }
                }
            }

            return stackTraceBuilder.ToString();
        }

        public async ValueTask DisposeAsync()
        {
            await _mcpClient.DisposeAsync();
        }

        private static AIFunction BuildListIssuesFunction(McpClient mcpClient, SentryMcpOptions sentry) =>
        AIFunctionFactory.Create(
            async (
                [Description("The Sentry organization slug, e.g. 'exact-software'")]
                string organizationSlug,

                [Description("The Sentry project slug, e.g. 'arf-frontend'. Optional — omit to search all projects.")]
                string? projectSlugOrId,

                [Description("A natural language description of the issues to search for, e.g. 'unresolved issues'")]
                string naturalLanguageQuery,

                [Description("The environment to search, e.g. 'production' or 'development'")]
                string environment,

                [Description("Maximum number of issues to return (default 25)")]
                int limit = 25
            ) =>
            {
                var args = new Dictionary<string, object?>
                {
                    ["organizationSlug"] = organizationSlug,
                    ["naturalLanguageQuery"] = naturalLanguageQuery,
                    ["environment"] = environment,
                    ["limit"] = limit,
                };

                if (!string.IsNullOrWhiteSpace(projectSlugOrId))
                    args["projectSlugOrId"] = projectSlugOrId;

                if (!string.IsNullOrWhiteSpace(sentry.RegionUrl))
                    args["regionUrl"] = sentry.RegionUrl;

                var result = await mcpClient.CallToolAsync("search_issues", args);
                return string.Join("\n", result.Content.Select(c => (c as TextContentBlock)?.Text));
            },
            name: "list_sentry_issues",
            description: "List issues from a Sentry project.");

        private static AIFunction BuildGetIssueDetailsFunction(McpClient mcpClient, SentryMcpOptions sentry) =>
        AIFunctionFactory.Create(
            async (
                [Description("The Sentry organization slug, e.g. 'exact-software'")]
                string organizationSlug,

                [Description("The Sentry issue ID, e.g. 'ARF-FRONTEND-3D1'")]
                string issueId
            ) =>
            {
                var args = new Dictionary<string, object?>
                {
                    ["organizationSlug"] = organizationSlug,
                    ["issueId"] = issueId,
                };

                if (!string.IsNullOrWhiteSpace(sentry.RegionUrl))
                    args["regionUrl"] = sentry.RegionUrl;

                var result = await mcpClient.CallToolAsync("get_issue_details", args);

                var fullResponse = string.Join("\n", result.Content.Select(c => (c as TextContentBlock)?.Text));

                const int maxLength = 8000;
                return fullResponse.Length > maxLength
                    ? fullResponse[..maxLength] + "\n... [truncated]"
                    : fullResponse;

            },
            name: "get_sentry_issue_details",
            description: "Get full details and stack trace of a specific Sentry issue by its ID.");
    }
}
