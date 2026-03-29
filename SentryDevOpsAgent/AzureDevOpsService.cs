using Azure.AI.OpenAI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using SentryIssuesAgent;
using System.ClientModel;

public class AzureDevOpsService
{
    private readonly AzureAIOptions azureAIOptions;
    private readonly AzureDevopsOptions azureDevopsOptions;
    private readonly IMcpService _mcpService;


    public AzureDevOpsService(
        IOptions<AzureAIOptions> azureAIOptions,
        IOptions<AzureDevopsOptions> azureDevopsOptions,
        IMcpService mcpService)
    {
        _mcpService = mcpService;
        this.azureAIOptions = azureAIOptions.Value;
        this.azureDevopsOptions = azureDevopsOptions.Value;
    }

    public async Task<int?> CreateBugFromSentryAsync(WorkItem workItem, string sentryIssueId)
    {
        var client = new AzureOpenAIClient(
           new Uri(azureAIOptions.Endpoint),
           new ApiKeyCredential(azureAIOptions.ApiKey)
       );

        var mcpClient = await _mcpService.GetClientAsync();
        var mcpTools = await mcpClient.ListToolsAsync();

        // Custom WIQL-backed duplicate check tool
        //var duplicateBugCheckTool = new DuplicateBugCheckTool(
        //    azureDevopsOptions.OrganizationName,
        //    azureDevopsOptions.ProjectName,
        //    azureDevopsOptions.AuthToken);

        //// Combine MCP tools + custom WIQL tool
        //var allTools = mcpTools
        //    .Cast<AITool>()
        //    .Append(duplicateBugCheckTool.CheckDuplicateTool)
        //    .ToList();

        var duplicateBugCheckTool = new DuplicateBugCheckToolWithWit(
            azureDevopsOptions.OrganizationName,
            azureDevopsOptions.ProjectName,
            azureDevopsOptions.AuthToken);

        var allTools = mcpTools
            .Cast<AITool>()
            .Append(duplicateBugCheckTool.CheckDuplicateTool)
            .ToList();

        var agent = client
            .GetChatClient(azureAIOptions.DeploymentName)
            .AsAIAgent(
                instructions: $$"""
You are an Azure DevOps assistant for organization '{{azureDevopsOptions.OrganizationName}}'.

You MUST always use the Azure DevOps MCP tools to retrieve work item data.
Never fabricate or guess work item information.

You MUST follow these steps in strict order:

STEP 1 — Duplicate check:
  Call the 'check_duplicate_bug' tool with the provided Sentry issue ID.
  - If it returns a value >= 1, a bug already exists with that ID. Return it immediately in the JSON format below. Do NOT create a new bug.
  - If it returns -1, proceed to STEP 2.

STEP 2 — Create bug:
  Use the Azure DevOps MCP tools to create a new Bug work item.
  Always target project '{{azureDevopsOptions.ProjectName}}'.
  If a project parameter is missing, automatically set it to '{{azureDevopsOptions.ProjectName}}'.

Return results strictly in this JSON format:

{
  "workItems": [
    {
      "id": number,
      "type": "string",
      "title": "string",
      "createdDate": "string",
      "createdBy": "string",
      "assignedTo": "string",
      "state": "string",
      "areaPath": "string",
      "iterationPath": "string",
      "url": "string"
    }
  ]
}

If nothing is found return: { "workItems": [] }

Return ONLY JSON.
""",
                tools: allTools
            );

        var session = await agent.CreateSessionAsync();

        string prompt = $"""
            Sentry Issue ID (used as duplicate key): {sentryIssueId}

            STEP 1: Call 'check_duplicate_bug' with sentryIssueId = '{sentryIssueId}'.
            - If result >= 1, return it immediately as a work item JSON. Stop here.
            - If result is -1, continue to STEP 2.

            STEP 2: Create a new Bug workitem in Azure DevOps with the following details:
            Title: {workItem.Title}
            Description: {workItem.Title}
            AssignedTo: {workItem.AssignedTo}
            AreaPath: {workItem.AreaPath}
            IterationPath: {workItem.IterationPath}
            Tags: {workItem.Tags}
            ReproSteps: {workItem.ReproSteps}

            Use Azure DevOps MCP tools to create the bug.
            
            Return the created work item in JSON with its ID.
            """;

        AgentResponse<WorkItemResults> response =
            await agent.RunAsync<WorkItemResults>(prompt, session);

        return response.Result?.WorkItems?.FirstOrDefault()?.Id;
    }


    public async Task<List<WorkItem>> FetchWorkItemsAsync(string query)
    {
        var client = new AzureOpenAIClient(
            new Uri(azureAIOptions.Endpoint),
            new ApiKeyCredential(azureAIOptions.ApiKey)
        );

        var mcpClient = await _mcpService.GetClientAsync();
        var tools = await mcpClient.ListToolsAsync();

        var agent = client
            .GetChatClient("gpt-5-nano")
            .AsAIAgent(
                instructions: $$"""
You are an Azure DevOps assistant for organization '{{azureDevopsOptions.OrganizationName}}'.

You MUST always use the Azure DevOps MCP tools to retrieve work item data.
Never fabricate or guess work item information.

All operations must target the project 'EOL-AnnualReporting-Fiscal'.
If a project parameter is missing, automatically set it to '{{azureDevopsOptions.ProjectName}}'.
Never ask the user for project name.

Return results strictly in this JSON format:

{
  "workItems": [
    {
      "id": number,
      "type": "string",
      "title": "string",
      "createdDate": "string",
      "createdBy": "string",
      "assignedTo": "string",
      "state": "string",
      "areaPath": "string",
      "iterationPath": "string",
      "url": "string"
    }
  ]
}

If nothing is found return:

{ "workItems": [] }

Return ONLY JSON.
""",
            tools: tools.Cast<AITool>().ToList()
        );

        var session = await agent.CreateSessionAsync();

        AgentResponse<WorkItemResults> response =
            await agent.RunAsync<WorkItemResults>(query, session);

        return response.Result?.WorkItems ?? [];
    }
}