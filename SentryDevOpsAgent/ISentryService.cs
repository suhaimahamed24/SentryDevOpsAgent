using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SentryIssuesAgent
{
    public interface ISentryService
    {
        Task<PagedResult<SentryIssue>> GetIssuesAsync(SentryIssueFilter? filter = null, string? cursor = null, CancellationToken ct = default);
        IAsyncEnumerable<SentryIssue> GetAllIssuesAsync(SentryIssueFilter filter, CancellationToken ct = default);
        Task<SentryIssue> GetIssueAsync(string issueId, CancellationToken ct = default);
        Task<IReadOnlyList<SentryEvent>> GetIssueEventsAsync(string issueId, CancellationToken ct = default);
        Task<SentryEvent> GetLatestIssueEventAsync(string issueId, CancellationToken ct = default);
        string GetStackTraceAsString(SentryEvent sentryEvent);
    }
}
