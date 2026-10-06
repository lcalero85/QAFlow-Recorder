using System.Net;
using System.Text;
using QAFlowRecorder.Models;

namespace QAFlowRecorder.Services;

public static class AutomationBrowserMatrixReportService
{
    public static async Task<string> GenerateAsync(IEnumerable<AutomationQueueItem> source)
    {
        var items = source.OrderBy(x => x.Browser).ToList();
        if (items.Count == 0) return "";
        static string H(string? value) => WebUtility.HtmlEncode(value ?? "");
        var first = items[0];
        var passed = items.Count(x => x.Status == AutomationQueueStatus.Passed);
        var failed = items.Count(x => x.Status == AutomationQueueStatus.Failed);
        var review = items.Count(x => x.Status == AutomationQueueStatus.NeedsReview);
        var rows = new StringBuilder();
        foreach (var x in items)
        {
            var cls = x.Status switch { AutomationQueueStatus.Passed => "pass", AutomationQueueStatus.Failed => "fail", AutomationQueueStatus.NeedsReview => "review", _ => "neutral" };
            var report = File.Exists(x.HtmlReportPath) ? $"<a href='{new Uri(x.HtmlReportPath).AbsoluteUri}'>Open suite report</a>" : "—";
            rows.Append($"<tr><td><strong>{H(x.Browser)}</strong></td><td>{H(x.EnvironmentName)}</td><td><span class='pill {cls}'>{H(x.Status.ToString())}</span></td><td>{H(x.DurationDisplay)}</td><td>{H(x.Message)}</td><td>{report}</td></tr>");
        }
        var html = """
<!doctype html><html><head><meta charset="utf-8"><title>QAFlow Browser Matrix Report</title>
<style>
body{font-family:Segoe UI,Arial,sans-serif;background:#0b1119;color:#e9f2fb;margin:0;padding:32px}main{max-width:1180px;margin:auto}.hero,.card{background:#111b28;border:1px solid #294159;border-radius:14px;padding:20px;margin-bottom:16px}.brand{font-size:13px;color:#77c9ff;font-weight:700;letter-spacing:.1em}.grid{display:grid;grid-template-columns:repeat(4,1fr);gap:12px}.metric{background:#0d1621;border:1px solid #22384d;border-radius:10px;padding:14px}.metric b{font-size:28px;display:block;margin-top:5px}table{width:100%;border-collapse:collapse}th,td{text-align:left;padding:12px;border-bottom:1px solid #263a50;font-size:13px}th{color:#8fa8c0}.pill{padding:4px 8px;border-radius:999px;font-weight:700;font-size:11px}.pass{background:#143728;color:#5ce0a3}.fail{background:#3e2028;color:#ff8997}.review{background:#3b321e;color:#f4c66b}.neutral{background:#243142;color:#b9cbe0}a{color:#77c9ff}small{color:#8fa8c0}</style></head><body><main>
<div class="hero"><div class="brand">QAFLOW RECORDER · WEB AUTOMATION</div><h1>Browser Matrix Execution Report</h1><p>__PROJECT__ · __SUITE__</p><small>Batch __BATCH__ · Generated __GENERATED__</small></div>
<div class="grid"><div class="metric">Browsers<b>__TOTAL__</b></div><div class="metric">Passed<b>__PASSED__</b></div><div class="metric">Failed<b>__FAILED__</b></div><div class="metric">Needs review<b>__REVIEW__</b></div></div>
<div class="card"><h2>Cross-browser results</h2><table><thead><tr><th>Browser</th><th>Environment</th><th>Result</th><th>Duration</th><th>Summary</th><th>Report</th></tr></thead><tbody>__ROWS__</tbody></table></div>
</main></body></html>
""";
        html = html.Replace("__PROJECT__", H(first.ProjectName)).Replace("__SUITE__", H(first.SuiteName)).Replace("__BATCH__", H(first.BatchDisplay))
            .Replace("__GENERATED__", DateTimeOffset.Now.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"))
            .Replace("__TOTAL__", items.Count.ToString()).Replace("__PASSED__", passed.ToString()).Replace("__FAILED__", failed.ToString()).Replace("__REVIEW__", review.ToString()).Replace("__ROWS__", rows.ToString());
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QAFlowRecorder", "automation", "matrix-reports");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, $"browser-matrix-{first.BatchDisplay}-{DateTime.Now:yyyyMMdd-HHmmss}.html");
        await File.WriteAllTextAsync(path, html, Encoding.UTF8);
        return path;
    }
}
