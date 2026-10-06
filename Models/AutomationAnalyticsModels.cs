using System.Text.Json.Serialization;

namespace QAFlowRecorder.Models;

public sealed class AutomationTrendPoint
{
    public Guid SuiteRunId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid SuiteId { get; set; }
    public string SuiteName { get; set; } = "";
    public string Browser { get; set; } = "";
    public string EnvironmentName { get; set; } = "";
    public AutomationSuiteRunStatus Status { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public int PassRate { get; set; }
    public int PassedCases { get; set; }
    public int FailedCases { get; set; }
    public int ReviewCases { get; set; }
    public string Duration { get; set; } = "";
    [JsonIgnore] public string StartedDisplay => StartedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
    [JsonIgnore] public string ResultSummary => $"{PassedCases} passed · {FailedCases} failed · {ReviewCases} review";
}

public sealed class AutomationFlakyTestSummary
{
    public Guid ProjectId { get; set; }
    public Guid SuiteId { get; set; }
    public Guid TestCaseId { get; set; }
    public string TestCaseKey { get; set; } = "";
    public string TestCaseName { get; set; } = "";
    public string Browser { get; set; } = "";
    public int Runs { get; set; }
    public int Passed { get; set; }
    public int Failed { get; set; }
    public int NeedsReview { get; set; }
    public int Transitions { get; set; }
    public int FailureRate { get; set; }
    public int StabilityScore { get; set; }
    public string Classification { get; set; } = "";
    public AutomationRunStatus LastStatus { get; set; }
    [JsonIgnore] public string DisplayName => string.IsNullOrWhiteSpace(TestCaseKey) ? TestCaseName : $"{TestCaseKey} · {TestCaseName}";
    [JsonIgnore] public string RunSummary => $"{Passed} pass · {Failed} fail · {NeedsReview} review";
}

public sealed class AutomationFailureCluster
{
    public string Signature { get; set; } = "";
    public int Count { get; set; }
    public int TestCaseCount { get; set; }
    public string TestCases { get; set; } = "";
    public string Browsers { get; set; } = "";
    public DateTimeOffset LastSeen { get; set; }
    public string SampleMessage { get; set; } = "";
    [JsonIgnore] public string LastSeenDisplay => LastSeen.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
}

public sealed class AutomationQualitySnapshot
{
    public Guid? ProjectId { get; set; }
    public string ProjectName { get; set; } = "";
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.Now;
    public int SuiteRunsAnalyzed { get; set; }
    public int TestRunsAnalyzed { get; set; }
    public int OverallPassRate { get; set; }
    public int FlakyTests { get; set; }
    public int RecurringFailureClusters { get; set; }
    public int StableTests { get; set; }
    public List<AutomationTrendPoint> Trends { get; set; } = [];
    public List<AutomationFlakyTestSummary> TestStability { get; set; } = [];
    public List<AutomationFailureCluster> FailureClusters { get; set; } = [];
}
