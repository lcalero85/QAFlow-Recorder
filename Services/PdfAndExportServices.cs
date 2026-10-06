using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using QAFlowRecorder.Models;

namespace QAFlowRecorder.Services;

internal static class ReportLanguage
{
    public static bool IsEs(string lang) => lang.StartsWith("Esp", StringComparison.OrdinalIgnoreCase);
    public static string H(string? value) => System.Net.WebUtility.HtmlEncode(value ?? "");
    public static string EventName(EventKind kind, bool es) => kind switch
    {
        EventKind.Click => es ? "Clic" : "Click",
        EventKind.Input => es ? "Entrada / teclado" : "Input / keyboard",
        EventKind.Select => es ? "Selección" : "Selection",
        EventKind.Submit => es ? "Envío" : "Submit",
        EventKind.Scroll => "Scroll",
        EventKind.Dialog => es ? "Diálogo" : "Dialog",
        EventKind.Wait => es ? "Pausa" : "Pause",
        EventKind.ConsoleError => es ? "Error JavaScript" : "JavaScript error",
        EventKind.HttpError => "HTTP error",
        EventKind.ResourceError => es ? "Recurso fallido" : "Failed resource",
        _ => es ? "Interacción" : "Interaction"
    };

    public static string ShotName(ScreenshotKind kind, bool es) => kind switch
    {
        ScreenshotKind.FlowAction => es ? "Evidencia de interacción" : "Interaction evidence",
        ScreenshotKind.Annotation => es ? "Anotación QA" : "QA annotation",
        ScreenshotKind.CriticalError => es ? "Hallazgo crítico" : "Critical finding",
        ScreenshotKind.Periodic => es ? "Evidencia periódica" : "Periodic evidence",
        ScreenshotKind.Final => es ? "Estado final" : "Final state",
        ScreenshotKind.Manual => es ? "Captura manual" : "Manual capture",
        _ => es ? "Evidencia" : "Evidence"
    };

    public static string SeverityName(Severity value, bool es) => value switch
    {
        Severity.Critical => es ? "Crítica" : "Critical",
        Severity.High => es ? "Alta" : "High",
        Severity.Medium => es ? "Media" : "Medium",
        Severity.Low => es ? "Baja" : "Low",
        _ => es ? "No definida" : "Not set"
    };

    public static string ResultName(AnnotationResult value, bool es) => value switch
    {
        AnnotationResult.Doubt => es ? "Duda / por confirmar" : "Doubt / to confirm",
        AnnotationResult.Error => es ? "Error / defecto" : "Error / defect",
        _ => es ? "Observación" : "Observation"
    };

    public static string Frequency(string value, bool es) => value switch
    {
        "Always" => es ? "Siempre" : "Always",
        "Sometimes" => es ? "A veces" : "Sometimes",
        "Intermittent" => es ? "Intermitente" : "Intermittent",
        _ => es ? "Primera vez" : "First time"
    };

    public static string Template(string value, bool es) => value switch
    {
        "Functional Normal" => es ? "Funcional Normal" : "Functional Normal",
        "Regression" => es ? "Regresión" : "Regression",
        "Exploratory" => es ? "Exploratorio" : "Exploratory",
        _ => value
    };

    public static string Target(QaEvent e)
    {
        var app = string.IsNullOrWhiteSpace(e.ApplicationName) ? "" : e.ApplicationName.Trim();
        var title = !string.IsNullOrWhiteSpace(e.WindowTitle) ? e.WindowTitle.Trim() : e.TargetTitle.Trim();
        if (!string.IsNullOrWhiteSpace(app) && !string.IsNullOrWhiteSpace(title) && !string.Equals(app, title, StringComparison.OrdinalIgnoreCase))
            return $"{app} — {title}";
        if (!string.IsNullOrWhiteSpace(title)) return title;
        if (!string.IsNullOrWhiteSpace(app)) return app;
        return string.IsNullOrWhiteSpace(e.Description) ? "Desktop" : e.Description;
    }

    public static string Action(QaEvent e, bool es)
    {
        return H(SmartNarrativeService.Describe(e, es));
    }

    public static bool IsAction(QaEvent e) => !e.ExcludeFromReport && (e.Kind is EventKind.Click or EventKind.Input or EventKind.Select or EventKind.Submit or EventKind.Scroll or EventKind.Dialog);

    public static string PartLabel(TestSession s, bool es)
    {
        if (!s.IsMultipart) return "";
        if (!es) return s.PartNumber switch { 1 => "First part", 2 => "Second part", 3 => "Third part", _ => $"Part {s.PartNumber}" };
        return s.PartNumber switch { 1 => "Primera parte", 2 => "Segunda parte", 3 => "Tercera parte", _ => $"Parte {s.PartNumber}" };
    }

    public static string ImageDataUri(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return "";
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var mime = ext switch { ".jpg" or ".jpeg" => "image/jpeg", ".bmp" => "image/bmp", _ => "image/png" };
        return $"data:{mime};base64,{Convert.ToBase64String(File.ReadAllBytes(path))}";
    }

    public static string LogoDataUri()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Assets/qaflow-logo.png", UriKind.Absolute);
            var resource = System.Windows.Application.GetResourceStream(uri);
            if (resource?.Stream is null) return "";
            using var ms = new MemoryStream();
            resource.Stream.CopyTo(ms);
            return $"data:image/png;base64,{Convert.ToBase64String(ms.ToArray())}";
        }
        catch
        {
            return "";
        }
    }

    public static EvidenceShot? EvidenceFor(TestSession s, QaEvent e, HashSet<Guid>? used = null)
    {
        return s.Screenshots
            .Where(x => File.Exists(x.FilePath) && x.Kind is not (ScreenshotKind.BugOriginal or ScreenshotKind.BugMarked))
            .Where(x => used is null || !used.Contains(x.Id))
            .OrderBy(x => Math.Abs((x.Timestamp - e.Timestamp).TotalMilliseconds))
            .FirstOrDefault(x => Math.Abs((x.Timestamp - e.Timestamp).TotalSeconds) <= 4.5);
    }
}

public static class QaLogHtmlGenerator
{
    public static string Generate(TestSession s, AppSettings cfg)
    {
        var es = ReportLanguage.IsEs(cfg.DocumentLanguage);
        string T(string en, string sp) => es ? sp : en;
        var steps = Deduplicator.Process(s.Events).Where(ReportLanguage.IsAction).OrderBy(e => e.Timestamp).ToList();
        var usedEvidence = new HashSet<Guid>();
        var body = new StringBuilder();
        var logo = ReportLanguage.LogoDataUri();
        var part = ReportLanguage.PartLabel(s, es);
        var reportTargetCount = steps.Select(ReportLanguage.Target).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var status = s.ErrorCount > 0 ? T("COMPLETED WITH FINDINGS", "COMPLETADA CON HALLAZGOS") : s.Annotations.Any(a => a.Result == AnnotationResult.Doubt) ? T("COMPLETED WITH OBSERVATIONS", "COMPLETADA CON OBSERVACIONES") : T("COMPLETED", "COMPLETADA");
        var coverage = CoverageAnalysisService.Analyze(s, cfg.ExpectedCoverageTargets);
        var insights = SessionInsightsService.Analyze(s);
        var risk = RiskAssessmentService.Analyze(s);
        var flowComparison = ReferenceFlowComparisonService.Compare(s);
        var evidenceQuality = EvidenceQualityService.Analyze(s);
        var visualComparison = cfg.IncludeVisualComparison ? VisualBaselineService.AnalyzeAsync(s).GetAwaiter().GetResult() : new VisualComparisonResult();
        var coverageRows = string.Join("", coverage.Items.Select(item => $"<tr><td>{ReportLanguage.H(item.Name)}</td><td><span class='coverage-status {(item.Observed ? "observed" : "missing")}'>{(item.Observed ? T("Observed","Observado") : T("Not observed","No observado"))}</span></td><td>{item.Interactions}</td><td>{item.ApproximateTime.ToString(@"hh\:mm\:ss")}</td><td>{item.EvidenceCount}</td></tr>"));
        var coverageHeadline = coverage.CoveragePercent is double p
            ? $"<div class='coverage-score'><strong>{p:0.#}%</strong><span>{T("configured targets observed","objetivos configurados observados")}</span></div>"
            : $"<div class='coverage-score'><strong>{coverage.ObservedCount}</strong><span>{T("observed targets","objetivos observados")}</span></div>";
        var coverageSection = cfg.IncludeCoverageMap
            ? $"<section class='page coverage'><h1>{T("Coverage map","Mapa de cobertura")}</h1><p>{T("This map reflects the applications, windows or modules observed during the recorded session. When expected targets are configured, QAFlow also identifies targets that were not observed.","Este mapa refleja las aplicaciones, ventanas o módulos observados durante la sesión grabada. Cuando se configuran objetivos esperados, QAFlow también identifica los que no fueron observados.")}</p>{coverageHeadline}<table><thead><tr><th>{T("Target","Objetivo")}</th><th>{T("Status","Estado")}</th><th>{T("Interactions","Interacciones")}</th><th>{T("Approx. time","Tiempo aprox.")}</th><th>{T("Evidence","Evidencia")}</th></tr></thead><tbody>{coverageRows}</tbody></table></section>"
            : "";
        var insightsSection = cfg.IncludeSessionInsights
            ? $"<section class='page insights'><h1>{T("Session insights","Hallazgos de la sesión")}</h1><p class='insight-lead'>{ReportLanguage.H(es ? insights.SummarySpanish : insights.SummaryEnglish)}</p><div class='insight-grid'><div><strong>{insights.RepeatedActions}</strong><span>{T("Repeated actions","Acciones repetidas")}</span></div><div><strong>{insights.LongPauses}</strong><span>{T("Significant pauses","Pausas significativas")}</span></div><div><strong>{insights.ReturnVisits}</strong><span>{T("Return visits","Retornos")}</span></div><div><strong>{ReportLanguage.H(insights.MostActiveTarget)}</strong><span>{T("Most active target","Objetivo con mayor actividad")}</span></div></div></section>"
            : "";
        var riskRows = string.Join("", risk.Areas.Take(10).Select(a => $"<tr><td>{ReportLanguage.H(a.Target)}</td><td><span class='risk risk-{a.Level.ToString().ToLowerInvariant()}'>{(es ? a.Level switch { RiskLevel.Critical=>"Crítico", RiskLevel.High=>"Alto", RiskLevel.Medium=>"Medio", _=>"Bajo" } : a.Level.ToString())}</span></td><td>{a.Score}/100</td><td>{ReportLanguage.H(es?a.RationaleSpanish:a.RationaleEnglish)}</td></tr>"));
        var riskSection = $"<section class='page riskpage'><h1>{T("Risk-based testing assistant","Asistente de pruebas basado en riesgo")}</h1><p>{T("QAFlow prioritizes areas that deserve retesting based on findings, repeated actions, return visits and significant waits. This is decision support, not a defect verdict.","QAFlow prioriza las áreas que merecen revalidación según hallazgos, acciones repetidas, retornos y esperas significativas. Es apoyo para la decisión, no un veredicto de defecto.")}</p><table><thead><tr><th>{T("Area / target","Área / objetivo")}</th><th>{T("Risk","Riesgo")}</th><th>{T("Score","Puntaje")}</th><th>{T("Signals","Señales")}</th></tr></thead><tbody>{riskRows}</tbody></table></section>";
        var referenceSection = string.IsNullOrWhiteSpace(s.ReferenceFlowName) ? "" : $"<section class='page reference'><h1>{T("Reusable flow comparison","Comparación con flujo reutilizable")}</h1><div class='coverage-score'><strong>{flowComparison.MatchPercent:0.#}%</strong><span>{T("reference steps matched","pasos de referencia coincidentes")}</span></div><p><b>{T("Reference flow","Flujo de referencia")}:</b> {ReportLanguage.H(s.ReferenceFlowName)} · {T("Expected","Esperados")}: {flowComparison.ExpectedSteps} · {T("Matched","Coincidentes")}: {flowComparison.MatchedSteps} · {T("Missing","Faltantes")}: {flowComparison.MissingSteps} · {T("Additional","Adicionales")}: {flowComparison.ExtraSteps}</p>{(flowComparison.MissingDescriptions.Count==0?"":$"<h2>{T("Missing expected steps","Pasos esperados faltantes")}</h2><ul>{string.Join("",flowComparison.MissingDescriptions.Select(x=>$"<li>{ReportLanguage.H(x)}</li>"))}</ul>")}</section>";

        var charterSection = string.IsNullOrWhiteSpace(s.CharterObjective) && string.IsNullOrWhiteSpace(s.CharterScope) && string.IsNullOrWhiteSpace(s.CharterRisks) ? "" :
            $"<section class='page charter'><h1>{T("Exploratory testing charter","Charter de pruebas exploratorias")}</h1><div class='charter-grid'><div><small>{T("Objective","Objetivo")}</small><b>{ReportLanguage.H(s.CharterObjective)}</b></div><div><small>{T("Scope","Alcance")}</small><b>{ReportLanguage.H(s.CharterScope)}</b></div><div><small>{T("Risks to observe","Riesgos a observar")}</small><b>{ReportLanguage.H(s.CharterRisks)}</b></div><div><small>{T("Test data","Datos de prueba")}</small><b>{ReportLanguage.H(s.CharterTestData)}</b></div><div><small>{T("Constraints / notes","Restricciones / notas")}</small><b>{ReportLanguage.H(s.CharterConstraints)}</b></div><div><small>{T("Planned duration","Duración prevista")}</small><b>{(s.CharterPlannedMinutes>0?$"{s.CharterPlannedMinutes} min":"—")}</b></div></div></section>";
        var qualityIssues = string.Join("", evidenceQuality.Issues.Select(i => $"<li>{ReportLanguage.H(es?i.MessageSpanish:i.MessageEnglish)}</li>"));
        var qualitySection = cfg.IncludeEvidenceQuality ? $"<section class='page quality'><h1>{T("Evidence quality check","Control de calidad de evidencia")}</h1><div class='coverage-score'><strong>{evidenceQuality.ReadinessPercent}%</strong><span>{T("report readiness","preparación del reporte")}</span></div>{(evidenceQuality.Issues.Count==0?$"<p>{T("No evidence quality issues were detected.","No se detectaron problemas de calidad de evidencia.")}</p>":$"<ul>{qualityIssues}</ul>")}</section>" : "";
        string VisualStatus(string status) => status switch
        {
            "Stable" => T("Stable", "Estable"),
            "Minor change" => T("Minor change", "Cambio menor"),
            "Changed" => T("Significant change", "Cambio significativo"),
            _ => T("No baseline", "Sin baseline")
        };
        var visualRows = string.Join("", visualComparison.Items.Select(v => $"<tr><td>{ReportLanguage.H(v.Target)}</td><td>{ReportLanguage.H(VisualStatus(v.Status))}</td><td>{(string.IsNullOrWhiteSpace(v.BaselinePath) ? "—" : $"{v.DifferencePercent:0.0}%")}</td></tr>"));
        var visualSection = cfg.IncludeVisualComparison ? $"<section class='page visual'><h1>{T("Baseline visual comparison","Comparación visual con baseline")}</h1><p>{T("QAFlow compares the latest evidence for each observed target against the locally stored visual baseline. Differences are indicators for review, not automatic defect verdicts.","QAFlow compara la evidencia más reciente de cada objetivo observado con el baseline visual almacenado localmente. Las diferencias son indicadores para revisión, no veredictos automáticos de defecto.")}</p><table><thead><tr><th>{T("Target","Objetivo")}</th><th>{T("Visual status","Estado visual")}</th><th>{T("Difference","Diferencia")}</th></tr></thead><tbody>{visualRows}</tbody></table></section>" : "";

        int index = 1;
        foreach (var e in steps)
        {
            if (e.IsCheckpoint)
                body.Append($"<div class='checkpoint'><span>{T("CHECKPOINT","HITO")}</span><strong>{ReportLanguage.H(SmartNarrativeService.CheckpointTitle(e, es))}</strong></div>");
            var evidence = ReportLanguage.EvidenceFor(s, e, usedEvidence);
            if (evidence is not null) usedEvidence.Add(evidence.Id);
            var annotation = s.Annotations.FirstOrDefault(a => a.EventId == e.Id);
            var keyBadge = e.IsImportant ? $" · <b>{T("KEY STEP", "PASO CLAVE")}</b>" : "";
            body.Append($"<section class='step'><div class='step-head'><span class='step-no'>{index++}</span><div><div class='eyebrow'>{ReportLanguage.EventName(e.Kind, es)} · {e.Timestamp:HH:mm:ss}{keyBadge}</div><h2>{ReportLanguage.Action(e, es)}</h2><div class='context'>{T("Application / window", "Aplicación / ventana")}: <b>{ReportLanguage.H(ReportLanguage.Target(e))}</b></div></div></div>");
            if (evidence is not null)
                body.Append($"<figure><img src='{ReportLanguage.ImageDataUri(evidence.FilePath)}'/><figcaption>{T("Visual evidence", "Evidencia visual")} · {evidence.Timestamp:HH:mm:ss}</figcaption></figure>");
            if (annotation is not null)
            {
                body.Append($"<div class='result {(annotation.Result == AnnotationResult.Error ? "bad" : annotation.Result == AnnotationResult.Doubt ? "warn" : "ok")}'><b>{T("QA observation", "Observación QA")}</b><div><strong>{T("Expected", "Esperado")}:</strong> {ReportLanguage.H(annotation.Expected)}</div><div><strong>{T("Observed", "Observado")}:</strong> {ReportLanguage.H(string.IsNullOrWhiteSpace(annotation.Actual) ? annotation.Expected : annotation.Actual)}</div>{(string.IsNullOrWhiteSpace(annotation.Note) ? "" : $"<div><strong>{T("Note", "Nota")}:</strong> {ReportLanguage.H(annotation.Note)}</div>")}</div>");
            }
            body.Append("</section>");
        }

        var noSteps = steps.Count == 0 ? $"<section class='page'><h1>{T("No desktop interactions captured", "No se capturaron interacciones de escritorio")}</h1><p>{T("This part contains no recorded user interactions.", "Esta parte no contiene interacciones de usuario registradas.")}</p></section>" : "";
        return $$"""
<!doctype html><html><head><meta charset='utf-8'><title>QAFlow Recorder — {{T("QA Execution Report","Reporte de Ejecución QA")}}</title><style>
@page{size:A4;margin:17mm 16mm 18mm}*{box-sizing:border-box}body{font-family:Segoe UI,Arial,sans-serif;color:#17212b;font-size:9.5pt;line-height:1.45}.cover{height:255mm;display:flex;flex-direction:column;justify-content:center}.brandrow{display:flex;align-items:center;gap:12px}.logo{width:22mm;height:22mm;object-fit:contain}.brand{font-size:9pt;letter-spacing:.15em;color:#4776a8;font-weight:700}.cover h1{font-size:29pt;line-height:1.05;color:#10233d;margin:16px 0 8px}.cover h2{font-size:16pt;color:#3b4d61;font-weight:500;margin:0 0 24px}.part{display:inline-block;margin-left:8px;color:#4776a8}.status{display:inline-block;padding:7px 12px;border-radius:6px;background:#eaf2fb;color:#245a91;font-weight:700}.meta{margin-top:22px;border-top:1px solid #dce4ec;padding-top:16px;display:grid;grid-template-columns:1fr 1fr;gap:9px 16px;max-width:150mm}.meta div small{display:block;color:#74899d;text-transform:uppercase;font-size:7pt;letter-spacing:.06em}.meta div b{display:block;margin-top:3px}.charter-grid{display:grid;grid-template-columns:1fr 1fr;gap:10px;margin-top:15px}.charter-grid div{border:1px solid #dce4ec;border-radius:7px;padding:10px}.charter-grid small{display:block;color:#74899d;text-transform:uppercase;font-size:7pt}.charter-grid b{display:block;margin-top:4px;white-space:pre-wrap}.summary{page-break-before:always}.summary-grid{display:grid;grid-template-columns:repeat(4,1fr);gap:9px;margin:18px 0 28px}.kpi{background:#f4f7fa;border:1px solid #dce4ec;border-radius:8px;padding:13px;text-align:center}.kpi strong{display:block;font-size:20pt;color:#1c4f80}.kpi span{font-size:7pt;color:#6f8294;text-transform:uppercase}.step{page-break-before:always}.step-head{display:flex;gap:14px;align-items:flex-start;border-bottom:2px solid #dce6f0;padding-bottom:10px}.step-no{display:flex;width:34px;height:34px;border-radius:50%;background:#24659e;color:white;align-items:center;justify-content:center;font-weight:700;font-size:13pt}.eyebrow{font-size:7.5pt;letter-spacing:.08em;color:#6f8aa6;text-transform:uppercase}.step h2{font-size:14pt;margin:3px 0 3px;color:#112b48}.context{font-size:8.5pt;color:#63778b}.target{font-weight:700}.checkpoint{margin:12px 0 8px;padding:10px 12px;border-left:4px solid #2d8cff;background:#eef6ff;border-radius:5px;page-break-after:avoid}.checkpoint span{display:block;font-size:7pt;letter-spacing:.12em;color:#4477a8;font-weight:700}.checkpoint strong{display:block;margin-top:3px;color:#183a5b;font-size:11pt}.step figure{margin:14px 0;page-break-inside:avoid}.step img{display:block;max-width:176mm;max-height:176mm;object-fit:contain;margin:auto;border:1px solid #bfcbd7;border-radius:5px}.step figcaption{text-align:center;color:#71859a;font-size:7.5pt;margin-top:5px}.result{margin-top:12px;padding:10px 12px;border-left:4px solid #4d7ba5;background:#f4f7fa;border-radius:4px}.result.bad{border-color:#c54154;background:#fff2f4}.result.warn{border-color:#d69b38;background:#fff8e9}.page{page-break-before:always}.page h1{font-size:19pt;color:#173653}.coverage-score{display:inline-flex;align-items:baseline;gap:9px;background:#eef6ff;border:1px solid #cfe1f4;border-radius:8px;padding:10px 14px;margin:8px 0 14px}.coverage-score strong{font-size:22pt;color:#24659e}.coverage-score span{color:#5d748b}.risk{display:inline-block;padding:3px 7px;border-radius:11px;font-weight:700;font-size:8pt}.risk-low{background:#e8f7ef;color:#24724d}.risk-medium{background:#fff7df;color:#866200}.risk-high{background:#fff0e6;color:#a34f00}.risk-critical{background:#fff0f2;color:#a83245}.riskpage table{width:100%;border-collapse:collapse;margin-top:14px}.riskpage th{background:#183a5b;color:white;text-align:left;padding:8px;font-size:8pt}.riskpage td{border-bottom:1px solid #dde5ed;padding:8px}.reference ul{padding-left:20px}.reference li{margin:5px 0}.coverage table{width:100%;border-collapse:collapse}.coverage th{background:#183a5b;color:white;text-align:left;padding:8px;font-size:8pt}.coverage td{border-bottom:1px solid #dde5ed;padding:8px}.coverage-status{display:inline-block;padding:3px 7px;border-radius:12px;font-size:7.5pt;font-weight:700}.coverage-status.observed{background:#e8f7ef;color:#24724d}.coverage-status.missing{background:#fff1f2;color:#a83245}.insight-lead{font-size:11pt;background:#f4f7fa;border-left:4px solid #24659e;padding:12px 14px;border-radius:5px}.insight-grid{display:grid;grid-template-columns:1fr 1fr;gap:10px;margin-top:18px}.insight-grid div{border:1px solid #dce4ec;border-radius:8px;padding:12px}.insight-grid strong{display:block;font-size:16pt;color:#173653;word-break:break-word}.insight-grid span{display:block;margin-top:4px;color:#6f8294;font-size:8pt;text-transform:uppercase}
</style></head><body>
<div class='cover'><div class='brandrow'>{{(string.IsNullOrWhiteSpace(logo)?"":$"<img class='logo' src='{logo}'/>")}}<div class='brand'>QAFLOW RECORDER 1.0</div></div><h1>{{T("QA Test Execution Report","Reporte de Ejecución de Pruebas QA")}}</h1><h2>{{ReportLanguage.H(string.IsNullOrWhiteSpace(s.BaseName)?s.Name:s.BaseName)}} {{(string.IsNullOrWhiteSpace(part)?"":$"<span class='part'>· {part}</span>")}}</h2><span class='status'>{{status}}</span><div class='meta'><div><small>QA</small><b>{{ReportLanguage.H(s.Tester)}}</b></div><div><small>{{T("Test type","Tipo de prueba")}}</small><b>{{ReportLanguage.Template(s.Template,es)}}</b></div><div><small>{{T("Started","Inicio")}}</small><b>{{s.StartedAt:yyyy-MM-dd HH:mm:ss zzz}}</b></div><div><small>{{T("Finished","Fin")}}</small><b>{{s.FinishedAt:yyyy-MM-dd HH:mm:ss zzz}}</b></div><div><small>{{T("Duration","Duración")}}</small><b>{{s.DurationText}}</b></div><div><small>{{T("Part","Parte")}}</small><b>{{(s.IsMultipart?s.PartNumber.ToString():T("Single session","Sesión única"))}}</b></div></div></div>
<section class='summary'><h1>{{T("Execution summary","Resumen de ejecución")}}</h1><div class='summary-grid'><div class='kpi'><strong>{{steps.Count}}</strong><span>{{T("Interactions","Interacciones")}}</span></div><div class='kpi'><strong>{{reportTargetCount}}</strong><span>{{T("Applications / targets","Aplicaciones / objetivos")}}</span></div><div class='kpi'><strong>{{s.Annotations.Count}}</strong><span>{{T("QA observations","Observaciones QA")}}</span></div><div class='kpi'><strong>{{s.ErrorCount}}</strong><span>{{T("Findings","Hallazgos")}}</span></div></div><p>{{T("The interaction record below is ordered chronologically and pairs each captured user action with its visual evidence when available. Bug records are intentionally kept in a separate defect report.","El registro de interacciones se presenta en orden cronológico y vincula cada acción capturada con su evidencia visual cuando está disponible. Los bugs se mantienen intencionalmente en un reporte de defectos separado.")}}</p></section>
{{charterSection}}{{coverageSection}}{{riskSection}}{{referenceSection}}{{insightsSection}}{{qualitySection}}{{visualSection}}{{noSteps}}{{body}}</body></html>
""";
    }
}

public static class BugPdfHtmlGenerator
{
    public static string Generate(TestSession s, AppSettings cfg)
    {
        var es = ReportLanguage.IsEs(cfg.DocumentLanguage);
        string T(string en, string sp) => es ? sp : en;
        var logo = ReportLanguage.LogoDataUri();
        var part = ReportLanguage.PartLabel(s, es);
        var body = new StringBuilder();
        int n = 1;
        foreach (var bug in s.Bugs.OrderBy(b => b.Timestamp))
        {
            var steps = Deduplicator.Process(s.Events).Where(ReportLanguage.IsAction).Where(e => e.Timestamp <= bug.Timestamp).OrderBy(e => e.Timestamp).ToList();
            var image = File.Exists(bug.MarkedScreenshotPath) ? bug.MarkedScreenshotPath : bug.OriginalScreenshotPath;
            body.Append($"<section class='bug'><div class='bug-head'><div><div class='id'>BUG-{s.StartedAt:yyyyMMdd}-{n:000}</div><h1>{ReportLanguage.H(string.IsNullOrWhiteSpace(bug.Title) ? bug.Screen : bug.Title)}</h1></div><span class='severity'>{T("Severity","Severidad")}: {ReportLanguage.SeverityName(bug.Severity,es)}</span></div>");
            if (File.Exists(image)) body.Append($"<figure><img src='{ReportLanguage.ImageDataUri(image)}'/><figcaption>{T("Marked evidence","Evidencia marcada")} · {bug.Timestamp:HH:mm:ss}</figcaption></figure>");
            body.Append($"<div class='meta'><div><small>{T("Application / screen","Aplicación / pantalla")}</small><b>{ReportLanguage.H(bug.Screen)}</b></div><div><small>{T("Frequency","Frecuencia")}</small><b>{ReportLanguage.H(ReportLanguage.Frequency(bug.Frequency,es))}</b></div><div><small>{T("Result type","Tipo de resultado")}</small><b>{ReportLanguage.ResultName(bug.Result,es)}</b></div><div><small>{T("Reported at","Reportado a las")}</small><b>{bug.Timestamp:yyyy-MM-dd HH:mm:ss zzz}</b></div></div>");
            body.Append($"<h2>{T("Expected result","Resultado esperado")}</h2><div class='box'>{ReportLanguage.H(bug.Expected)}</div><h2>{T("Actual result","Resultado real")}</h2><div class='box bad'>{ReportLanguage.H(string.IsNullOrWhiteSpace(bug.Actual) ? bug.Expected : bug.Actual)}</div>");
            if (!string.IsNullOrWhiteSpace(bug.Note)) body.Append($"<h2>{T("Additional QA notes","Notas adicionales del QA")}</h2><div class='box'>{ReportLanguage.H(bug.Note)}</div>");
            if (bug.PossibleDuplicateBugId is not null) body.Append($"<h2>{T("Duplicate check","Revisión de duplicados")}</h2><div class='box'><b>{T("Possible related defect","Posible defecto relacionado")}:</b> {ReportLanguage.H(bug.PossibleDuplicateTitle)} · {Math.Round(bug.DuplicateSimilarity*100)}%</div>");
            body.Append($"<h2>{T("Steps to reproduce from the recorded session","Pasos para reproducir desde la sesión capturada")}</h2><ol>{string.Join("", steps.Select(e => $"<li><b>{e.Timestamp:HH:mm:ss}</b> — {ReportLanguage.Action(e,es)}</li>"))}</ol>");
            if (bug.Attachments.Count > 0) body.Append($"<h2>{T("Attachments","Adjuntos")}</h2><ul>{string.Join("", bug.Attachments.Select(a => $"<li>{ReportLanguage.H(Path.GetFileName(a))}</li>"))}</ul>");
            body.Append("</section>");
            n++;
        }

        return $$"""
<!doctype html><html><head><meta charset='utf-8'><title>QAFlow Recorder — {{T("QA Defect Report","Reporte de Defectos QA")}}</title><style>
@page{size:A4;margin:17mm 16mm}*{box-sizing:border-box}body{font-family:Segoe UI,Arial,sans-serif;color:#1a232d;font-size:9.5pt;line-height:1.45}.cover{height:255mm;display:flex;flex-direction:column;justify-content:center}.brandrow{display:flex;align-items:center;gap:12px}.logo{width:22mm;height:22mm;object-fit:contain}.brand{font-size:9pt;letter-spacing:.15em;color:#a7283b;font-weight:700}.cover h1{font-size:30pt;line-height:1.05;color:#7c1f2e;margin:16px 0 8px}.cover h2{font-size:16pt;color:#465766;font-weight:500;margin:0 0 20px}.cover .summary{margin-top:12px;color:#657788}.bug{page-break-before:always}.bug-head{display:flex;justify-content:space-between;gap:16px;border-bottom:3px solid #b72f43;padding-bottom:10px}.id{font-size:8pt;letter-spacing:.1em;color:#8a4a55;font-weight:700}.bug h1{font-size:17pt;margin:3px 0;color:#4a1720}.severity{background:#fff0f2;border:1px solid #e4b9c0;color:#9e2336;padding:7px 10px;border-radius:5px;height:max-content;font-weight:700}.meta{display:grid;grid-template-columns:1fr 1fr;gap:8px 12px;margin:14px 0}.meta div{border:1px solid #e0e5ea;border-radius:6px;padding:8px 10px}.meta small{display:block;color:#748494;text-transform:uppercase;font-size:7pt;letter-spacing:.05em}.meta b{display:block;margin-top:3px}.bug figure{margin:14px 0;page-break-inside:avoid}.bug img{display:block;max-width:176mm;max-height:155mm;object-fit:contain;margin:auto;border:2px solid #b72f43;border-radius:4px}.bug figcaption{text-align:center;font-size:7.5pt;color:#6a7c8c;margin-top:5px}.bug h2{font-size:11pt;margin:15px 0 6px;color:#374b60}.box{padding:10px 12px;background:#f4f7fa;border-left:4px solid #557b9e;border-radius:4px}.box.bad{background:#fff3f5;border-color:#b72f43}ol{padding-left:23px}li{margin:7px 0}.target{font-weight:700}
</style></head><body><div class='cover'><div class='brandrow'>{{(string.IsNullOrWhiteSpace(logo)?"":$"<img class='logo' src='{logo}'/>")}}<div class='brand'>QAFLOW RECORDER 1.0</div></div><h1>{{T("QA Defect Report","Reporte de Defectos QA")}}</h1><h2>{{ReportLanguage.H(string.IsNullOrWhiteSpace(s.BaseName)?s.Name:s.BaseName)}} {{(string.IsNullOrWhiteSpace(part)?"":$"· {part}")}}</h2><div class='summary'><b>QA:</b> {{ReportLanguage.H(s.Tester)}}<br><b>{{T("Execution","Ejecución")}}:</b> {{s.StartedAt:yyyy-MM-dd HH:mm}} — {{s.FinishedAt:yyyy-MM-dd HH:mm}}<br><b>{{T("Documented defects","Defectos documentados")}}:</b> {{s.Bugs.Count}}</div></div>{{body}}</body></html>
""";
    }
}

public sealed class PdfService
{
    public async Task CreateAsync(string html, string outputPath)
    {
        outputPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        if (File.Exists(outputPath)) File.Delete(outputPath);

        var tempHtml = Path.Combine(Path.GetTempPath(), $"qaflow-report-{Guid.NewGuid():N}.html");
        await File.WriteAllTextAsync(tempHtml, html, new UTF8Encoding(false));

        var web = new WebView2 { Width = 900, Height = 1200, Visibility = System.Windows.Visibility.Visible };
        var host = new System.Windows.Window
        {
            Width = 900, Height = 1200, Left = -32000, Top = -32000,
            ShowInTaskbar = false, ShowActivated = false, Opacity = 0.01,
            WindowStyle = System.Windows.WindowStyle.None, ResizeMode = System.Windows.ResizeMode.NoResize,
            Content = web
        };

        string stage = "initializing report renderer";
        host.Show();
        try
        {
            stage = "initializing WebView2";
            await web.EnsureCoreWebView2Async();

            stage = "loading report content";
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Done(object? sender, CoreWebView2NavigationCompletedEventArgs e) => tcs.TrySetResult(e.IsSuccess);
            web.CoreWebView2.NavigationCompleted += Done;
            web.CoreWebView2.Navigate(new Uri(tempHtml).AbsoluteUri);
            var ok = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(45));
            web.CoreWebView2.NavigationCompleted -= Done;
            if (!ok) throw new InvalidOperationException("The report renderer could not load the generated document.");

            // Give embedded Base64 evidence images time to finish decoding before printing.
            stage = "waiting for report images";
            await web.CoreWebView2.ExecuteScriptAsync("new Promise(resolve => { const imgs=[...document.images]; if(!imgs.length) return resolve(true); Promise.all(imgs.map(i => i.complete ? Promise.resolve() : new Promise(r => { i.addEventListener('load',r,{once:true}); i.addEventListener('error',r,{once:true}); }))).then(()=>resolve(true)); })");
            await Task.Delay(150);

            stage = "printing PDF";
            var ps = web.CoreWebView2.Environment.CreatePrintSettings();
            ps.ShouldPrintBackgrounds = true;
            ps.ShouldPrintHeaderAndFooter = false;
            ps.Orientation = CoreWebView2PrintOrientation.Portrait;
            var printed = await web.CoreWebView2.PrintToPdfAsync(outputPath, ps);
            if (!printed || !File.Exists(outputPath) || new FileInfo(outputPath).Length < 1000)
                throw new IOException("The PDF renderer did not create a valid output file.");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"QAFlow could not export the PDF while {stage}. {ex.Message}", ex);
        }
        finally
        {
            try { web.Dispose(); } catch { }
            try { host.Close(); } catch { }
            try { if (File.Exists(tempHtml)) File.Delete(tempHtml); } catch { }
        }
    }
}

public sealed class ExportService(AppSettings settings)
{
    public async Task<string> ExportSeriesAsync(IReadOnlyList<TestSession> sessions, bool record, bool bug, bool pdf, bool package)
    {
        if (sessions.Count == 0) throw new InvalidOperationException("No session data is available for export.");
        Directory.CreateDirectory(settings.ExportFolder);
        var ordered = sessions.OrderBy(s => s.PartNumber).ToList();
        var baseName = string.IsNullOrWhiteSpace(ordered[0].BaseName) ? ordered[0].Name : ordered[0].BaseName;
        var root = Path.Combine(settings.ExportFolder, $"{Safe(baseName)}-{DateTime.Now:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(root);
        var multipart = ordered.Count > 1 || ordered.Any(s => s.IsMultipart);

        foreach (var s in ordered)
        {
            var partSuffix = multipart ? $"-part-{s.PartNumber:00}" : "";
            var dir = multipart ? Path.Combine(root, $"Part {s.PartNumber:00}") : root;
            Directory.CreateDirectory(dir);

            if (record)
            {
                await File.WriteAllTextAsync(Path.Combine(dir, $"session-record{partSuffix}.md"), SessionRecordMarkdown(s, settings.DocumentLanguage));
                await File.WriteAllTextAsync(Path.Combine(dir, $"session-record{partSuffix}.json"), JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
            }
            if (bug && s.Bugs.Count > 0)
                await new PdfService().CreateAsync(BugPdfHtmlGenerator.Generate(s, settings), Path.Combine(dir, $"bug-report-{Safe(baseName)}{partSuffix}.pdf"));
            if (pdf)
                await new PdfService().CreateAsync(QaLogHtmlGenerator.Generate(s, settings), Path.Combine(dir, $"qa-report-{Safe(baseName)}{partSuffix}.pdf"));

            var evidenceDir = Path.Combine(dir, "evidence");
            foreach (var sh in s.Screenshots.Where(x => File.Exists(x.FilePath) && x.Kind is not (ScreenshotKind.BugOriginal or ScreenshotKind.BugMarked)))
            {
                Directory.CreateDirectory(evidenceDir);
                File.Copy(sh.FilePath, Path.Combine(evidenceDir, Path.GetFileName(sh.FilePath)), true);
            }
        }

        if (package)
        {
            var zip = root + ".zip";
            if (File.Exists(zip)) File.Delete(zip);
            ZipFile.CreateFromDirectory(root, zip, CompressionLevel.Optimal, false);
            return zip;
        }
        return root;
    }

    private static string SessionRecordMarkdown(TestSession s, string language)
    {
        var es = ReportLanguage.IsEs(language);
        string T(string en, string sp) => es ? sp : en;
        var sb = new StringBuilder();
        sb.AppendLine($"# {T("QA Session Record","Registro de Sesión QA")} — {s.DisplayName}");
        sb.AppendLine();
        sb.AppendLine($"- **QA:** {s.Tester}");
        sb.AppendLine($"- **{T("Test type","Tipo de prueba")}:** {ReportLanguage.Template(s.Template, es)}");
        sb.AppendLine($"- **{T("Started","Inicio")}:** {s.StartedAt:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine($"- **{T("Duration","Duración")}:** {s.DurationText}");
        if (s.IsMultipart) sb.AppendLine($"- **{T("Part","Parte")}:** {s.PartNumber}");
        sb.AppendLine();
        sb.AppendLine($"## {T("Recorded interactions","Interacciones registradas")}");
        var i = 1;
        foreach (var e in Deduplicator.Process(s.Events).Where(ReportLanguage.IsAction).OrderBy(e => e.Timestamp))
        {
            if (e.IsCheckpoint) sb.AppendLine($"\n### {T("Checkpoint","Hito")}: {SmartNarrativeService.CheckpointTitle(e, es)}");
            sb.AppendLine($"{i++}. {e.Timestamp:HH:mm:ss} — {SmartNarrativeService.Describe(e, es)}{(e.IsImportant ? (es ? " **[PASO CLAVE]**" : " **[KEY STEP]**") : "")}");
        }
        return sb.ToString();
    }

    private static string Safe(string value) => string.Concat((value ?? "").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}
