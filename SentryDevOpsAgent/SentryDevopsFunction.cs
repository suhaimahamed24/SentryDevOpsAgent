using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SentryIssuesAgent;

public class SentryDevopsFunction
{
    private readonly AzureDevOpsService _azureDevOpsService;
    private readonly IOptions<SentryMcpOptions> _sentryMcpOptions;
    private readonly IOptions<AzureAIOptions> _azureAIOptions;

    public SentryDevopsFunction(
        AzureDevOpsService azureDevOpsService,
        IOptions<SentryMcpOptions> sentryMcpOptions,
        IOptions<AzureAIOptions> azureAIOptions)
    {
        _azureDevOpsService = azureDevOpsService;
        _sentryMcpOptions = sentryMcpOptions;
        _azureAIOptions = azureAIOptions;
    }

    [Function("ProcessSentryIssues")]
    public async Task Run(
        [TimerTrigger("0 */5 * * * *")] TimerInfo timer,
        FunctionContext context)
    {
        var logger = context.GetLogger("ProcessSentryIssues");
        await ProcessAsync(logger);
    }

    [Function("ManualRun")]
    public async Task<HttpResponseData> ManualRun(
    [HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestData req,
    FunctionContext context)
    {
        var logger = context.GetLogger("ManualRun");

        logger.LogInformation("Manual trigger started");

        try
        {
            await ProcessAsync(logger);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in ManualRun");
            throw;
        }
        var response = req.CreateResponse(System.Net.HttpStatusCode.OK);
        await response.WriteStringAsync("Executed successfully");

        return response;
    }

    private async Task ProcessAsync(ILogger logger)
    {
        await using var sentryAgent = await SentryAgentClient.CreateAsync(
            _azureAIOptions,
            _sentryMcpOptions);

        var searchResult = await sentryAgent.SearchIssuesAsync(
            "Search unresolved issues in arf-frontend project in exact-software organization from production environment, limit to 3");

        if (searchResult is null || searchResult.Issues.Count == 0)
        {
            logger.LogInformation("No Sentry issues found.");
            return;
        }

        foreach (var sentryIssue in searchResult.Issues)
        {
            var detail = await sentryAgent.GetIssueDetailsAsync(
                sentryIssue.Id,
                _sentryMcpOptions.Value.DefaultOrganizationSlug);

            if (detail == null)
                continue;

            var stackTrace = sentryAgent.GetStackTraceAsString(detail);

            var workItem = new WorkItem
            {
                Title = sentryIssue.Title,
                AssignedTo = "Suhaim Ahamed",
                Description = sentryIssue.Url,
                AreaPath = "EOL-AnnualReporting-Fiscal\\Annual Reporting",
                IterationPath = "EOL-AnnualReporting-Fiscal\\Annual Reporting\\Nova\\2026\\Sprint 1112",
                Tags = $"SentryIssueId_{sentryIssue.Id}",
                ReproSteps = $@"
                <h3>Stack Trace</h3>
                <pre>{stackTrace}</pre>

                <h3>Sentry Issue</h3>
                <a href=""{sentryIssue.Url}"" target=""_blank"">Open in Sentry</a>
            "
            };

            var id = await _azureDevOpsService.CreateBugFromSentryAsync(workItem, sentryIssue.Id);

            logger.LogInformation($"Created Bug ID: {id}");
        }
    }
}