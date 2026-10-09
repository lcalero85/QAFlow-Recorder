using System.Net;
using System.Text;
using QAFlowRecorder.Models;

namespace QAFlowRecorder.Services;

public static class AutomationDataDrivenHtmlReportService
{
    public static async Task<string> GenerateAsync(AutomationProject project, AutomationSuite suite, AutomationTestCase testCase, IReadOnlyList<AutomationTestRun> runs)
    {
        static string H(string? v) => WebUtility.HtmlEncode(v ?? string.Empty);
        var folder = runs.FirstOrDefault()?.RunFolder;
        var parent = string.IsNullOrWhiteSpace(folder) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "QAFlow Recorder", "AutomationRuns") : Directory.GetParent(folder!)?.FullName ?? folder!;
        Directory.CreateDirectory(parent);
        var path = Path.Combine(parent, $"QAFlow-DataDriven-{Safe(testCase.Key)}-{DateTime.Now:yyyyMMdd-HHmmss}.html");
        var rows = new StringBuilder();
        foreach (var r in runs)
        {
            var cls = r.Status == AutomationRunStatus.Passed ? "pass" : "fail";
            var link = string.IsNullOrWhiteSpace(r.HtmlReportPath) ? "" : $"<a href='file:///{H(r.HtmlReportPath.Replace('\\','/'))}'>Abrir detalle</a>";
            rows.Append($"<tr><td>{H(r.DataSetName)}</td><td class='{cls}'>{H(r.Status.ToString())}</td><td>{r.PassRate}%</td><td>{H(r.DurationDisplay)}</td><td>{link}</td></tr>");
        }
        var passed = runs.Count(r => r.Status == AutomationRunStatus.Passed);
        var html = """
<!doctype html>
<html>
<head>
<meta charset='utf-8'>
<title>QAFlow Data-driven Report</title>
<style>
:root{color-scheme:dark}
body{background:#09111b;color:#eef5fc;font:14px Segoe UI,Arial;margin:0}
main{max-width:1000px;margin:auto;padding:36px}
.card{background:#101b29;border:1px solid #29425d;border-radius:14px;padding:20px;margin-bottom:16px}
table{width:100%;border-collapse:collapse}
th,td{padding:11px;border-bottom:1px solid #263a50;text-align:left}
.pass{color:#4de0a4}
.fail{color:#ff7888}
a{color:#6fc6ff}
</style>
</head>
<body>
<main>
<div class='card'>
<b>QAFLOW RECORDER · DATA-DRIVEN</b>
<h1>{{TEST_CASE}}</h1>
<p>{{PROJECT}} / {{SUITE}}</p>
<p>{{PASSED}}/{{TOTAL}} iteraciones aprobadas</p>
</div>
<div class='card'>
<table>
<thead><tr><th>Datos</th><th>Estado</th><th>Pass</th><th>Duración</th><th>Reporte</th></tr></thead>
<tbody>{{ROWS}}</tbody>
</table>
</div>
</main>
</body>
</html>
"""
            .Replace("{{TEST_CASE}}", H(testCase.DisplayName), StringComparison.Ordinal)
            .Replace("{{PROJECT}}", H(project.Name), StringComparison.Ordinal)
            .Replace("{{SUITE}}", H(suite.Name), StringComparison.Ordinal)
            .Replace("{{PASSED}}", passed.ToString(), StringComparison.Ordinal)
            .Replace("{{TOTAL}}", runs.Count.ToString(), StringComparison.Ordinal)
            .Replace("{{ROWS}}", rows.ToString(), StringComparison.Ordinal);
        await File.WriteAllTextAsync(path, html, Encoding.UTF8);
        return path;
    }
    private static string Safe(string v) => string.Concat((v ?? "run").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}
