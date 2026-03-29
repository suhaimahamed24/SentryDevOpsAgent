using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SentryIssuesAgent
{
    public record PagedResult<T>(
    IReadOnlyList<T> Items,
    string? NextCursor,
    bool HasMore
    );
}
