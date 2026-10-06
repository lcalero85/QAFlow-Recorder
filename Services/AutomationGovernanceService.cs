using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using QAFlowRecorder.Models;

namespace QAFlowRecorder.Services;

public sealed class AutomationGovernanceService
{
    private readonly string _root;
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public AutomationGovernanceService()
    {
        _root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QAFlow Recorder", "automation-governance");
        Directory.CreateDirectory(_root);
    }

    private string PathFor(Guid projectId) => Path.Combine(_root, $"{projectId:N}.json");

    public async Task<AutomationGovernanceDocument> LoadAsync(Guid projectId)
    {
        var path = PathFor(projectId);
        if (!File.Exists(path)) return new AutomationGovernanceDocument { ProjectId = projectId };
        try
        {
            var text = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<AutomationGovernanceDocument>(text, _json) ?? new AutomationGovernanceDocument { ProjectId = projectId };
        }
        catch { return new AutomationGovernanceDocument { ProjectId = projectId }; }
    }

    public async Task SaveAsync(AutomationGovernanceDocument doc)
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(PathFor(doc.ProjectId), JsonSerializer.Serialize(doc, _json));
    }

    public static string ComputeHash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes);
    }

    public async Task<AutomationTestCaseVersionRecord?> CreateVersionAsync(Guid projectId, AutomationTestCase testCase, string user, string note)
    {
        var doc = await LoadAsync(projectId);
        var record = GetOrCreate(doc, testCase.Id);
        var snapshot = JsonSerializer.Serialize(testCase, _json);
        var hash = ComputeHash(snapshot);
        var last = record.Versions.OrderByDescending(v => v.Version).FirstOrDefault();
        if (last is not null && last.Sha256.Equals(hash, StringComparison.OrdinalIgnoreCase)) return null;
        var next = Math.Max(testCase.Version, (last?.Version ?? 0) + 1);
        if (last is not null && next <= last.Version) next = last.Version + 1;
        testCase.Version = next;
        snapshot = JsonSerializer.Serialize(testCase, _json);
        hash = ComputeHash(snapshot);
        var version = new AutomationTestCaseVersionRecord
        {
            TestCaseId = testCase.Id, Version = testCase.Version, CreatedBy = user,
            CreatedAt = DateTimeOffset.Now, Note = note ?? "", SnapshotJson = snapshot, Sha256 = hash
        };
        record.Versions.Add(version);
        record.ApprovalState = AutomationApprovalState.Draft;
        record.ApprovedVersion = 0;
        AddAudit(doc, user, "VersionCreated", "TestCase", testCase.Id, testCase.DisplayName, $"Created v{version.Version}. {note}".Trim());
        await SaveAsync(doc);
        return version;
    }

    public async Task<AutomationTestCaseGovernanceRecord> SetApprovalAsync(Guid projectId, AutomationTestCase testCase, AutomationApprovalState state, string user, string note)
    {
        var doc = await LoadAsync(projectId);
        var record = GetOrCreate(doc, testCase.Id);
        record.ApprovalState = state;
        record.ReviewNote = note ?? "";
        var now = DateTimeOffset.Now;
        if (state == AutomationApprovalState.InReview) { record.SubmittedAt = now; record.SubmittedBy = user; }
        if (state == AutomationApprovalState.Approved) { record.ApprovedAt = now; record.ApprovedBy = user; record.ApprovedVersion = testCase.Version; }
        if (state == AutomationApprovalState.Rejected) { record.RejectedAt = now; record.RejectedBy = user; record.ApprovedVersion = 0; }
        AddAudit(doc, user, state.ToString(), "TestCase", testCase.Id, testCase.DisplayName, note ?? "");
        await SaveAsync(doc);
        return record;
    }

    public async Task LogAsync(Guid projectId, string user, string action, string entityType, Guid? entityId, string entityName, string details = "")
    {
        var doc = await LoadAsync(projectId);
        AddAudit(doc, user, action, entityType, entityId, entityName, details);
        await SaveAsync(doc);
    }

    public async Task<bool> IsExecutionAllowedAsync(Guid projectId, IEnumerable<AutomationTestCase> cases)
    {
        var doc = await LoadAsync(projectId);
        if (!doc.Settings.RequireApprovalForAutomatedRuns) return true;
        foreach (var tc in cases.Where(c => c.Status != AutomationCaseStatus.Archived && c.Mode != AutomationCaseMode.Manual))
        {
            var rec = doc.TestCases.FirstOrDefault(x => x.TestCaseId == tc.Id);
            if (rec is null || rec.ApprovalState != AutomationApprovalState.Approved || rec.ApprovedVersion != tc.Version) return false;
        }
        return true;
    }

    public async Task SetRequireApprovalAsync(Guid projectId, bool value, string user)
    {
        var doc = await LoadAsync(projectId);
        if (doc.Settings.RequireApprovalForAutomatedRuns == value) return;
        doc.Settings.RequireApprovalForAutomatedRuns = value;
        AddAudit(doc, user, "GovernanceSettingChanged", "Project", projectId, "Project", $"RequireApprovalForAutomatedRuns={value}");
        await SaveAsync(doc);
    }

    public async Task<AutomationTestCase?> BuildRestoredCopyAsync(AutomationTestCaseVersionRecord version)
    {
        await Task.CompletedTask;
        return JsonSerializer.Deserialize<AutomationTestCase>(version.SnapshotJson, _json);
    }

    private static AutomationTestCaseGovernanceRecord GetOrCreate(AutomationGovernanceDocument doc, Guid testCaseId)
    {
        var record = doc.TestCases.FirstOrDefault(x => x.TestCaseId == testCaseId);
        if (record is not null) return record;
        record = new AutomationTestCaseGovernanceRecord { TestCaseId = testCaseId };
        doc.TestCases.Add(record);
        return record;
    }

    private static void AddAudit(AutomationGovernanceDocument doc, string user, string action, string type, Guid? id, string name, string details)
    {
        doc.AuditTrail.Add(new AutomationAuditEntry { User = string.IsNullOrWhiteSpace(user) ? Environment.UserName : user, Action = action, EntityType = type, EntityId = id, EntityName = name, Details = details });
        if (doc.AuditTrail.Count > 5000) doc.AuditTrail = doc.AuditTrail.OrderByDescending(x => x.Timestamp).Take(5000).OrderBy(x => x.Timestamp).ToList();
    }
}
