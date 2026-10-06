using QAFlowRecorder.Models;

namespace QAFlowRecorder.Services;

public sealed record CoverageItem(string Name, bool Observed, int Interactions, TimeSpan ApproximateTime, int EvidenceCount);
public sealed record CoverageResult(IReadOnlyList<CoverageItem> Items, int ObservedCount, int ExpectedCount, double? CoveragePercent, string MostActiveTarget);

public static class CoverageAnalysisService
{
    public static CoverageResult Analyze(TestSession session, string? expectedTargets)
    {
        var events = Deduplicator.Process(session.Events)
            .Where(ReportLanguage.IsAction)
            .Where(e => !e.ExcludeFromReport)
            .OrderBy(e => e.Timestamp)
            .ToList();

        static string Target(QaEvent e)
        {
            if (!string.IsNullOrWhiteSpace(e.TargetTitle)) return e.TargetTitle.Trim();
            if (!string.IsNullOrWhiteSpace(e.WindowTitle)) return e.WindowTitle.Trim();
            if (!string.IsNullOrWhiteSpace(e.ApplicationName)) return e.ApplicationName.Trim();
            return "Desktop";
        }

        var observed = events.GroupBy(Target, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var time = new Dictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < events.Count; i++)
        {
            var name = Target(events[i]);
            var next = i + 1 < events.Count ? events[i + 1].Timestamp : (session.FinishedAt ?? events[i].Timestamp);
            var delta = next - events[i].Timestamp;
            if (delta < TimeSpan.Zero) delta = TimeSpan.Zero;
            if (delta > TimeSpan.FromMinutes(5)) delta = TimeSpan.FromMinutes(5);
            time[name] = time.TryGetValue(name, out var current) ? current + delta : delta;
        }

        var expected = (expectedTargets ?? "")
            .Split([',',';','\n','\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var names = expected.Count > 0
            ? expected.Concat(observed.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            : observed.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();

        var items = names.Select(name =>
        {
            observed.TryGetValue(name, out var list);
            var evidence = session.Screenshots.Count(s => string.Equals(s.TargetTitle, name, StringComparison.OrdinalIgnoreCase));
            return new CoverageItem(name, list is not null && list.Count > 0, list?.Count ?? 0, time.GetValueOrDefault(name), evidence);
        }).ToList();

        var expectedObserved = expected.Count == 0 ? observed.Count : expected.Count(x => observed.ContainsKey(x));
        var percent = expected.Count == 0 ? (double?)null : Math.Round(expectedObserved * 100d / expected.Count, 1);
        var mostActive = observed.OrderByDescending(x => x.Value.Count).Select(x => x.Key).FirstOrDefault() ?? "—";
        return new CoverageResult(items, observed.Count, expected.Count, percent, mostActive);
    }
}
