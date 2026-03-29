using Azure.Core;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Ms_Agent_Demo
{
    #region SentryResponse Models

    /// <summary>
    /// Structured response model for Sentry issue queries.
    /// Used with ChatResponseFormat.ForJsonSchema&lt;SentryResponse&gt;() 
    /// to enforce structured JSON output from the LLM.
    /// </summary>
    public class SentryResponse
    {
        public int TotalCount { get; set; }

        public List<SentryResponseIssue> Issues { get; set; } = [];
    }

    public class SentryResponseIssue
    {
        public string Id { get; set; }

        public string Url { get; set; }

        public string Title { get; set; }

        public string Status { get; set; }
        public int Events { get; set; }

        public string FirstSeen { get; set; }

        public string LastSeen { get; set; }

        public string Culprit { get; set; }

        public string? Actionability { get; set; }
    }

    public class SentryOrg
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public string? WebUrl { get; set; }
        public string? RegionUrl { get; set; }
    }

    /// <summary>
    /// Combines issue details with the Seer AI root-cause analysis / potential fix.
    /// Mirrors the two-step flow that GitHub Copilot Chat performs:
    ///   1. <c>get_issue_details</c>  → populates <see cref="Issue"/> and <see cref="IssueDetailRaw"/>
    ///   2. <c>analyze_issue_with_seer</c> → populates <see cref="SeerAnalysisRaw"/>
    /// </summary>
    public class SentryIssueWithFix
    {
        /// <summary>Parsed issue (may be null if parsing failed).</summary>
        public SentryIssue? Issue { get; set; }

        /// <summary>Raw markdown/text from <c>get_issue_details</c>.</summary>
        public string? IssueDetailRaw { get; set; }

        /// <summary>
        /// Raw markdown/text from <c>analyze_issue_with_seer</c>.
        /// Contains root-cause analysis, affected files, line numbers, and code fix suggestions.
        /// This is the part that produces the "potential fix" shown in GitHub Copilot Chat.
        /// </summary>
        public string? SeerAnalysisRaw { get; set; }

        /// <summary>Whether Seer analysis was successfully retrieved.</summary>
        public bool HasFix => !string.IsNullOrWhiteSpace(SeerAnalysisRaw);
    }
    #endregion

    public class SentryHelper
    {
        private McpClient? mcpClient { get; set; }

        private static readonly string SentryInstructions = """
                You are a helpful assistant with access to Sentry error tracking.

                Important:
                - Organization and project values are "slugs" — lowercase, hyphen-separated identifiers.
                - "exact-software organization" → organizationSlug = "exact-software"
                - "arf-frontend project" → projectSlug = "arf-frontend"
                - For date ranges, use Sentry's relative syntax: "firstSeen:-14d" means last 14 days.
                - Always call the tool with explicit slug values extracted from the user's message.

                Response format:
                You must respond only with valid JSON. No markdown, no explanation, no code fences.
                Use this exact schema:
                {
                  "totalcount": <number>,
                  "issues": [
                    {
                      "id": "<issue short id, e.g. ARF-FRONTEND-9>",
                      "url": "<sentry issue url>",
                      "title": "<error message>",
                      "status": "<unresolved|resolved|ignored>",
                      "users": <number>,
                      "events": <number>,
                      "assignedto": "<name or null>",
                      "firstseen": "<relative time>",
                      "lastseen": "<relative time>",
                      "culprit": "<culprit string>",
                      "actionability": "<low|medium|high or null>"
                    }
                  ]
                }
                """;

        public SentryHelper()
        {

        }

        public McpClient GetClient()
        {
            string sentryToken = Environment.GetEnvironmentVariable("SENTRY_AUTH_TOKEN") ?? string.Empty;
            string openApiKey = Environment.GetEnvironmentVariable("Devon_Open_Api_Key") ?? string.Empty;
            
            var transport = new StdioClientTransport(new()
            {
                Name = "SentryMCP",
                Command = "npx",
                Arguments = ["-y", "mcp-remote@latest", "https://mcp.sentry.dev/mcp"],
                EnvironmentVariables = new Dictionary<string, string?>
                {
                    ["SENTRY_ACCESS_TOKEN"] = sentryToken,
                    ["SENTRY_HOST"] = "https://sentry.io/settings/exact-software",
                    ["EMBEDDED_AGENT_PROVIDER"] = "azureopenai",
                    ["OPENAI_API_KEY"] = openApiKey
                }
            });

            mcpClient = McpClient.CreateAsync(transport,
            new McpClientOptions
            {
                ClientInfo = new Implementation { Name = "Ms_Agent_Demo", Version = "1.0" },
            }, cancellationToken: CancellationToken.None).Result;

            return mcpClient;
        }

        public async Task<string?> GetOrganizations()
        {
            var result = await InvokeMcpToolAsync("find_organizations", new Dictionary<string, object?>());
            return result?.ToString();
        }

        public async Task<string?> GetProjects()
        {
            string _regionUrl = "https://de.sentry.io";
            string _organizationSlug = "exact-software";

            var parameters = new Dictionary<string, object?>
            {
                ["organizationSlug"] = _organizationSlug,
                ["regionUrl"] = _regionUrl
            };
            var result = await InvokeMcpToolAsync("find_projects", parameters);
            return result?.ToString();
        }

        public async Task<List<SentryIssue>> GetIssues()
        {
            //var parameters = new Dictionary<string, object?>
            //{
            //    ["organization_slug"] = "exact-software",
            //    ["project_slug"] = "arf-frontend",
            //    ["query"] = query
            //};
            string sortBy = "date"; // or "freq"
            string environment = "production";
            string query = "seen a month ago";
            int limit = 5;
            string _organizationSlug = "exact-software";
            string _projectSlug = "arf-frontend";
            string _regionUrl = "https://de.sentry.io";

            // Build natural language query for the Sentry MCP
            string naturalLanguageQuery = sortBy switch
            {
                "date" => "most recent unresolved issues",
                "freq" => "most frequent unresolved issues",
                _ => "unresolved issues"
            };

            if (!string.IsNullOrEmpty(environment))
            {
                naturalLanguageQuery += $" in {environment} environment";
            }

            if (!string.IsNullOrEmpty(query))
            {
                naturalLanguageQuery += $" {query}";
            }

            try
            {
                // First, ensure we have organization info
                //if (string.IsNullOrEmpty(_organizationSlug))
                //{
                //    await InitializeOrganizationAsync();
                //}

                var searchParams = new Dictionary<string, object?>
                {
                    ["organizationSlug"] = _organizationSlug,
                    ["regionUrl"] = _regionUrl,
                    ["naturalLanguageQuery"] = naturalLanguageQuery,
                    ["limit"] = limit,
                    ["projectSlugOrId"] = _projectSlug
                };

                // Add project filter if configured (single project takes precedence)
                //if (!string.IsNullOrEmpty(_projectSlug))
                //{
                //    searchParams["projectSlugOrId"] = _projectSlug;
                //}
                //else if (_projectSlugs.Count == 1)
                //{
                //    searchParams["projectSlugOrId"] = _projectSlugs[0];
                //}

                var result = await InvokeMcpToolAsync("search_issues", searchParams);
                return ParseIssues(result.Content[0]?.ToString());
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Error retrieving issues: {ex.Message}", ex);
            }
        }


        private async Task<CallToolResult> InvokeMcpToolAsync(string toolName, Dictionary<string, object?> parameters)
        {
            try
            {
                mcpClient ??= GetClient();
                CallToolResult result = await mcpClient.CallToolAsync(toolName, parameters);
                return result;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Error invoking MCP tool '{toolName}': {ex.Message}", ex);
            }
        }


        /// <summary>
        /// Parses a <see cref="CallToolResult"/> into a list of <typeparamref name="T"/> objects.
        /// <para>
        /// Supports markdown returned by Sentry MCP tools. Each <c>## **heading**</c> starts
        /// a new item, and <c>**Label:** value</c> lines are mapped to writable string
        /// properties on <typeparamref name="T"/> by normalising the label (removing spaces,
        /// hyphens, underscores) and comparing case-insensitively.
        /// </para>
        /// <para>
        /// The heading value is automatically assigned to the first matching property
        /// among Name, Slug, Id (in that order).
        /// </para>
        /// <para>
        /// If the response contains valid JSON (array or object with a known wrapper
        /// property), it will be deserialized directly instead of parsing markdown.
        /// </para>
        /// </summary>
        /// <typeparam name="T">The target model type. Must have a parameterless constructor.</typeparam>
        /// <param name="result">The <see cref="CallToolResult"/> to parse.</param>
        /// <returns>A list of <typeparamref name="T"/> objects extracted from the result.</returns>
        public static List<T> ParseCallToolResult<T>(CallToolResult result) where T : class, new()
        {
            if (result?.Content is null || result.Content.Count == 0)
                return [];

            //// --- 1. Try structured JSON content first ---
            //if (result.StructuredContent is System.Text.Json.Nodes.JsonNode structured)
            //{
            //    var jsonString = structured.ToJsonString();
            //    var deserialized = TryDeserializeJson<T>(jsonString);
            //    if (deserialized is not null)
            //        return deserialized;
            //}

            // --- 2. Concatenate all text content blocks ---
            var sb = new StringBuilder();
            foreach (var block in result.Content)
            {
                if (block?.Type == "text")
                    sb.AppendLine(block.ToString());
            }

            var text = sb.ToString();
            if (string.IsNullOrWhiteSpace(text))
                return [];

            // --- 3. Try to parse as JSON (some tools return JSON in text blocks) ---
            var trimmed = text.Trim();
            if (trimmed.StartsWith("{", StringComparison.Ordinal) ||
                trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                var deserialized = TryDeserializeJson<T>(trimmed);
                if (deserialized is not null)
                    return deserialized;
            }

            // --- 4. Parse markdown ---
            return ParseMarkdownToModels<T>(text);
        }

        public async Task<SentryIssue> GetIssueDetailsAsync(string issueId)
        {
            string _regionUrl = "https://de.sentry.io";

            try
            {
                var result = await InvokeMcpToolAsync("get_issue_details", new Dictionary<string, object?>
                {
                    ["organizationSlug"] = "exact-software",
                    ["regionUrl"] = _regionUrl,
                    //["issueId"] = issueId,
                    ["issueUrl"] = "https://exact-software.sentry.io/issues/1797054/events/5e131e41ad8b4e089a59fd68dcafc322/"
                });

                var issues = ParseIssues(result);
                return issues.FirstOrDefault();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to get issue details: {ex}");
                return null;
            }
        }

        /// <summary>
        /// Gets issue details AND the AI-powered root cause analysis with potential fix
        /// from Sentry's Seer. This is the equivalent of what GitHub Copilot Chat does
        /// when it shows a "potential fix" — it calls two tools:
        ///   1. <c>get_issue_details</c>  → issue metadata &amp; stack trace
        ///   2. <c>analyze_issue_with_seer</c> → root cause analysis &amp; code fix
        /// </summary>
        /// <param name="issueId">The Sentry issue short ID (e.g. "ARF-FRONTEND-9") or numeric ID.</param>
        /// <param name="issueUrl">Full Sentry issue URL. If provided, takes precedence over issueId.</param>
        /// <param name="organizationSlug">Organization slug (defaults to "exact-software").</param>
        /// <param name="regionUrl">Region URL (defaults to "https://de.sentry.io").</param>
        /// <returns>An object containing both issue details and the Seer fix analysis.</returns>
        public async Task<SentryIssueWithFix> GetIssueDetailsWithFixAsync(
            string? issueId = null,
            string? issueUrl = null,
            string organizationSlug = "exact-software",
            string regionUrl = "https://de.sentry.io")
        {
            var response = new SentryIssueWithFix();

            // --- Step 1: get_issue_details ---
            try
            {
                var detailArgs = new Dictionary<string, object?>
                {
                    ["organizationSlug"] = organizationSlug,
                    ["regionUrl"] = regionUrl
                };

                if (!string.IsNullOrEmpty(issueUrl))
                    detailArgs["issueUrl"] = issueUrl;
                else if (!string.IsNullOrEmpty(issueId))
                    detailArgs["issueId"] = issueId;

                CallToolResult detailResult = await InvokeMcpToolAsync("get_issue_details", detailArgs);
                response.IssueDetailRaw = ExtractText(detailResult);

                //var parsed = ParseIssues(detailResult);
                //response.Issue = parsed.FirstOrDefault();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to get issue details: {ex}");
            }

            // --- Step 2: analyze_issue_with_seer  (this is what produces the fix) ---
            try
            {
                var seerArgs = new Dictionary<string, object?>
                {
                    ["organizationSlug"] = organizationSlug,
                    ["regionUrl"] = regionUrl
                };

                if (!string.IsNullOrEmpty(issueUrl))
                    seerArgs["issueUrl"] = issueUrl;
                else if (!string.IsNullOrEmpty(issueId))
                    seerArgs["issueId"] = issueId;

                CallToolResult seerResult = await InvokeMcpToolAsync("analyze_issue_with_seer", seerArgs);
                response.SeerAnalysisRaw = ExtractText(seerResult);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Seer analysis failed (may not be available for this issue): {ex.Message}");
                // Seer analysis is optional — the issue details are still useful without it
            }

            return response;
        }
        /// <summary>
        /// Extracts the raw text content from a <see cref="CallToolResult"/> as a single string.
        /// Useful when you need the unparsed markdown/text before deciding how to process it.
        /// </summary>
        public static string ExtractText(CallToolResult result)
        {
            if (result?.Content is null || result.Content.Count == 0)
                return string.Empty;

            var sb = new StringBuilder();
            foreach (var block in result.Content)
            {
                if (block?.Type == "text")
                    sb.AppendLine(block.ToString());
            }
            return sb.ToString();
        }

        #region ParseCallToolResult helpers

        /// <summary>
        /// Attempts to deserialize a JSON string into a List&lt;T&gt;.
        /// Handles JSON arrays directly or objects with common wrapper properties
        /// ("data", "items", "results", "organizations", "projects", "issues").
        /// Returns null if deserialization fails.
        /// </summary>
        private static List<T>? TryDeserializeJson<T>(string json) where T : class, new()
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            try
            {
                var element = JsonSerializer.Deserialize<JsonElement>(json, options);

                if (element.ValueKind == JsonValueKind.Array)
                    return JsonSerializer.Deserialize<List<T>>(json, options);

                if (element.ValueKind == JsonValueKind.Object)
                {
                    // Try common wrapper properties
                    string[] wrapperKeys = ["data", "items", "results", "organizations", "projects", "issues"];
                    foreach (var key in wrapperKeys)
                    {
                        if (element.TryGetProperty(key, out var inner) && inner.ValueKind == JsonValueKind.Array)
                            return JsonSerializer.Deserialize<List<T>>(inner.GetRawText(), options);
                    }

                    // Single object → wrap in list
                    var single = JsonSerializer.Deserialize<T>(json, options);
                    if (single is not null)
                        return [single];
                }
            }
            catch (JsonException) { /* Not valid JSON, fall through */ }

            return null;
        }

        /// <summary>
        /// Parses markdown text into a list of <typeparamref name="T"/> objects.
        /// <c>## **heading**</c> starts a new item; <c>**Label:** value</c> lines populate properties.
        /// </summary>
        private static List<T> ParseMarkdownToModels<T>(string markdown) where T : class, new()
        {
            var items = new List<T>();
            var lines = markdown.Split('\n');

            // Cache writable string properties for T
            var props = typeof(T)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanWrite && p.PropertyType == typeof(string) || p.PropertyType == typeof(string))
                .ToArray();

            // Build a lookup: normalised name → PropertyInfo
            var propLookup = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in props)
            {
                var normalised = NormaliseKey(p.Name);
                propLookup.TryAdd(normalised, p);

                // Also register [JsonPropertyName] alias if present
                var jsonAttr = p.GetCustomAttribute<JsonPropertyNameAttribute>();
                if (jsonAttr is not null)
                    propLookup.TryAdd(NormaliseKey(jsonAttr.Name), p);
            }

            // Properties to receive the heading value (first match wins)
            string[] headingTargets = ["Name", "Slug", "Id"];

            T? current = null;

            foreach (var rawLine in lines)
            {
                var line = rawLine.TrimEnd();

                // --- New item: ## **heading-text** ---
                var headingMatch = Regex.Match(line, @"^##\s+\*\*(.+?)\*\*");
                if (headingMatch.Success)
                {
                    if (current is not null)
                        items.Add(current);

                    current = new T();
                    var value = headingMatch.Groups[1].Value.Trim();

                    // Set the heading value on the first available target property
                    foreach (var target in headingTargets)
                    {
                        var normalised = NormaliseKey(target);
                        if (propLookup.TryGetValue(normalised, out var prop))
                        {
                            prop.SetValue(current, value);
                        }
                    }
                    continue;
                }

                if (current is null)
                    continue;

                // --- Property line: **Some Label:** some value ---
                var kvMatch = Regex.Match(line, @"\*\*(.+?):\*\*\s*(.+)");
                if (kvMatch.Success)
                {
                    var label = NormaliseKey(kvMatch.Groups[1].Value);
                    var value = kvMatch.Groups[2].Value.Trim();

                    if (propLookup.TryGetValue(label, out var prop))
                    {
                        prop.SetValue(current, value);
                    }
                    continue;
                }

                // --- List-item property: - **Label:** value  or  * **Label:** value ---
                var listKvMatch = Regex.Match(line, @"^[-*]\s+\*\*(.+?):\*\*\s*(.+)");
                if (listKvMatch.Success)
                {
                    var label = NormaliseKey(listKvMatch.Groups[1].Value);
                    var value = listKvMatch.Groups[2].Value.Trim();

                    if (propLookup.TryGetValue(label, out var prop))
                    {
                        prop.SetValue(current, value);
                    }
                    continue;
                }
            }

            // Don't forget the last item
            if (current is not null)
                items.Add(current);

            return items;
        }

        /// <summary>
        /// Normalises a key by lowering case and stripping spaces, hyphens, and underscores.
        /// e.g. "Region URL" → "regionurl", "web_url" → "weburl", "Web-Url" → "weburl".
        /// </summary>
        private static string NormaliseKey(string key)
            => Regex.Replace(key, @"[\s_-]+", "").ToLowerInvariant();

        #endregion

        /// <summary>
        /// Formats a <see cref="CallToolResult"/> into a readable string.
        /// Prefers structured JSON content when available; otherwise concatenates
        /// all text content blocks from the result.
        /// </summary>
        private static string FormatCallToolResult(CallToolResult result)
        {
            // Fallback: extract all text content blocks
            if (result.Content is null || result.Content.Count == 0)
                return string.Empty;


            foreach (var content in result.Content)
            {
                if (content?.Type == "text")
                    Console.WriteLine(content.ToString());
            }
            return string.Empty;
        }

        private List<SentryIssue> ParseIssues(object? result)
        {
            if (result == null)
            {
                return new List<SentryIssue>();
            }

            // Handle string response (markdown format)
            if (result is string markdown)
            {
                return ParseIssuesFromMarkdown(markdown);
            }

            if (result is CallToolResult)
            {
                var callResult = (CallToolResult)result;
                var text = ExtractText(callResult);
                return ParseIssuesFromMarkdown(text);
            }
            return new List<SentryIssue>();
        }

        private static List<SentryIssue> ParseIssuesFromMarkdown(string markdown)
        {
            var issues = new List<SentryIssue>();
            var lines = markdown.Split('\n');

            SentryIssue? currentIssue = null;

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                // Match numbered issue header: ## 1. [ISSUE-ID](url)
                var numberedMatch = Regex.Match(line, @"^##\s+\d+\.\s+\[([A-Z0-9-]+)\]\(([^)]+)\)");
                if (numberedMatch.Success)
                {
                    if (currentIssue != null && !string.IsNullOrEmpty(currentIssue.ShortId))
                    {
                        issues.Add(currentIssue);
                    }
                    currentIssue = new SentryIssue
                    {
                        ShortId = numberedMatch.Groups[1].Value,
                        Permalink = numberedMatch.Groups[2].Value,
                        Id = numberedMatch.Groups[1].Value,
                        Status = IssueStatus.Unresolved,
                        Level = IssueLevel.Error,
                        Title = string.Empty
                    };
                    continue;
                }

                // Match title line (bold text after issue header): **Title**
                var titleMatch = Regex.Match(line, @"^\*\*(.+?)\*\*$");
                if (titleMatch.Success && currentIssue != null && string.IsNullOrEmpty(currentIssue.Title))
                {
                    currentIssue.Title = titleMatch.Groups[1].Value;
                    continue;
                }

                // Match status: - **Status**: value
                var statusMatch = Regex.Match(line, @"-\s+\*\*Status\*\*:\s*(\w+)", RegexOptions.IgnoreCase);
                if (statusMatch.Success && currentIssue != null)
                {
                    currentIssue.Status = ParseIssueStatus(statusMatch.Groups[1].Value);
                    continue;
                }

                // Match users: - **Users**: value
                var usersMatch = Regex.Match(line, @"-\s+\*\*Users\*\*:\s*([\d,]+)", RegexOptions.IgnoreCase);
                if (usersMatch.Success && currentIssue != null)
                {
                    if (int.TryParse(usersMatch.Groups[1].Value.Replace(",", ""), out var userCount))
                    {
                        currentIssue.UserCount = userCount;
                    }
                    continue;
                }

                // Match events: - **Events**: value
                var eventsMatch = Regex.Match(line, @"-\s+\*\*Events\*\*:\s*([\d,]+)", RegexOptions.IgnoreCase);
                if (eventsMatch.Success && currentIssue != null)
                {
                    currentIssue.Count = eventsMatch.Groups[1].Value.Replace(",", "");
                    continue;
                }
                                

                // Match first seen: - **First seen**: value
                var firstSeenMatch = Regex.Match(line, @"-\s+\*\*First seen\*\*:\s*(.+)", RegexOptions.IgnoreCase);
                if (firstSeenMatch.Success && currentIssue != null)
                {
                    currentIssue.FirstSeen = firstSeenMatch.Groups[1].Value.Trim();
                    continue;
                }

                // Match last seen: - **Last seen**: value
                var lastSeenMatch = Regex.Match(line, @"-\s+\*\*Last seen\*\*:\s*(.+)", RegexOptions.IgnoreCase);
                if (lastSeenMatch.Success && currentIssue != null)
                {
                    currentIssue.LastSeen = lastSeenMatch.Groups[1].Value.Trim();
                    continue;
                }

                // Match culprit: - **Culprit**: value
                var culpritMatch = Regex.Match(line, @"-\s+\*\*Culprit\*\*:\s*(.+)", RegexOptions.IgnoreCase);
                if (culpritMatch.Success && currentIssue != null)
                {
                    currentIssue.Culprit = culpritMatch.Groups[1].Value.Trim().Replace("`", "");
                    continue;
                }
            }

            // Add the last issue
            if (currentIssue != null && !string.IsNullOrEmpty(currentIssue.ShortId))
            {
                issues.Add(currentIssue);
            }

            return issues;
        }

        private static IssueStatus ParseIssueStatus(string status)
        {
            return status.ToLowerInvariant() switch
            {
                "resolved" => IssueStatus.Resolved,
                "unresolved" => IssueStatus.Unresolved,
                "ignored" => IssueStatus.Ignored,
                _ => IssueStatus.Unresolved
            };
        }

    }
}
