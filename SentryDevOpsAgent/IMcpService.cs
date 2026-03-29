using ModelContextProtocol.Client;

public interface IMcpService
{
    Task<McpClient> GetClientAsync();
}