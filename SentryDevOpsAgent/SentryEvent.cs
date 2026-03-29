using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SentryIssuesAgent
{
    public record SentryEvent(
    string Id,
    string Message,
    string Platform,
    object Entries
    );
}
