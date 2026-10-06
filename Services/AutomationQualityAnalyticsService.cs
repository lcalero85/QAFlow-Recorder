using System.Text.RegularExpressions;
using QAFlowRecorder.Models;

namespace QAFlowRecorder.Services;

public sealed partial class AutomationQualityAnalyticsService
{
    private readonly AutomationRunStore _runStore = new();
    private readonly AutomationSuiteRunStore _suiteRunStore = new();

    public async Task<AutomationQualitySnapshot> AnalyzeAsync(AutomationProject? project)
    {
        var allRuns = await _runStore.LoadAllAsync();
        var allSuiteRuns = await _suiteRunStore.LoadAllAsync();
        if (project is not null)
        {
            allRuns = allRuns.Where(r => r.ProjectId == project.Id).ToList();
            allSuiteRuns = allSuiteRuns.Where(r => r.ProjectId == project.Id).ToList();
        }

        var snapshot = new AutomationQualitySnapshot
        {
            ProjectId = project?.Id,
            ProjectName = project?.Name ?? "All projects",
            SuiteRunsAnalyzed = allSuiteRuns.Count,
            TestRunsAnalyzed = allRuns.Count
        };

        snapshot.Trends = allSuiteRuns
            .OrderByDescending(r => r.StartedAt)
            .Take(40)
            .Select(r => new AutomationTrendPoint
            {
                SuiteRunId = r.Id,
                ProjectId = r.ProjectId,
                SuiteId = r.SuiteId,
                SuiteName = r.SuiteName,
                Browser = r.Browser,
                EnvironmentName = r.EnvironmentName,
                Status = r.Status,
                StartedAt = r.StartedAt,
                PassRate = r.PassRate,
                PassedCases = r.PassedCases,
                FailedCases = r.FailedCases,
                ReviewCases = r.ReviewCases,
                Duration = r.DurationDisplay
            }).ToList();

        if (allSuiteRuns.Count > 0)
            snapshot.OverallPassRate = (int)Math.Round(allSuiteRuns.Average(r => r.PassRate));

        var stability = new List<AutomationFlakyTestSummary>();
        foreach (var group in allRuns.GroupBy(r => new { r.ProjectId, r.SuiteId, r.TestCaseId, Browser = r.Browser.ToUpperInvariant() }))
        {
            var ordered = group.OrderBy(r => r.StartedAt).ToList();
            if (ordered.Count == 0) continue;
            var passed = ordered.Count(r => r.Status == AutomationRunStatus.Passed);
            var failed = ordered.Count(r => r.Status == AutomationRunStatus.Failed);
            var review = ordered.Count(r => r.Status is AutomationRunStatus.NeedsReview or AutomationRunStatus.Blocked);
            var decisive = passed + failed + review;
            var transitions = 0;
            for (var i = 1; i < ordered.Count; i++)
                if (NormalizeStatus(ordered[i - 1].Status) != NormalizeStatus(ordered[i].Status)) transitions++;

            var failureRate = decisive == 0 ? 0 : (int)Math.Round((failed + review) * 100.0 / decisive);
            var transitionPenalty = ordered.Count <= 1 ? 0 : (int)Math.Round(transitions * 100.0 / (ordered.Count - 1));
            var stabilityScore = Math.Clamp(100 - (int)Math.Round(failureRate * 0.55 + transitionPenalty * 0.45), 0, 100);
            var classification = Classify(ordered.Count, passed, failed, review, transitions, failureRate);
            var last = ordered[^1];
            stability.Add(new AutomationFlakyTestSummary
            {
                ProjectId = group.Key.ProjectId,
                SuiteId = group.Key.SuiteId,
                TestCaseId = group.Key.TestCaseId,
                TestCaseKey = last.TestCaseKey,
                TestCaseName = last.TestCaseName,
                Browser = last.Browser,
                Runs = ordered.Count,
                Passed = passed,
                Failed = failed,
                NeedsReview = review,
                Transitions = transitions,
                FailureRate = failureRate,
                StabilityScore = stabilityScore,
                Classification = classification,
                LastStatus = last.Status
            });
        }
        snapshot.TestStability = stability
            .OrderBy(s => StabilityRank(s.Classification))
            .ThenBy(s => s.StabilityScore)
            .ThenByDescending(s => s.Runs)
            .ToList();
        snapshot.FlakyTests = stability.Count(s => s.Classification == "Flaky");
        snapshot.StableTests = stability.Count(s => s.Classification == "Stable");

        var failedRuns = allRuns.Where(r => r.Status is AutomationRunStatus.Failed or AutomationRunStatus.NeedsReview or AutomationRunStatus.Blocked).ToList();
        var clusterRows = failedRuns.Select(r => new
        {
            Run = r,
            Message = BestFailureMessage(r),
            Signature = NormalizeFailure(BestFailureMessage(r))
        }).Where(x => !string.IsNullOrWhiteSpace(x.Signature));

        snapshot.FailureClusters = clusterRows
            .GroupBy(x => x.Signature)
            .Select(g => new AutomationFailureCluster
            {
                Signature = g.Key,
                Count = g.Count(),
                TestCaseCount = g.Select(x => x.Run.TestCaseId).Distinct().Count(),
                TestCases = string.Join(", ", g.Select(x => x.Run.DisplayName).Distinct().Take(5)),
                Browsers = string.Join(", ", g.Select(x => x.Run.Browser).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct()),
                LastSeen = g.Max(x => x.Run.StartedAt),
                SampleMessage = g.Select(x => x.Message).FirstOrDefault(m => !string.IsNullOrWhiteSpace(m)) ?? g.Key
            })
            .OrderByDescending(x => x.Count)
            .ThenByDescending(x => x.LastSeen)
            .Take(30)
            .ToList();
        snapshot.RecurringFailureClusters = snapshot.FailureClusters.Count(c => c.Count >= 2);
        return snapshot;
    }

    private static string NormalizeStatus(AutomationRunStatus status) => status switch
    {
        AutomationRunStatus.Passed => "pass",
        AutomationRunStatus.Failed => "fail",
        AutomationRunStatus.NeedsReview or AutomationRunStatus.Blocked => "review",
        _ => status.ToString().ToLowerInvariant()
    };

    private static string Classify(int runs, int passed, int failed, int review, int transitions, int failureRate)
    {
        if (runs >= 3 && passed > 0 && (failed + review) > 0 && (transitions > 0 || (failureRate >= 10 && failureRate <= 90))) return "Flaky";
        if (runs >= 2 && passed == runs) return "Stable";
        if (runs >= 2 && passed == 0 && (failed + review) == runs) return "Consistently failing";
        if (runs < 3) return "Insufficient history";
        return failureRate == 0 ? "Stable" : "Unstable";
    }

    private static int StabilityRank(string classification) => classification switch
    {
        "Flaky" => 0,
        "Consistently failing" => 1,
        "Unstable" => 2,
        "Insufficient history" => 3,
        _ => 4
    };

    private static string BestFailureMessage(AutomationTestRun run)
    {
        if (!string.IsNullOrWhiteSpace(run.FailureMessage)) return run.FailureMessage.Trim();
        var failedStep = run.Steps.FirstOrDefault(s => s.Status is AutomationStepRunStatus.Failed or AutomationStepRunStatus.ManualFailed);
        if (failedStep is not null && !string.IsNullOrWhiteSpace(failedStep.Message)) return failedStep.Message.Trim();
        var failedAssertion = run.Steps.SelectMany(s => s.Assertions).FirstOrDefault(a => a.Status == AutomationAssertionRunStatus.Failed);
        return failedAssertion?.Message?.Trim() ?? "Execution failed without a diagnostic message.";
    }

    private static string NormalizeFailure(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "";
        var s = message.Trim().ToLowerInvariant();
        s = GuidRegex().Replace(s, "{guid}");
        s = UrlRegex().Replace(s, "{url}");
        s = NumberRegex().Replace(s, "{n}");
        s = SpaceRegex().Replace(s, " ");
        return s.Length > 180 ? s[..180] : s;
    }

    [GeneratedRegex(@"\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b", RegexOptions.IgnoreCase)] private static partial Regex GuidRegex();
    [GeneratedRegex(@"https?://\S+", RegexOptions.IgnoreCase)] private static partial Regex UrlRegex();
    [GeneratedRegex(@"\b\d+\b")] private static partial Regex NumberRegex();
    [GeneratedRegex(@"\s+")] private static partial Regex SpaceRegex();
}
