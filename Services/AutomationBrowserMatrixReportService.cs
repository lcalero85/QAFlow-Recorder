using System.Net;
using System.Text;
using QAFlowRecorder.Models;

namespace QAFlowRecorder.Services;

public static class AutomationBrowserMatrixReportService
{
    private sealed record CaseSnapshot(
        Guid TestRunId,
        string Key,
        string Name,
        AutomationRunStatus Status,
        int PassRate,
        string Duration,
        string FailureMessage,
        string ReportPath);

    private sealed record BrowserSnapshot(
        AutomationQueueItem Queue,
        AutomationSuiteRun? Run,
        Dictionary<string, CaseSnapshot> Cases);

    public static async Task<string> GenerateAsync(IEnumerable<AutomationQueueItem> source)
    {
        var items = source.OrderBy(x => BrowserOrder(x.Browser)).ThenBy(x => x.Browser).ToList();
        if (items.Count == 0) return "";

        static string H(string? value) => WebUtility.HtmlEncode(value ?? "");
        var first = items[0];
        var passed = items.Count(x => x.Status == AutomationQueueStatus.Passed);
        var failed = items.Count(x => x.Status == AutomationQueueStatus.Failed);
        var review = items.Count(x => x.Status == AutomationQueueStatus.NeedsReview);

        var suiteStore = new AutomationSuiteRunStore();
        var allRuns = await suiteStore.LoadAllAsync();
        var testRunStore = new AutomationRunStore();
        var allTestRuns = await testRunStore.LoadAllAsync();
        var snapshots = items.Select(item =>
        {
            var run = item.SuiteRunId.HasValue
                ? allRuns.FirstOrDefault(r => r.Id == item.SuiteRunId.Value)
                : null;
            run ??= allRuns
                .Where(r => r.ProjectId == item.ProjectId && r.SuiteId == item.SuiteId && r.Browser.Equals(item.Browser, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(r => r.StartedAt)
                .FirstOrDefault(r => !item.StartedAt.HasValue || r.StartedAt >= item.StartedAt.Value.AddSeconds(-5));

            var cases = (run?.Cases ?? [])
                .Where(c => string.IsNullOrWhiteSpace(c.LifecycleStage))
                .GroupBy(c => CaseIdentity(c), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g =>
                {
                    var c = g.First();
                    return new CaseSnapshot(c.TestRunId, c.TestCaseKey, c.TestCaseName, c.Status, c.PassRate, c.Duration, c.FailureMessage, c.HtmlReportPath);
                }, StringComparer.OrdinalIgnoreCase);
            return new BrowserSnapshot(item, run, cases);
        }).ToList();

        var rows = new StringBuilder();
        foreach (var x in snapshots)
        {
            var item = x.Queue;
            var cls = item.Status switch { AutomationQueueStatus.Passed => "pass", AutomationQueueStatus.Failed => "fail", AutomationQueueStatus.NeedsReview => "review", _ => "neutral" };
            var report = File.Exists(item.HtmlReportPath) ? $"<a href='{new Uri(item.HtmlReportPath).AbsoluteUri}'>Open suite report</a>" : "—";
            var caseSummary = x.Run is null ? "—" : $"{x.Run.PassedCases}/{x.Run.ExecutedCases} passed · {x.Run.PassRate}%";
            rows.Append($"<tr><td><strong>{H(item.Browser)}</strong></td><td>{H(string.IsNullOrWhiteSpace(item.BrowserVersion) ? "—" : item.BrowserVersion)}</td><td>{H(item.EnvironmentName)}</td><td><span class='pill {cls}'>{H(item.Status.ToString())}</span></td><td>{H(item.DurationDisplay)}</td><td>{H(caseSummary)}</td><td>{H(item.Message)}</td><td>{report}</td></tr>");
        }

        var compatibility = BuildCompatibilityAnalysis(snapshots, H);
        var evidence = BuildEvidenceComparison(snapshots, allTestRuns, H);
        var completed = items.Count(x => x.Status is AutomationQueueStatus.Passed or AutomationQueueStatus.Failed or AutomationQueueStatus.NeedsReview);
        var comparedCoverage = compatibility.ComparedCases + compatibility.MissingCases == 0
            ? 0
            : (int)Math.Round(compatibility.ComparedCases * 100d / (compatibility.ComparedCases + compatibility.MissingCases));
        var phaseClosureClass = completed < items.Count ? "review" : failed > 0 || compatibility.IncompatibleCases > 0 || compatibility.MissingCases > 0 ? "fail" : review > 0 || compatibility.ReviewCases > 0 ? "review" : "pass";
        var phaseClosureText = completed < items.Count
            ? "Matrix incomplete — finish pending browser jobs before QA sign-off"
            : failed > 0 || compatibility.IncompatibleCases > 0 || compatibility.MissingCases > 0
                ? "Attention required — review cross-browser findings before QA sign-off"
                : review > 0 || compatibility.ReviewCases > 0
                    ? "Ready for QA review — manual review items remain"
                    : "Ready for QA sign-off — no cross-browser differences detected";
        var executiveActions = BuildExecutiveActions(items, compatibility, H);
        var verdictClass = compatibility.IncompatibleCases > 0 || compatibility.MissingCases > 0 ? "fail" : compatibility.ReviewCases > 0 ? "review" : "pass";
        var verdictText = compatibility.IncompatibleCases > 0 || compatibility.MissingCases > 0
            ? "Cross-browser differences detected"
            : compatibility.ReviewCases > 0
                ? "Consistent with review items"
                : "Consistent across compared browsers";

        var html = """
<!doctype html><html><head><meta charset="utf-8"><title>QAFlow Browser Matrix Report</title>
<style>
body{font-family:Segoe UI,Arial,sans-serif;background:#0b1119;color:#e9f2fb;margin:0;padding:32px}main{max-width:1240px;margin:auto}.hero,.card{background:#111b28;border:1px solid #294159;border-radius:14px;padding:20px;margin-bottom:16px}.brand{font-size:13px;color:#77c9ff;font-weight:700;letter-spacing:.1em}.grid{display:grid;grid-template-columns:repeat(4,1fr);gap:12px}.metric{background:#0d1621;border:1px solid #22384d;border-radius:10px;padding:14px}.metric b{font-size:28px;display:block;margin-top:5px}table{width:100%;border-collapse:collapse}th,td{text-align:left;padding:12px;border-bottom:1px solid #263a50;font-size:13px;vertical-align:top}th{color:#8fa8c0}.pill{padding:4px 8px;border-radius:999px;font-weight:700;font-size:11px;display:inline-block}.pass{background:#143728;color:#5ce0a3}.fail{background:#3e2028;color:#ff8997}.review{background:#3b321e;color:#f4c66b}.neutral{background:#243142;color:#b9cbe0}a{color:#77c9ff}small,.muted{color:#8fa8c0}.verdict{display:flex;align-items:center;gap:10px;margin-top:12px}.compat-summary{display:grid;grid-template-columns:repeat(4,1fr);gap:10px;margin:14px 0 6px}.compat-summary div{background:#0d1621;border:1px solid #22384d;border-radius:10px;padding:12px}.compat-summary b{display:block;font-size:22px;margin-top:4px}.browser-result{white-space:nowrap}.detail{max-width:390px;word-break:break-word}.empty{padding:18px;border:1px dashed #2b455f;border-radius:10px;color:#8fa8c0;background:#0d1621}.evidence-case{border-top:1px solid #263a50;padding-top:16px;margin-top:16px}.evidence-case:first-of-type{border-top:0;margin-top:0}.evidence-case-title{display:flex;justify-content:space-between;gap:12px;align-items:center;margin-bottom:10px}.evidence-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(260px,1fr));gap:12px}.evidence-card{background:#0d1621;border:1px solid #22384d;border-radius:10px;padding:12px}.evidence-head{display:flex;justify-content:space-between;gap:8px;align-items:center;margin-bottom:10px}.evidence-img{width:100%;height:180px;object-fit:contain;background:#070b10;border:1px solid #263a50;border-radius:8px;display:block}.evidence-missing{height:180px;display:flex;align-items:center;justify-content:center;border:1px dashed #2b455f;border-radius:8px;color:#8fa8c0;background:#090f17}.evidence-meta{font-size:12px;margin-top:9px;margin-bottom:5px;word-break:break-word}.executive-grid{display:grid;grid-template-columns:repeat(4,1fr);gap:10px;margin:12px 0}.executive-grid div{background:#0d1621;border:1px solid #22384d;border-radius:10px;padding:12px}.executive-grid b{display:block;font-size:22px;margin-top:4px}.actions{margin:10px 0 0;padding-left:20px}.actions li{margin:6px 0}.phase-note{border-left:4px solid #77c9ff;padding:10px 12px;background:#0d1621;border-radius:8px;margin-top:12px}
</style></head><body><main>
<div class="hero"><div class="brand">QAFLOW RECORDER · WEB AUTOMATION</div><h1>Browser Matrix Execution Report</h1><p>__PROJECT__ · __SUITE__</p><small>Batch __BATCH__ · Generated __GENERATED__</small><div class="verdict"><span class="pill __VERDICT_CLASS__">__VERDICT__</span><span class="muted">Automatic differential analysis compares the same test cases across browsers; it does not change test execution.</span></div></div>
<div class="grid"><div class="metric">Browsers<b>__TOTAL__</b></div><div class="metric">Passed<b>__PASSED__</b></div><div class="metric">Failed<b>__FAILED__</b></div><div class="metric">Needs review<b>__REVIEW__</b></div></div>
<div class="card"><h2>Phase 16 executive summary</h2><div class="verdict"><span class="pill __PHASE_CLASS__">__PHASE_STATUS__</span><span class="muted">Consolidated decision aid only; final QA approval remains manual.</span></div>
<div class="executive-grid"><div>Browser jobs completed<b>__COMPLETED__/__TOTAL__</b></div><div>Comparable-case coverage<b>__COVERAGE__%</b></div><div>Cross-browser differences<b>__DIFF_TOTAL__</b></div><div>Review items<b>__REVIEW_TOTAL__</b></div></div>
<div class="phase-note"><strong>Recommended next actions</strong><ul class="actions">__EXEC_ACTIONS__</ul></div></div>
<div class="card"><h2>Cross-browser results</h2><table><thead><tr><th>Browser</th><th>Version</th><th>Environment</th><th>Result</th><th>Duration</th><th>Cases</th><th>Summary</th><th>Report</th></tr></thead><tbody>__ROWS__</tbody></table></div>
<div class="card"><h2>Compatibility differential analysis</h2>
<div class="compat-summary"><div>Compared cases<b>__COMPARED__</b></div><div>Consistent<b>__CONSISTENT__</b></div><div>Different / missing<b>__DIFFERENT__</b></div><div>Review-only<b>__CASE_REVIEW__</b></div></div>
__COMPATIBILITY__</div>
<div class="card"><h2>Comparative evidence</h2><p class="muted">Side-by-side execution evidence for cross-browser diagnosis. QAFlow does not alter or approve results automatically.</p>
__EVIDENCE__</div>
</main></body></html>
""";
        html = html.Replace("__PROJECT__", H(first.ProjectName)).Replace("__SUITE__", H(first.SuiteName)).Replace("__BATCH__", H(first.BatchDisplay))
            .Replace("__GENERATED__", DateTimeOffset.Now.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"))
            .Replace("__TOTAL__", items.Count.ToString()).Replace("__PASSED__", passed.ToString()).Replace("__FAILED__", failed.ToString()).Replace("__REVIEW__", review.ToString()).Replace("__ROWS__", rows.ToString())
            .Replace("__PHASE_CLASS__", phaseClosureClass).Replace("__PHASE_STATUS__", H(phaseClosureText)).Replace("__COMPLETED__", completed.ToString())
            .Replace("__COVERAGE__", comparedCoverage.ToString()).Replace("__DIFF_TOTAL__", (compatibility.IncompatibleCases + compatibility.MissingCases).ToString())
            .Replace("__REVIEW_TOTAL__", (review + compatibility.ReviewCases).ToString()).Replace("__EXEC_ACTIONS__", executiveActions)
            .Replace("__VERDICT_CLASS__", verdictClass).Replace("__VERDICT__", H(verdictText))
            .Replace("__COMPARED__", compatibility.ComparedCases.ToString()).Replace("__CONSISTENT__", compatibility.ConsistentCases.ToString())
            .Replace("__DIFFERENT__", (compatibility.IncompatibleCases + compatibility.MissingCases).ToString()).Replace("__CASE_REVIEW__", compatibility.ReviewCases.ToString())
            .Replace("__COMPATIBILITY__", compatibility.Html)
            .Replace("__EVIDENCE__", evidence);

        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QAFlowRecorder", "automation", "matrix-reports");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, $"browser-matrix-{first.BatchDisplay}-{DateTime.Now:yyyyMMdd-HHmmss}.html");
        await File.WriteAllTextAsync(path, html, Encoding.UTF8);
        return path;
    }

    private static string BuildExecutiveActions(
        IReadOnlyList<AutomationQueueItem> items,
        (int ComparedCases, int ConsistentCases, int IncompatibleCases, int MissingCases, int ReviewCases, string Html) compatibility,
        Func<string?, string> h)
    {
        var actions = new List<string>();
        var pending = items.Count(x => x.Status is AutomationQueueStatus.Queued or AutomationQueueStatus.Running);
        var failed = items.Count(x => x.Status == AutomationQueueStatus.Failed);
        var review = items.Count(x => x.Status == AutomationQueueStatus.NeedsReview);

        if (pending > 0) actions.Add($"Finish {pending} pending browser job(s) before using the matrix as release evidence.");
        if (failed > 0) actions.Add($"Inspect {failed} failed browser run(s) and open the individual suite report for root-cause evidence.");
        if (compatibility.IncompatibleCases > 0) actions.Add($"Review {compatibility.IncompatibleCases} case(s) with different outcomes across browsers.");
        if (compatibility.MissingCases > 0) actions.Add($"Investigate {compatibility.MissingCases} case(s) missing from at least one browser execution.");
        if (review + compatibility.ReviewCases > 0) actions.Add($"Resolve manual review items before final QA sign-off.");
        if (actions.Count == 0) actions.Add("No blocking cross-browser findings were detected. Perform the normal QA sign-off process using this report and its evidence.");

        return string.Join("", actions.Select(x => $"<li>{h(x)}</li>"));
    }

    private static (int ComparedCases, int ConsistentCases, int IncompatibleCases, int MissingCases, int ReviewCases, string Html) BuildCompatibilityAnalysis(
        IReadOnlyList<BrowserSnapshot> snapshots,
        Func<string?, string> h)
    {
        if (snapshots.Count < 2)
            return (0, 0, 0, 0, 0, "<div class='empty'>Select at least two browsers in the same matrix batch to compare compatibility.</div>");

        var browserRuns = snapshots.Where(s => s.Run is not null).ToList();
        if (browserRuns.Count < 2)
            return (0, 0, 0, 0, 0, "<div class='empty'>Not enough completed suite-run data is available for a differential comparison.</div>");

        var allCaseIds = browserRuns.SelectMany(s => s.Cases.Keys).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        if (allCaseIds.Count == 0)
            return (0, 0, 0, 0, 0, "<div class='empty'>No executable test-case results were found in this matrix batch.</div>");

        var consistent = 0;
        var incompatible = 0;
        var missing = 0;
        var review = 0;
        var detailRows = new StringBuilder();

        foreach (var caseId in allCaseIds)
        {
            var available = browserRuns.Where(s => s.Cases.ContainsKey(caseId)).ToList();
            var missingBrowsers = browserRuns.Where(s => !s.Cases.ContainsKey(caseId)).Select(s => s.Queue.Browser).ToList();
            var results = available.Select(s => s.Cases[caseId]).ToList();
            var normalizedStatuses = results.Select(r => NormalizeStatus(r.Status)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var hasReview = results.Any(r => r.Status is AutomationRunStatus.NeedsReview or AutomationRunStatus.Blocked);
            var isMissing = missingBrowsers.Count > 0;
            var isDifferent = normalizedStatuses.Count > 1;

            string classification;
            string cls;
            if (isMissing)
            {
                missing++;
                classification = "Missing result";
                cls = "fail";
            }
            else if (isDifferent)
            {
                incompatible++;
                classification = "Different result";
                cls = "fail";
            }
            else if (hasReview)
            {
                review++;
                classification = "Needs review";
                cls = "review";
            }
            else
            {
                consistent++;
                classification = "Consistent";
                cls = "pass";
            }

            var representative = results.FirstOrDefault();
            var perBrowser = new StringBuilder();
            foreach (var snapshot in browserRuns)
            {
                if (!snapshot.Cases.TryGetValue(caseId, out var c))
                {
                    perBrowser.Append($"<div class='browser-result'><strong>{h(snapshot.Queue.Browser)}</strong>: <span class='pill fail'>Missing</span></div>");
                    continue;
                }
                var statusClass = c.Status switch
                {
                    AutomationRunStatus.Passed => "pass",
                    AutomationRunStatus.NeedsReview or AutomationRunStatus.Blocked => "review",
                    _ => "fail"
                };
                var browserLabel = string.IsNullOrWhiteSpace(snapshot.Queue.BrowserVersion) ? snapshot.Queue.Browser : $"{snapshot.Queue.Browser} v{snapshot.Queue.BrowserVersion}";
                perBrowser.Append($"<div class='browser-result'><strong>{h(browserLabel)}</strong>: <span class='pill {statusClass}'>{h(c.Status.ToString())}</span> · {c.PassRate}% · {h(c.Duration)}</div>");
            }

            var failureDetails = available
                .Select(s => (Browser: s.Queue.Browser, Case: s.Cases[caseId]))
                .Where(x => !string.IsNullOrWhiteSpace(x.Case.FailureMessage))
                .Select(x => $"<div><strong>{h(x.Browser)}:</strong> {h(TrimMessage(x.Case.FailureMessage))}</div>")
                .ToList();
            var detail = failureDetails.Count == 0 ? "—" : string.Join("", failureDetails);

            detailRows.Append($"<tr><td><strong>{h(representative?.Key)}</strong><br><span class='muted'>{h(representative?.Name)}</span></td><td><span class='pill {cls}'>{h(classification)}</span></td><td>{perBrowser}</td><td class='detail'>{detail}</td></tr>");
        }

        var html = $"<table><thead><tr><th>Test case</th><th>Compatibility</th><th>Per-browser result</th><th>Failure detail</th></tr></thead><tbody>{detailRows}</tbody></table>";
        return (allCaseIds.Count, consistent, incompatible, missing, review, html);
    }


    private static string BuildEvidenceComparison(
        IReadOnlyList<BrowserSnapshot> snapshots,
        IReadOnlyList<AutomationTestRun> testRuns,
        Func<string?, string> h)
    {
        var browserRuns = snapshots.Where(s => s.Run is not null).ToList();
        if (browserRuns.Count < 2)
            return "<div class='empty'>Run the same suite in at least two browsers to compare evidence.</div>";

        var allCaseIds = browserRuns.SelectMany(s => s.Cases.Keys).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        if (allCaseIds.Count == 0)
            return "<div class='empty'>No cross-browser case evidence is available for this batch.</div>";

        var testRunById = testRuns
            .Where(r => r.Id != Guid.Empty)
            .GroupBy(r => r.Id)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.StartedAt).First());

        var sections = new StringBuilder();
        var rendered = 0;

        foreach (var caseId in allCaseIds)
        {
            var availableCases = browserRuns
                .Where(s => s.Cases.ContainsKey(caseId))
                .Select(s => (Snapshot: s, Case: s.Cases[caseId]))
                .ToList();

            if (availableCases.Count < 2) continue;

            var statuses = availableCases.Select(x => NormalizeStatus(x.Case.Status)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var shouldPrioritize = statuses.Count > 1 ||
                                   availableCases.Any(x => x.Case.Status is AutomationRunStatus.Failed or AutomationRunStatus.NeedsReview or AutomationRunStatus.Blocked);

            var evidenceRuns = availableCases
                .Select(x => (x.Snapshot, x.Case,
                    Run: x.Case.TestRunId != Guid.Empty && testRunById.TryGetValue(x.Case.TestRunId, out var tr) ? tr : null))
                .ToList();

            var withEvidence = evidenceRuns.Count(x => x.Run?.Steps.Any(s => !string.IsNullOrWhiteSpace(s.ScreenshotPath) && File.Exists(s.ScreenshotPath)) == true);
            if (!shouldPrioritize && withEvidence < 2) continue;

            var representative = availableCases[0].Case;
            var cards = new StringBuilder();

            foreach (var entry in evidenceRuns)
            {
                var browserLabel = string.IsNullOrWhiteSpace(entry.Snapshot.Queue.BrowserVersion)
                    ? entry.Snapshot.Queue.Browser
                    : $"{entry.Snapshot.Queue.Browser} v{entry.Snapshot.Queue.BrowserVersion}";

                var statusClass = entry.Case.Status switch
                {
                    AutomationRunStatus.Passed => "pass",
                    AutomationRunStatus.NeedsReview or AutomationRunStatus.Blocked => "review",
                    _ => "fail"
                };

                var run = entry.Run;
                AutomationStepRun? evidenceStep = null;
                if (run is not null)
                {
                    evidenceStep = run.Steps
                        .Where(s => !string.IsNullOrWhiteSpace(s.ScreenshotPath) && File.Exists(s.ScreenshotPath))
                        .OrderByDescending(s => s.Status is AutomationStepRunStatus.Failed or AutomationStepRunStatus.ManualFailed)
                        .ThenByDescending(s => s.Order)
                        .FirstOrDefault();
                }

                string imageHtml;
                string evidenceMeta;
                if (evidenceStep is not null)
                {
                    var uri = new Uri(evidenceStep.ScreenshotPath).AbsoluteUri;
                    imageHtml = $"<a href='{uri}'><img class='evidence-img' src='{uri}' alt='{h(browserLabel)} evidence'></a>";
                    evidenceMeta = $"Step {evidenceStep.Order} · {h(evidenceStep.Action)} · {h(evidenceStep.Target)}";
                }
                else
                {
                    imageHtml = "<div class='evidence-missing'>No screenshot captured</div>";
                    evidenceMeta = "No visual evidence available";
                }

                var caseReport = File.Exists(entry.Case.ReportPath)
                    ? $"<a href='{new Uri(entry.Case.ReportPath).AbsoluteUri}'>Open case report</a>"
                    : "";

                cards.Append($"""
<div class='evidence-card'>
  <div class='evidence-head'><strong>{h(browserLabel)}</strong><span class='pill {statusClass}'>{h(entry.Case.Status.ToString())}</span></div>
  {imageHtml}
  <div class='evidence-meta'>{evidenceMeta}</div>
  <div class='muted'>{entry.Case.PassRate}% · {h(entry.Case.Duration)} {caseReport}</div>
</div>
""");
            }

            var classification = statuses.Count > 1 ? "Functional result differs" :
                availableCases.Any(x => x.Case.Status is AutomationRunStatus.NeedsReview or AutomationRunStatus.Blocked) ? "Review evidence" :
                "Comparable evidence";

            sections.Append($"""
<section class='evidence-case'>
  <div class='evidence-case-title'><div><strong>{h(representative.Key)}</strong> · {h(representative.Name)}</div><span class='muted'>{h(classification)}</span></div>
  <div class='evidence-grid'>{cards}</div>
</section>
""");
            rendered++;
        }

        return rendered == 0
            ? "<div class='empty'>No comparable screenshots were captured in at least two browsers. Functional differential results remain available above.</div>"
            : sections.ToString();
    }

    private static string CaseIdentity(AutomationSuiteCaseResult c)
        => c.TestCaseId != Guid.Empty ? c.TestCaseId.ToString("N") : $"{c.TestCaseKey}|{c.TestCaseName}";

    private static string NormalizeStatus(AutomationRunStatus status) => status switch
    {
        AutomationRunStatus.Passed => "Passed",
        AutomationRunStatus.NeedsReview or AutomationRunStatus.Blocked => "Review",
        AutomationRunStatus.Aborted => "Aborted",
        _ => "Failed"
    };

    private static string TrimMessage(string value)
    {
        var text = value.Replace("\r", " ").Replace("\n", " ").Trim();
        return text.Length <= 220 ? text : text[..217] + "...";
    }

    private static int BrowserOrder(string browser) => browser.ToLowerInvariant() switch
    {
        "chrome" => 0,
        "edge" => 1,
        "firefox" => 2,
        _ => 9
    };
}
