using SentryIssuesAgent;
using System.Text.Json.Serialization;

public class WorkItemResults
{
    [JsonPropertyName("workItems")]
    public List<WorkItem> WorkItems { get; set; } = [];
}