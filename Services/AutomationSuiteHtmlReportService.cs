using System.Net;
using System.Text;
using QAFlowRecorder.Models;

namespace QAFlowRecorder.Services;

public static class AutomationSuiteHtmlReportService
{
    public static async Task<string> GenerateAsync(AutomationSuiteRun run)
    {
        static string H(string? v) => WebUtility.HtmlEncode(v ?? string.Empty);
        var rows = new StringBuilder();
        foreach (var item in run.Cases)
        {
            var cls = item.Status == AutomationRunStatus.Passed ? "pass" : item.Status == AutomationRunStatus.Failed ? "fail" : "review";
            var report = string.IsNullOrWhiteSpace(item.HtmlReportPath) ? "" : $"<a href='file:///{H(item.HtmlReportPath.Replace('\\','/'))}'>Open case report</a>";
            var stage = string.IsNullOrWhiteSpace(item.LifecycleStage) ? "Test" : item.LifecycleStage;
            rows.Append($"<tr><td>{H(stage)}</td><td>{H(item.TestCaseKey)}</td><td>{H(item.TestCaseName)}</td><td>{H(item.DataSetName)}</td><td class='{cls}'>{H(item.Status.ToString())}</td><td>{item.PassRate}%</td><td>{H(item.Duration)}</td><td>{report}</td></tr>");
        }
        var path = Path.Combine(run.RunFolder, "QAFlow-Suite-Execution-Report.html");
        var html = """
<!doctype html><html><head><meta charset="utf-8"><title>QAFlow Suite Execution Report</title><style>
:root{color-scheme:dark}body{margin:0;background:#09111b;color:#eef5fc;font:14px Segoe UI,Arial,sans-serif}.wrap{max-width:1120px;margin:auto;padding:38px}.hero,.card{background:#101b29;border:1px solid #29425d;border-radius:14px;padding:22px;margin-bottom:16px}.brand{color:#5bb6ff;font-weight:700;letter-spacing:.08em}.grid{display:grid;grid-template-columns:repeat(5,1fr);gap:12px}.metric{background:#0b1520;border:1px solid #233b53;border-radius:10px;padding:14px}.metric strong{display:block;font-size:25px;margin-top:4px}.scope{padding:10px 12px;border-radius:9px;background:#0c2332;color:#91d9ff}.muted{color:#8fa8c0}.pass{color:#4de0a4}.fail{color:#ff7888}.review{color:#f8c768}table{width:100%;border-collapse:collapse}th,td{text-align:left;padding:11px;border-bottom:1px solid #263a50}th{color:#9ab3cb}a{color:#6fc6ff}@media(max-width:800px){.grid{grid-template-columns:1fr 1fr}}</style></head><body><main class="wrap">
<div class="hero"><div class="brand">QAFLOW RECORDER · WEB AUTOMATION</div><h1>Suite Execution Report</h1><p>__PROJECT__ / __SUITE__</p><div class="scope">Environment: __ENVIRONMENT__ · Web scope: __BASEURL__ · Browser: __BROWSER__ · Test data: __DATASET__</div></div>
<div class="grid"><div class="metric"><span>Status</span><strong>__STATUS__</strong></div><div class="metric"><span>Pass rate</span><strong>__PASSRATE__%</strong></div><div class="metric"><span>Passed</span><strong>__PASSED__</strong></div><div class="metric"><span>Failed</span><strong>__FAILED__</strong></div><div class="metric"><span>Review</span><strong>__REVIEW__</strong></div></div>
<div class="card"><b>Run</b><p class="muted">Started __STARTED__ · Duration __DURATION__ · __COUNT__ test case(s)</p></div>
<div class="card"><h2>Test case results</h2><table><thead><tr><th>Stage</th><th>ID</th><th>Test case</th><th>Data</th><th>Status</th><th>Pass</th><th>Duration</th><th>Report</th></tr></thead><tbody>__ROWS__</tbody></table></div>
</main></body></html>
""";
        html = html.Replace("__PROJECT__", H(run.ProjectName)).Replace("__SUITE__", H(run.SuiteName)).Replace("__ENVIRONMENT__", H(string.IsNullOrWhiteSpace(run.EnvironmentName) ? "Default" : run.EnvironmentName)).Replace("__BASEURL__", H(run.BaseUrl)).Replace("__BROWSER__", H(run.Browser)).Replace("__DATASET__", H(string.IsNullOrWhiteSpace(run.DataSetName) ? "Case defaults" : run.DataSetName)).Replace("__STATUS__", H(run.Status.ToString())).Replace("__PASSRATE__", run.PassRate.ToString()).Replace("__PASSED__", run.PassedCases.ToString()).Replace("__FAILED__", run.FailedCases.ToString()).Replace("__REVIEW__", run.ReviewCases.ToString()).Replace("__STARTED__", H(run.StartedDisplay)).Replace("__DURATION__", H(run.DurationDisplay)).Replace("__COUNT__", run.Cases.Count.ToString()).Replace("__ROWS__", rows.ToString());
        await File.WriteAllTextAsync(path, html, Encoding.UTF8);
        return path;
    }
}
