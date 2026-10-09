using System.Text.Json;
using QAFlowRecorder.Models;

namespace QAFlowRecorder.Services;

public sealed class SessionTemplateService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QAFlow Recorder 1.0", "session-templates.json");

    public async Task<List<SessionTemplateDefinition>> LoadAsync()
    {
        var builtIns = BuiltIns();
        try
        {
            if (!File.Exists(_file)) return builtIns;
            var custom = JsonSerializer.Deserialize<List<SessionTemplateDefinition>>(await File.ReadAllTextAsync(_file)) ?? [];
            foreach (var x in custom) x.BuiltIn = false;
            return [.. builtIns, .. custom.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)];
        }
        catch { return builtIns; }
    }

    public async Task SaveCustomAsync(IEnumerable<SessionTemplateDefinition> templates)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        var custom = templates.Where(x => !x.BuiltIn).ToList();
        await File.WriteAllTextAsync(_file, JsonSerializer.Serialize(custom, JsonOptions));
    }

    private static List<SessionTemplateDefinition> BuiltIns() =>
    [
        new() { Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), Name = "Functional", BuiltIn = true, TestType = "Functional Normal", DefaultTags = "functional", PrivacyMasking = PrivacyMaskMode.Automatic },
        new() { Id = Guid.Parse("22222222-2222-2222-2222-222222222222"), Name = "Smoke", BuiltIn = true, TestType = "Smoke", DefaultTags = "smoke", ChecklistText = "Core screen loads\nPrimary action works\nNo blocking error is visible", PrivacyMasking = PrivacyMaskMode.Automatic },
        new() { Id = Guid.Parse("33333333-3333-3333-3333-333333333333"), Name = "Regression", BuiltIn = true, TestType = "Regression", DefaultTags = "regression", ChecklistText = "Primary flow completes\nPreviously fixed behavior remains stable\nNo unexpected visual regression", PrivacyMasking = PrivacyMaskMode.Automatic },
        new() { Id = Guid.Parse("44444444-4444-4444-4444-444444444444"), Name = "UAT", BuiltIn = true, TestType = "UAT", DefaultTags = "uat", ChecklistText = "Business objective can be completed\nUser-facing result is correct\nNo blocking usability issue", PrivacyMasking = PrivacyMaskMode.Automatic },
        new() { Id = Guid.Parse("55555555-5555-5555-5555-555555555555"), Name = "Exploratory", BuiltIn = true, TestType = "Exploratory", DefaultTags = "exploratory", CharterObjective = "Explore the selected area and document meaningful behavior, risks and findings.", CharterRisks = "Unexpected states, confusing behavior, broken transitions and data integrity issues.", PrivacyMasking = PrivacyMaskMode.Automatic }
    ];
}

public sealed class SessionComparisonSummary
{
    public required TestSession Current { get; init; }
    public required TestSession Previous { get; init; }
    public TimeSpan DurationDelta => Current.Duration - Previous.Duration;
    public double DurationDeltaPercent => Previous.Duration.TotalSeconds <= 0 ? 0 : Math.Round(DurationDelta.TotalSeconds / Previous.Duration.TotalSeconds * 100, 1);
    public int InteractionDelta => Current.ActionCount - Previous.ActionCount;
    public int BugDelta => Current.Bugs.Count - Previous.Bugs.Count;
    public int CheckpointDelta => Current.Events.Count(e => e.IsCheckpoint) - Previous.Events.Count(e => e.IsCheckpoint);
    public int NewStepCount { get; init; }
    public int MissingStepCount { get; init; }
    public int VisualChangeCount { get; init; }
    public List<string> NewSteps { get; init; } = [];
    public List<string> MissingSteps { get; init; } = [];
}

public static class SessionComparisonService
{
    public static SessionComparisonSummary Compare(TestSession current, TestSession previous)
    {
        static List<string> Steps(TestSession s) => Deduplicator.Process(s.Events)
            .Where(ReportLanguage.IsAction)
            .Select(e => Normalize(SmartNarrativeService.Describe(e, false)))
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var a = Steps(current);
        var b = Steps(previous);
        var newer = a.Except(b, StringComparer.OrdinalIgnoreCase).Take(10).ToList();
        var missing = b.Except(a, StringComparer.OrdinalIgnoreCase).Take(10).ToList();
        var visualChanges = CountVisualChanges(current, previous);
        return new SessionComparisonSummary
        {
            Current = current, Previous = previous,
            NewStepCount = a.Except(b, StringComparer.OrdinalIgnoreCase).Count(),
            MissingStepCount = b.Except(a, StringComparer.OrdinalIgnoreCase).Count(),
            VisualChangeCount = visualChanges,
            NewSteps = newer, MissingSteps = missing
        };
    }

    private static int CountVisualChanges(TestSession current, TestSession previous)
    {
        static Dictionary<string,string> LatestByTarget(TestSession session)
        {
            var result = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            foreach (var ev in session.Events.Where(ReportLanguage.IsAction).OrderBy(x => x.Timestamp))
            {
                var target = ReportLanguage.Target(ev);
                var shot = ReportLanguage.EvidenceFor(session, ev);
                if (!string.IsNullOrWhiteSpace(target) && shot is not null && File.Exists(shot.FilePath)) result[target] = shot.FilePath;
            }
            return result;
        }
        var a = LatestByTarget(current); var b = LatestByTarget(previous); var changes = 0;
        foreach (var target in a.Keys.Intersect(b.Keys, StringComparer.OrdinalIgnoreCase))
        {
            try { if (VisualBaselineService.CompareEvidenceFiles(a[target], b[target]) >= 12) changes++; } catch { }
        }
        return changes;
    }

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var compact = System.Text.RegularExpressions.Regex.Replace(value.ToLowerInvariant(), @"\s+", " ").Trim();
        compact = System.Text.RegularExpressions.Regex.Replace(compact, @"\b\d+\b", "#");
        return compact;
    }
}
