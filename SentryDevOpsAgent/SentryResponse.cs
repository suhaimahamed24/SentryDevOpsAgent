using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace SentryIssuesAgent
{
    [Serializable]
    public class SentryResponse
    {
        public int TotalCount { get; set; } = default!;
        public List<SentryError> Issues { get; set; } = new List<SentryError>();
    }

    [Serializable]
    public class SentryError
    {
        public string Id { get; set; } = default!;
        public string Url { get; set; } = default!;
        public string Title { get; set; } = default!;
        public string Status { get; set; } = default!;
        public int Users { get; set; } = default!;
        public int Events { get; set; } = default!;
        public string AssignedTo { get; set; } = default!;
        public string FirstSeen { get; set; } = default!;
        public string LastSeen { get; set; } = default!;
        public string Culprit { get; set; } = default!;
        public string Actionability { get; set; } = default!;
    }

    [Serializable]
    public class SentryIssueDetail
    {
        public string Id { get; set; } = default!;
        public string Title { get; set; } = default!;
        public string Culprit { get; set; } = default!;
        public string Status { get; set; } = default!;
        public string FirstSeen { get; set; } = default!;
        public string LastSeen { get; set; } = default!;
        
        public List<SentryStackFrame> StackTrace { get; set; } = new List<SentryStackFrame>();
        public string ExceptionType { get; set; } = default!;
        public string ExceptionValue { get; set; } = default!;
        
    }

    public class SentryIssueDetailWithMarkdown
    {
        public string Id { get; set; } = default!;
        public string Title { get; set; } = default!;
        public string Culprit { get; set; } = default!;
        public string Status { get; set; } = default!;
        public string FirstSeen { get; set; } = default!;
        public string LastSeen { get; set; } = default!;

        public List<SentryStackFrame> StackTrace { get; set; } = new List<SentryStackFrame>();
        public string ExceptionType { get; set; } = default!;
        public string ExceptionValue { get; set; } = default!;
        public string Markdown { get; set; } = default!;
    }

    [Serializable]
    public class SentryStackFrame
    {
        public string Filename { get; set; } = default!;
        public string Function { get; set; } = default!;
        public int LineNumber { get; set; } = default!;
        public string Module { get; set; } = default!;
        public string Context { get; set; } = default!;
    }
}
