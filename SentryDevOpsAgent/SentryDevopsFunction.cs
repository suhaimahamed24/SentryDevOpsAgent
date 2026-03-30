using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SentryIssuesAgent;
namespace SentryDevOpsAgent.Functions;

public class SentryDevopsFunction
{
    private readonly ILogger<SentryDevopsFunction> _logger;
    private readonly AzureDevOpsService _azureDevOpsService;
    private readonly IOptions<SentryMcpOptions> _sentryMcpOptions;
    private readonly IOptions<AzureAIOptions> _azureAIOptions;

    public SentryDevopsFunction(ILogger<SentryDevopsFunction> logger, 
        AzureDevOpsService azureDevOpsService,
        IOptions<SentryMcpOptions> sentryMcpOptions,
        IOptions<AzureAIOptions> azureAIOptions)
    {
        _logger = logger;
        _azureDevOpsService = azureDevOpsService;
        _sentryMcpOptions = sentryMcpOptions;
        _azureAIOptions = azureAIOptions;
    }

    [Function("SentryDevopsFunction")]
    public void TestTimer([TimerTrigger("*/30 * * * * *")] TimerInfo timer)
    {
        _logger.LogInformation("🔥 v2: Hello from TestTimerFunction at: {time}", DateTime.UtcNow);
        _logger.LogInformation("Org: {org}",
            _sentryMcpOptions.Value.AccessToken);
    }

    [Function("ProcessSentryIssues")]
    public async Task ProcessSentryIssuesTimer(
        [TimerTrigger("0 */5 * * * *")] TimerInfo timer,
        FunctionContext context)
    {
        await ProcessAsync();
    }

    [Function("ManualRun")]
    public async Task<HttpResponseData> ManualRun(
    [HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestData req,
    FunctionContext context)
    {
        _logger.LogInformation("Manual trigger started");

        try
        {
            await ProcessAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString()); // 👈 IMPORTANT
            _logger.LogError(ex, "Error in ManualRun");
            throw;
        }
        var response = req.CreateResponse(System.Net.HttpStatusCode.OK);
        await response.WriteStringAsync("Executed successfully");

        return response;
    }

    private async Task ProcessAsync()
    {
        _logger.LogInformation("I am herer");
        SentryAgentClient? sentryAgent = null;

        try
        {
            sentryAgent = await SentryAgentClient.CreateAsync(
                _azureAIOptions,
                _sentryMcpOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create MCP client");
            return;
        }

        await using (sentryAgent)
        {
            var searchResult = await sentryAgent.SearchIssuesAsync(
                "Search unresolved issues in arf-frontend project in exact-software organization from production environment, limit to 3");

            if (searchResult is null || searchResult.Issues.Count == 0)
            {
                _logger.LogInformation("No Sentry issues found.");
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

                _logger.LogInformation($"Created Bug ID: {id}");
            }
        }
    }
}
