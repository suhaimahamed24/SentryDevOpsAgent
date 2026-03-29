using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;

public class DuplicateBugCheckToolWithWit
{
    private readonly string _projectName;
    private readonly WorkItemTrackingHttpClient _witClient;

    public DuplicateBugCheckToolWithWit(
        string organizationName,
        string projectName,
        string authToken)
    {
        _projectName = projectName;

        var credentials = new VssBasicCredential("", authToken);

        var connection = new VssConnection(
            new Uri($"https://dev.azure.com/{organizationName}"),
            credentials);

        _witClient = connection.GetClient<WorkItemTrackingHttpClient>();
    }

    public AITool CheckDuplicateTool => AIFunctionFactory.Create(
        async ([Description("The Sentry issue ID to check for duplicates, e.g. 'ARF-FRONTEND-3D1'")] string sentryIssueId) =>
        {
            try
            {
                var wiql = new Wiql
                {
                    Query = $@"
                        SELECT [System.Id]
                        FROM WorkItems
                        WHERE [System.TeamProject] = '{_projectName}'
                          AND [System.WorkItemType] = 'Bug'
                          AND [System.Tags] CONTAINS 'SentryIssueId_{sentryIssueId}'
                        ORDER BY [System.CreatedDate] DESC"
                };

                var result = await _witClient.QueryByWiqlAsync(wiql);

                var firstId = result.WorkItems?.FirstOrDefault()?.Id;

                return firstId ?? -1;
            }
            catch (Exception ex)
            {
                // Optional: log this somewhere (App Insights / Console / etc.)
                Console.WriteLine($"Duplicate check failed: {ex.Message}");
                return -1;
            }
        },
        "check_duplicate_bug",
        "Check if an Azure DevOps Bug already exists for the given Sentry issue ID. Returns work item ID or -1 if not found.");
}