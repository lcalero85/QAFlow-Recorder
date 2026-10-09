using System.Net;
using System.Text;
using QAFlowRecorder.Models;

namespace QAFlowRecorder.Services;

public static class AutomationHtmlReportService
{
    public static async Task<string> GenerateAsync(AutomationTestRun run)
    {
        var path = Path.Combine(run.RunFolder, $"QAFlow-Test-Run-{Safe(run.TestCaseKey)}.html");
        static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
        string Img(string file)
        {
            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file)) return "";
            var rel = Path.GetRelativePath(run.RunFolder, file).Replace('\\', '/');
            return $"<img src='{H(rel)}' loading='lazy' alt='Evidence'/>";
        }
        var rows = new StringBuilder();
        foreach (var s in run.Steps.OrderBy(x => x.Order))
        {
            var cls = s.Status is AutomationStepRunStatus.Passed or AutomationStepRunStatus.ManualPassed ? "pass" :
                      s.Status is AutomationStepRunStatus.Failed or AutomationStepRunStatus.ManualFailed ? "fail" : "neutral";
            var assertionHtml = s.Assertions.Count == 0 ? "" : "<div class='assertions'><b>Assertions</b>" + string.Join("", s.Assertions.Select(a => $"<div class='assert {(a.Status == AutomationAssertionRunStatus.Passed ? "pass-text" : "fail-text")}'>{H(a.Type.ToString())}{(string.IsNullOrWhiteSpace(a.Expected) ? "" : " · " + H(a.Expected))} — {H(a.Message)}</div>")) + "</div>";
            var recoveryHtml = s.SelectorHealed
                ? $"<div class='recovery'><b>Selector self-healing</b><div>{H(s.HealingMessage)}</div><div class='muted'>Score: {(s.HealingScore > 0 ? s.HealingScore + "/100" : "n/a")} · Strategy: {H(s.HealingStrategy)} · Retries: {s.RetryCount}</div></div>"
                : s.RetryCount > 0 ? $"<div class='recovery'><b>Recovery</b><div>Retries performed: {s.RetryCount}</div></div>" : "";
            var stage = string.IsNullOrWhiteSpace(s.LifecycleStage) || s.LifecycleStage == "Test" ? "" : $"<span class='stage'>{H(s.LifecycleStage)}</span>";
            rows.Append($"<section class='step {cls}'><div class='step-head'><span class='num'>{s.Order}</span><div><b>{stage}{H(s.Action)} · {H(s.Target)}</b><div class='muted'>{H(s.StatusDisplay)} · {H(s.Url)}</div></div></div><p>{H(s.Message)}</p>{recoveryHtml}{assertionHtml}{Img(s.ScreenshotPath)}</section>");
        }
        var statusClass = run.Status == AutomationRunStatus.Passed ? "pass" : "fail";
        var failureHtml = string.IsNullOrWhiteSpace(run.FailureMessage)
            ? string.Empty
            : $"<p class='status fail'>{H(run.FailureMessage)}</p>";
        var rowsHtml = rows.ToString();
        var html = """
<!doctype html><html><head><meta charset="utf-8"><title>QAFlow Test Execution Report · __TITLE__</title>
<style>
:root{color-scheme:dark}body{margin:0;background:#09111b;color:#ecf4fc;font:14px Segoe UI,Arial,sans-serif}.wrap{max-width:1100px;margin:auto;padding:36px}.hero,.card,.step{background:#101b29;border:1px solid #29425d;border-radius:14px;padding:20px;margin-bottom:16px}.brand{color:#5bb6ff;font-weight:700;letter-spacing:.08em}.grid{display:grid;grid-template-columns:repeat(4,1fr);gap:12px}.metric{background:#0b1520;border:1px solid #233b53;border-radius:10px;padding:14px}.metric strong{display:block;font-size:24px}.pass{border-color:#236b52}.fail{border-color:#873848}.status.pass{color:#4de0a4}.status.fail{color:#ff7888}.muted{color:#8fa8c0;font-size:12px}.step-head{display:flex;gap:12px;align-items:center}.num{display:grid;place-items:center;width:34px;height:34px;border-radius:50%;background:#1d4d78;font-weight:700}.step img{max-width:100%;border-radius:8px;border:1px solid #29425d;margin-top:10px}code{color:#8fd3ff}h1{margin:.2em 0}.scope{padding:10px 12px;border-radius:9px;background:#0c2332;color:#91d9ff}.assertions{margin:10px 0;padding:10px;border-radius:8px;background:#0b1520}.recovery{margin:10px 0;padding:10px;border-radius:8px;background:#11253a;border-left:3px solid #5bb6ff}.assert{margin-top:5px}.pass-text{color:#4de0a4}.fail-text{color:#ff7888}.stage{display:inline-block;margin-right:8px;padding:2px 7px;border-radius:999px;background:#17324a;color:#83cfff;font-size:11px}@media(max-width:760px){.grid{grid-template-columns:1fr 1fr}}
</style></head><body><main class="wrap">
<div class="hero"><div class="brand">QAFLOW RECORDER · WEB AUTOMATION</div><h1>Test Execution Report</h1><p>__PROJECT__ / __SUITE__ / __DISPLAY__</p><div class="scope">Web scope: __BASEURL__ · Browser: __BROWSER__ · Test data: __DATASET__</div></div>
<div class="grid"><div class="metric"><span>Status</span><strong class="status __STATUSCLASS__">__STATUS__</strong></div><div class="metric"><span>Pass rate</span><strong>__PASSRATE__%</strong></div><div class="metric"><span>Passed</span><strong>__PASSED__</strong></div><div class="metric"><span>Failed</span><strong>__FAILED__</strong></div></div>
<div class="card"><b>Run</b><p class="muted">Started __STARTED__ · Duration __DURATION__ · Test Case v__VERSION__</p>__FAILURE__</div>
<h2>Execution steps</h2>__ROWS__
</main></body></html>
""";
        html = html
            .Replace("__TITLE__", H(run.DisplayName))
            .Replace("__PROJECT__", H(run.ProjectName))
            .Replace("__SUITE__", H(run.SuiteName))
            .Replace("__DISPLAY__", H(run.DisplayName))
            .Replace("__BASEURL__", H(run.BaseUrl))
            .Replace("__BROWSER__", H(run.Browser))
            .Replace("__DATASET__", H(string.IsNullOrWhiteSpace(run.DataSetName) ? "None" : run.DataSetName))
            .Replace("__STATUSCLASS__", statusClass)
            .Replace("__STATUS__", H(run.Status.ToString()))
            .Replace("__PASSRATE__", run.PassRate.ToString())
            .Replace("__PASSED__", run.PassedCount.ToString())
            .Replace("__FAILED__", run.FailedCount.ToString())
            .Replace("__STARTED__", H(run.StartedDisplay))
            .Replace("__DURATION__", H(run.DurationDisplay))
            .Replace("__VERSION__", run.TestCaseVersion.ToString())
            .Replace("__FAILURE__", failureHtml)
            .Replace("__ROWS__", rowsHtml);
        await File.WriteAllTextAsync(path, html, Encoding.UTF8);
        return path;
    }

    private static string Safe(string v) => string.Concat((v ?? "run").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}
