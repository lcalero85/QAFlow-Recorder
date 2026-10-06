using System.Text.Json.Serialization;

namespace QAFlowRecorder.Models;

public enum AutomationApprovalState { Draft, InReview, Approved, Rejected }

public sealed class AutomationProjectGovernanceSettings
{
    public bool RequireApprovalForAutomatedRuns { get; set; }
}

public sealed class AutomationTestCaseVersionRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TestCaseId { get; set; }
    public int Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public string CreatedBy { get; set; } = Environment.UserName;
    public string Note { get; set; } = "";
    public string SnapshotJson { get; set; } = "";
    public string Sha256 { get; set; } = "";
    [JsonIgnore] public string Display => $"v{Version} · {CreatedAt:yyyy-MM-dd HH:mm} · {CreatedBy}";
}

public sealed class AutomationTestCaseGovernanceRecord
{
    public Guid TestCaseId { get; set; }
    public AutomationApprovalState ApprovalState { get; set; } = AutomationApprovalState.Draft;
    public int ApprovedVersion { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public string SubmittedBy { get; set; } = "";
    public DateTimeOffset? ApprovedAt { get; set; }
    public string ApprovedBy { get; set; } = "";
    public DateTimeOffset? RejectedAt { get; set; }
    public string RejectedBy { get; set; } = "";
    public string ReviewNote { get; set; } = "";
    public List<AutomationTestCaseVersionRecord> Versions { get; set; } = [];
    [JsonIgnore] public string ApprovalDisplay => ApprovalState.ToString();
}

public sealed class AutomationAuditEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public string User { get; set; } = Environment.UserName;
    public string Action { get; set; } = "";
    public string EntityType { get; set; } = "";
    public Guid? EntityId { get; set; }
    public string EntityName { get; set; } = "";
    public string Details { get; set; } = "";
    [JsonIgnore] public string TimestampDisplay => Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
}

public sealed class AutomationGovernanceDocument
{
    public Guid ProjectId { get; set; }
    public AutomationProjectGovernanceSettings Settings { get; set; } = new();
    public List<AutomationTestCaseGovernanceRecord> TestCases { get; set; } = [];
    public List<AutomationAuditEntry> AuditTrail { get; set; } = [];
}
