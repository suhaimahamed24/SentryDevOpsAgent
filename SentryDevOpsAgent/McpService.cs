using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using SentryIssuesAgent;

public class McpService : IMcpService
{
    private readonly AzureDevopsOptions _options;
    private McpClient? _client;

    public McpService(IOptions<AzureDevopsOptions> options)
    {
        _options = options.Value;
    }

    public async Task<McpClient> GetClientAsync()
    {
        if (_client != null)
            return _client;

        _client = await McpClient.CreateAsync(
            new StdioClientTransport(new()
            {
                Name = "AzureDevOpsMcp",
                Command = "npx",
                Arguments =
                [
                    "-y",
                    "@azure-devops/mcp",
                    _options.OrganizationName,
                    "--authentication",
                    "envvar"
                ],
                EnvironmentVariables = new Dictionary<string, string>
                {
                    ["ADO_MCP_AUTH_TOKEN"] = _options.AuthToken
                }
            })
        );

        return _client;
    }
}