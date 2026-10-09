using System.Text.Json.Serialization;

namespace QAFlowRecorder.Models;

public enum AutomationQueueStatus { Queued, Running, Passed, Failed, NeedsReview, Aborted, Cancelled }

public sealed class AutomationQueueItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BatchId { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Guid SuiteId { get; set; }
    public Guid? EnvironmentId { get; set; }
    public string ProjectName { get; set; } = "";
    public string SuiteName { get; set; } = "";
    public string EnvironmentName { get; set; } = "Default";
    public string Browser { get; set; } = "Chrome";
    public string BrowserVersion { get; set; } = "";
    public string BrowserExecutablePath { get; set; } = "";
    public AutomationQueueStatus Status { get; set; } = AutomationQueueStatus.Queued;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public int ExitCode { get; set; }
    public string Message { get; set; } = "";
    public Guid? SuiteRunId { get; set; }
    public string HtmlReportPath { get; set; } = "";
    public string MatrixReportPath { get; set; } = "";
    [JsonIgnore] public string BatchDisplay => BatchId.ToString("N")[..8].ToUpperInvariant();
    [JsonIgnore] public string CreatedDisplay => CreatedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
    [JsonIgnore] public string DurationDisplay
    {
        get
        {
            if (!StartedAt.HasValue) return "—";
            var d=(FinishedAt ?? DateTimeOffset.Now)-StartedAt.Value;
            return d.TotalHours >= 1 ? d.ToString(@"hh\:mm\:ss") : d.ToString(@"mm\:ss");
        }
    }
}

public sealed class AutomationQueueSettings
{
    public int MaxConcurrency { get; set; } = 2;
    public bool StopBatchOnFailure { get; set; }
    public bool BrowserHeadless { get; set; }
}
