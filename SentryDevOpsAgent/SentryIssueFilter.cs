using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SentryIssuesAgent
{
    public record SentryIssueFilter(
    string Query = "is:unresolved",   // Sentry search query
    string? Level = null,              // "error" | "warning" | "info"
    string? Environment = null,              // "production" | "staging" etc.
    string? SortBy = "date",            // "date" | "freq" | "new" | "priority"
    int Limit = 25
    );
}
