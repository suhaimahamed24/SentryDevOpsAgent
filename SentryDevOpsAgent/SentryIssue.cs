using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace SentryIssuesAgent
{
    public record SentryIssue(
    string Id,
    string Title,
    string Culprit,
    string Status,          // "unresolved" | "resolved" | "ignored"
    string Level,           // "error" | "warning" | "info"
    [property: JsonConverter(typeof(StringOrIntConverter))] int Count,
    [property: JsonConverter(typeof(StringOrIntConverter))] int UserCount,
    string FirstSeen,
    string LastSeen,
    string Permalink,
    Metadata Metadata
    );

    public record Metadata(
        string FileName,
        string Function,
        string Type,
        string Value
        );

}
