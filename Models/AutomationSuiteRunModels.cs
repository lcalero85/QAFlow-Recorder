using System.Text.Json.Serialization;

namespace QAFlowRecorder.Models;

public enum AutomationSuiteRunStatus { Queued, Running, Passed, Failed, Aborted, NeedsReview }

public sealed class AutomationSuiteCaseResult
{
    public Guid TestCaseId { get; set; }
    public Guid TestRunId { get; set; }
    public string TestCaseKey { get; set; } = "";
    public string TestCaseName { get; set; } = "";
    public AutomationRunStatus Status { get; set; }
    public int PassRate { get; set; }
    public string Duration { get; set; } = "";
    public string HtmlReportPath { get; set; } = "";
    public string FailureMessage { get; set; } = "";
    public string DataSetName { get; set; } = "";
    public string LifecycleStage { get; set; } = "";
    [JsonIgnore] public string DisplayName => $"{TestCaseKey} · {TestCaseName}";
}

public sealed class AutomationSuiteRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Guid SuiteId { get; set; }
    public string ProjectName { get; set; } = "";
    public string SuiteName { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string Browser { get; set; } = "Chrome";
    public string DataSetName { get; set; } = "";
    public string EnvironmentName { get; set; } = "";
    public bool IsScheduled { get; set; }
    public AutomationSuiteRunStatus Status { get; set; } = AutomationSuiteRunStatus.Queued;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string RunFolder { get; set; } = "";
    public string HtmlReportPath { get; set; } = "";
    public string FailureMessage { get; set; } = "";
    public List<AutomationSuiteCaseResult> Cases { get; set; } = [];

    [JsonIgnore] public string DisplayName => $"{SuiteName} · {Cases.Count} case(s)";
    [JsonIgnore] public string StartedDisplay => StartedAt == default ? "—" : StartedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
    [JsonIgnore] public string DurationDisplay
    {
        get
        {
            if (StartedAt == default) return "00:00";
            var d = (FinishedAt ?? DateTimeOffset.Now) - StartedAt;
            return d.TotalHours >= 1 ? d.ToString(@"hh\:mm\:ss") : d.ToString(@"mm\:ss");
        }
    }
    [JsonIgnore] public int PassedCases => Cases.Count(c => string.IsNullOrWhiteSpace(c.LifecycleStage) && c.Status == AutomationRunStatus.Passed);
    [JsonIgnore] public int FailedCases => Cases.Count(c => string.IsNullOrWhiteSpace(c.LifecycleStage) && c.Status == AutomationRunStatus.Failed);
    [JsonIgnore] public int ReviewCases => Cases.Count(c => string.IsNullOrWhiteSpace(c.LifecycleStage) && c.Status is AutomationRunStatus.NeedsReview or AutomationRunStatus.Blocked);
    [JsonIgnore] public int ExecutedCases => Cases.Count(c => string.IsNullOrWhiteSpace(c.LifecycleStage));
    [JsonIgnore] public int PassRate => ExecutedCases == 0 ? 0 : (int)Math.Round(PassedCases * 100.0 / ExecutedCases);
}
