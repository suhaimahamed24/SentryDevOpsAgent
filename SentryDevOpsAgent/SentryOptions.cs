using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SentryIssuesAgent
{
    public class SentryOptions
    {
        public const string Section = "Sentry";

        public string AuthToken { get; set; } = string.Empty;
        public string OrganizationSlug { get; set; } = string.Empty;
        public string ProjectSlug { get; set; } = string.Empty;
        public string BaseUrl { get; set; } = "https://sentry.io/api/0";
        //public int CacheTtlSeconds { get; set; } = 300;   // 5 min default
        public int MaxRetries { get; set; } = 3;
        public int TimeoutSeconds { get; set; } = 30;
        public int DefaultPageSize { get; set; } = 25;
    }

    public class AzureDevopsOptions
    {
        public const string Section = "AzureDevops";

        public string AuthToken { get; set; } = string.Empty;
        public string OrganizationName { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty; 
    }

    public class AzureAIOptions
    {
        public const string Section = "AzureAI";

        public string Endpoint { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public string DeploymentName { get; set; } = string.Empty;
    }

    public class SentryMcpOptions
    {
        public const string SectionName = "Sentry";

        public string AccessToken { get; set; } = string.Empty;
        public string Host { get; set; } = string.Empty;
        public string McpServerUrl { get; set; } = string.Empty;
        public string RegionUrl { get; set; } = string.Empty;
        public string DefaultOrganizationSlug { get; set; } = string.Empty;
        public string DefaultProjectSlug { get; set; } = string.Empty;
    }
}
