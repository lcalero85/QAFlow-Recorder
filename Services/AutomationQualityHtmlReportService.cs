using System.Net;
using System.Text;
using QAFlowRecorder.Models;

namespace QAFlowRecorder.Services;

public static class AutomationQualityHtmlReportService
{
    public static async Task<string> GenerateAsync(AutomationQualitySnapshot snapshot, string language)
    {
        var es = language.StartsWith("Esp", StringComparison.OrdinalIgnoreCase);
        static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QAFlowRecorder", "automation-quality");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, $"quality-intelligence-{DateTime.Now:yyyyMMdd-HHmmss}.html");

        var stabilityRows = new StringBuilder();
        foreach (var item in snapshot.TestStability.Take(40))
            stabilityRows.Append($"<tr><td>{H(item.DisplayName)}</td><td>{H(item.Browser)}</td><td><span class='pill {CssClass(item.Classification)}'>{H(TranslateClass(item.Classification, es))}</span></td><td>{item.Runs}</td><td>{item.RunSummary}</td><td>{item.FailureRate}%</td><td>{item.StabilityScore}%</td><td>{H(item.LastStatus.ToString())}</td></tr>");

        var failureRows = new StringBuilder();
        foreach (var item in snapshot.FailureClusters)
            failureRows.Append($"<tr><td>{item.Count}</td><td>{item.TestCaseCount}</td><td>{H(item.SampleMessage)}</td><td>{H(item.TestCases)}</td><td>{H(item.Browsers)}</td><td>{H(item.LastSeenDisplay)}</td></tr>");

        var trendRows = new StringBuilder();
        foreach (var item in snapshot.Trends)
            trendRows.Append($"<tr><td>{H(item.StartedDisplay)}</td><td>{H(item.SuiteName)}</td><td>{H(item.EnvironmentName)}</td><td>{H(item.Browser)}</td><td>{H(item.Status.ToString())}</td><td>{item.PassRate}%</td><td>{H(item.ResultSummary)}</td><td>{H(item.Duration)}</td></tr>");

        var title = es ? "QAFlow · Inteligencia de calidad" : "QAFlow · Quality Intelligence";
        var html = """
<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>__TITLE__</title>
<style>
:root{color-scheme:dark;--bg:#08111b;--panel:#0d1926;--panel2:#111f2e;--line:#243a50;--text:#eef6ff;--muted:#8fa8c0;--blue:#61b4ff;--green:#49d69d;--amber:#f5bd62;--red:#ff6d7d}
*{box-sizing:border-box}body{margin:0;background:linear-gradient(160deg,#08111b,#0a1420 45%,#071018);color:var(--text);font-family:Segoe UI,Arial,sans-serif}.wrap{max-width:1400px;margin:auto;padding:34px}.hero{display:flex;justify-content:space-between;gap:24px;align-items:center;padding:24px;border:1px solid var(--line);background:rgba(13,25,38,.94);border-radius:16px}.brand{display:flex;gap:14px;align-items:center}.logo{width:48px;height:48px;border-radius:13px;background:linear-gradient(135deg,#1f7cff,#69d4ff);display:grid;place-items:center;font-weight:800;font-size:20px;color:#06111d}.muted{color:var(--muted)}h1{margin:0 0 4px;font-size:26px}.cards{display:grid;grid-template-columns:repeat(5,1fr);gap:12px;margin:18px 0}.card,.section{border:1px solid var(--line);background:var(--panel);border-radius:14px}.card{padding:16px}.label{font-size:11px;color:var(--muted);text-transform:uppercase;letter-spacing:.08em}.value{font-size:27px;font-weight:700;margin-top:6px}.section{padding:18px;margin-top:16px}h2{font-size:17px;margin:0 0 12px}table{width:100%;border-collapse:collapse;font-size:13px}th,td{padding:10px;border-bottom:1px solid #1d3042;text-align:left;vertical-align:top}th{color:#a9bfd3;font-size:11px;text-transform:uppercase;letter-spacing:.05em}.pill{padding:4px 8px;border-radius:999px;border:1px solid #35516b;background:#15283a}.flaky{border-color:#9b6a22;color:#ffd28a}.fail{border-color:#8e3440;color:#ff9ba6}.stable{border-color:#22664f;color:#78e4b7}.other{color:#bed0df}@media(max-width:900px){.cards{grid-template-columns:1fr 1fr}.wrap{padding:18px}.hero{align-items:flex-start;flex-direction:column}}
</style></head><body><div class="wrap"><div class="hero"><div class="brand"><div class="logo">QF</div><div><h1>__TITLE__</h1><div class="muted">__PROJECT__ · __GENERATED__</div></div></div><div class="muted">__SCOPE__</div></div>
<div class="cards"><div class="card"><div class="label">__PASS_LABEL__</div><div class="value">__PASS__%</div></div><div class="card"><div class="label">__FLAKY_LABEL__</div><div class="value">__FLAKY__</div></div><div class="card"><div class="label">__FAIL_LABEL__</div><div class="value">__FAILS__</div></div><div class="card"><div class="label">__STABLE_LABEL__</div><div class="value">__STABLE__</div></div><div class="card"><div class="label">__RUNS_LABEL__</div><div class="value">__RUNS__</div></div></div>
<div class="section"><h2>__STABILITY_TITLE__</h2><table><thead><tr><th>__CASE__</th><th>Browser</th><th>__CLASS__</th><th>Runs</th><th>__RESULTS__</th><th>__FAILRATE__</th><th>__STABILITY__</th><th>__LAST__</th></tr></thead><tbody>__STABILITY_ROWS__</tbody></table></div>
<div class="section"><h2>__FAILURES_TITLE__</h2><table><thead><tr><th>Count</th><th>Cases</th><th>__MESSAGE__</th><th>__AFFECTED__</th><th>Browsers</th><th>__LAST_SEEN__</th></tr></thead><tbody>__FAILURE_ROWS__</tbody></table></div>
<div class="section"><h2>__TREND_TITLE__</h2><table><thead><tr><th>__STARTED__</th><th>Suite</th><th>Environment</th><th>Browser</th><th>Status</th><th>Pass rate</th><th>__RESULTS__</th><th>Duration</th></tr></thead><tbody>__TREND_ROWS__</tbody></table></div>
</div></body></html>
""";
        html = html
            .Replace("__TITLE__", H(title))
            .Replace("__PROJECT__", H(snapshot.ProjectName))
            .Replace("__GENERATED__", H(snapshot.GeneratedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss")))
            .Replace("__SCOPE__", es ? "Histórico local de automatización web" : "Local web automation history")
            .Replace("__PASS_LABEL__", es ? "Aprobación histórica" : "Historical pass rate").Replace("__PASS__", snapshot.OverallPassRate.ToString())
            .Replace("__FLAKY_LABEL__", es ? "Casos flaky" : "Flaky tests").Replace("__FLAKY__", snapshot.FlakyTests.ToString())
            .Replace("__FAIL_LABEL__", es ? "Fallos recurrentes" : "Recurring failures").Replace("__FAILS__", snapshot.RecurringFailureClusters.ToString())
            .Replace("__STABLE_LABEL__", es ? "Casos estables" : "Stable tests").Replace("__STABLE__", snapshot.StableTests.ToString())
            .Replace("__RUNS_LABEL__", es ? "Suite runs" : "Suite runs").Replace("__RUNS__", snapshot.SuiteRunsAnalyzed.ToString())
            .Replace("__STABILITY_TITLE__", es ? "Estabilidad de casos" : "Test stability")
            .Replace("__FAILURES_TITLE__", es ? "Agrupación de fallos" : "Failure clusters")
            .Replace("__TREND_TITLE__", es ? "Tendencia de ejecuciones" : "Execution trend")
            .Replace("__CASE__", es ? "Caso" : "Test case").Replace("__CLASS__", es ? "Clasificación" : "Classification")
            .Replace("__RESULTS__", es ? "Resultados" : "Results").Replace("__FAILRATE__", es ? "Fallo" : "Failure rate")
            .Replace("__STABILITY__", es ? "Estabilidad" : "Stability").Replace("__LAST__", es ? "Último" : "Last")
            .Replace("__MESSAGE__", es ? "Mensaje representativo" : "Representative message").Replace("__AFFECTED__", es ? "Casos afectados" : "Affected cases")
            .Replace("__LAST_SEEN__", es ? "Última vez" : "Last seen").Replace("__STARTED__", es ? "Inicio" : "Started")
            .Replace("__STABILITY_ROWS__", stabilityRows.ToString()).Replace("__FAILURE_ROWS__", failureRows.ToString()).Replace("__TREND_ROWS__", trendRows.ToString());
        await File.WriteAllTextAsync(path, html, Encoding.UTF8);
        return path;
    }

    private static string CssClass(string classification) => classification switch
    {
        "Flaky" => "flaky", "Consistently failing" => "fail", "Stable" => "stable", _ => "other"
    };
    private static string TranslateClass(string classification, bool es) => !es ? classification : classification switch
    {
        "Flaky" => "Inestable (flaky)", "Consistently failing" => "Fallo consistente", "Stable" => "Estable", "Insufficient history" => "Historial insuficiente", "Unstable" => "Inestable", _ => classification
    };
}
