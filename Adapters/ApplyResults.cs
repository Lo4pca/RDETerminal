using System.Collections.Generic;
using System.Linq;

namespace RDETerminal.Adapters
{
    public sealed class EventApplyResult
    {
        public EventApplyResult()
        {
            FailedMembers = [];
        }

        public string EventType { get; set; }
        public int WrittenCount { get; set; }
        public int SkippedCount { get; set; }
        public List<string> FailedMembers { get; private set; }

        public int FailedCount
        {
            get { return FailedMembers.Count; }
        }

        public bool Success
        {
            get { return FailedCount == 0; }
        }

        public void AddFailure(string memberName, string message)
        {
            if (string.IsNullOrEmpty(memberName))
            {
                memberName = "(unknown)";
            }

            if (string.IsNullOrEmpty(message))
            {
                message = "unknown error";
            }

            FailedMembers.Add(memberName + ": " + message);
        }

        public string ToSummaryString(int maxFailures)
        {
            if (maxFailures < 0)
            {
                maxFailures = 0;
            }

            string head = EventType + ": written=" + WrittenCount + ", skipped=" + SkippedCount + ", failed=" + FailedCount;

            if (FailedMembers.Count == 0 || maxFailures == 0)
            {
                return head;
            }

            List<string> shown = FailedMembers.Take(maxFailures).ToList();
            string tail = FailedMembers.Count > maxFailures
                ? " ... (+" + (FailedMembers.Count - maxFailures) + " more)"
                : "";

            return head + ", issues=[" + string.Join("; ", shown) + "]" + tail;
        }
    }

    public sealed class LevelApplyResult
    {
        public LevelApplyResult()
        {
            Issues = [];
        }

        public int TotalEvents { get; set; }
        public int AppliedEvents { get; set; }
        public int SkippedEvents { get; set; }
        public int WrittenCount { get; set; }
        public int FailedCount { get; set; }
        public List<string> Issues { get; private set; }

        public bool Success
        {
            get { return FailedCount == 0; }
        }

        public void AddIssue(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            Issues.Add(message);
        }

        public string ToSummaryString(int maxIssues)
        {
            string head = "events=" + TotalEvents
                + ", applied=" + AppliedEvents
                + ", skipped=" + SkippedEvents
                + ", written=" + WrittenCount
                + ", failed=" + FailedCount;

            if (Issues.Count == 0 || maxIssues <= 0)
            {
                return head;
            }

            List<string> shown = [.. Issues.Take(maxIssues)];
            string tail = Issues.Count > maxIssues
                ? " ... (+" + (Issues.Count - maxIssues) + " more)"
                : "";

            return head + ", issues=[" + string.Join("; ", shown) + "]" + tail;
        }
    }
}