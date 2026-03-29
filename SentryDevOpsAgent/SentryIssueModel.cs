using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Ms_Agent_Demo
{
    public class SentryIssue
    {
        public string Id { get; set; } = string.Empty;
        public string ShortId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Culprit { get; set; } = string.Empty;
        public string Permalink { get; set; } = string.Empty;
        public IssueLevel Level { get; set; } = IssueLevel.Error;
        public IssueStatus Status { get; set; } = IssueStatus.Unresolved;
        public string Count { get; set; } = "0";
        public int UserCount { get; set; }
        public string FirstSeen { get; set; } = string.Empty;
        public string LastSeen { get; set; } = string.Empty;
        public IssueProject Project { get; set; } = new();
        public IssueMetadata Metadata { get; set; } = new();
        public List<StackFrame>? StackTrace { get; set; }
        public Dictionary<string, string>? Tags { get; set; }
        public Dictionary<string, object>? Context { get; set; }
    }

    public class IssueProject
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
    }

    public class IssueMetadata
    {
        public string? Type { get; set; }
        public string? Value { get; set; }
        public string? Filename { get; set; }
        public string? Function { get; set; }
    }

    public enum IssueLevel
    {
        Fatal,
        Error,
        Warning,
        Info,
        Debug
    }

    public enum IssueStatus
    {
        Resolved,
        Unresolved,
        Ignored
    }
}
