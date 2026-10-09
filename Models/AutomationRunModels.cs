using System.Text.Json.Serialization;

namespace QAFlowRecorder.Models;

public enum AutomationRunStatus { Queued, Running, Paused, Passed, Failed, Blocked, Aborted, NeedsReview }
public enum AutomationStepRunStatus { Pending, Running, Passed, Failed, ManualPassed, ManualFailed, Skipped }
public enum AutomationAssertionRunStatus { Passed, Failed }

public sealed class AutomationAssertionRun
{
    public AutomationAssertionType Type { get; set; }
    public string Expected { get; set; } = "";
    public AutomationAssertionRunStatus Status { get; set; }
    public string Message { get; set; } = "";
}

public sealed class AutomationStepRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TestStepId { get; set; }
    public int Order { get; set; }
    public string Action { get; set; } = "";
    public string Target { get; set; } = "";
    public string Selector { get; set; } = "";
    public AutomationStepRunStatus Status { get; set; } = AutomationStepRunStatus.Pending;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string Message { get; set; } = "";
    public string ScreenshotPath { get; set; } = "";
    public string Url { get; set; } = "";
    public List<AutomationAssertionRun> Assertions { get; set; } = [];
    public int RetryCount { get; set; }
    public bool SelectorHealed { get; set; }
    public string HealingMessage { get; set; } = "";
    public int HealingScore { get; set; }
    public string HealingStrategy { get; set; } = "";
    public string LifecycleStage { get; set; } = "Test";
    [JsonIgnore] public string StatusDisplay => Status.ToString();
    [JsonIgnore] public string AssertionSummary => Assertions.Count == 0 ? "—" : $"{Assertions.Count(a => a.Status == AutomationAssertionRunStatus.Passed)}/{Assertions.Count}";
    [JsonIgnore] public string RecoverySummary => SelectorHealed ? $"Healed · {RetryCount} retry" : RetryCount > 0 ? $"{RetryCount} retry" : "—";
}

public sealed class AutomationTestRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Guid SuiteId { get; set; }
    public Guid TestCaseId { get; set; }
    public string ProjectName { get; set; } = "";
    public string SuiteName { get; set; } = "";
    public string TestCaseKey { get; set; } = "";
    public string TestCaseName { get; set; } = "";
    public int TestCaseVersion { get; set; }
    public string BaseUrl { get; set; } = "";
    public string Browser { get; set; } = "Chrome";
    public string DataSetName { get; set; } = "";
    public AutomationRunStatus Status { get; set; } = AutomationRunStatus.Queued;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string RunFolder { get; set; } = "";
    public string HtmlReportPath { get; set; } = "";
    public string FailureMessage { get; set; } = "";
    public List<AutomationStepRun> Steps { get; set; } = [];

    [JsonIgnore] public string DisplayName => $"{TestCaseKey} · {TestCaseName}";
    [JsonIgnore] public string StartedDisplay => StartedAt == default ? "—" : StartedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
    [JsonIgnore] public string DurationDisplay
    {
        get
        {
            if (StartedAt == default) return "00:00";
            var end = FinishedAt ?? DateTimeOffset.Now;
            var d = end - StartedAt;
            return d.TotalHours >= 1 ? d.ToString(@"hh\:mm\:ss") : d.ToString(@"mm\:ss");
        }
    }
    [JsonIgnore] public int PassedCount => Steps.Count(s => s.Status is AutomationStepRunStatus.Passed or AutomationStepRunStatus.ManualPassed);
    [JsonIgnore] public int FailedCount => Steps.Count(s => s.Status is AutomationStepRunStatus.Failed or AutomationStepRunStatus.ManualFailed);
    [JsonIgnore] public int PassRate => Steps.Count == 0 ? 0 : (int)Math.Round(PassedCount * 100.0 / Steps.Count);
}
