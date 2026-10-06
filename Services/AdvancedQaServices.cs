using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QAFlowRecorder.Models;

namespace QAFlowRecorder.Services;

public static class VisualBaselineService
{
    private static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QAFlow Recorder 1.0", "visual-baselines");
    private static readonly string IndexPath = Path.Combine(Root, "index.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private sealed class BaselineEntry
    {
        public string Target { get; set; } = "";
        public string FilePath { get; set; } = "";
        public DateTimeOffset UpdatedAt { get; set; }
    }

    public static async Task<int> SaveFromSessionAsync(TestSession session)
    {
        Directory.CreateDirectory(Root);
        var index = await LoadIndexAsync();
        var actions = session.Events.Where(ReportLanguage.IsAction).OrderBy(e => e.Timestamp).ToList();
        var saved = 0;
        foreach (var group in actions.GroupBy(ReportLanguage.Target, StringComparer.OrdinalIgnoreCase))
        {
            var target = group.Key?.Trim();
            if (string.IsNullOrWhiteSpace(target)) continue;
            var ev = group.Last();
            var shot = session.Screenshots.Where(x => File.Exists(x.FilePath) && x.Kind is not (ScreenshotKind.BugOriginal or ScreenshotKind.BugMarked))
                .OrderBy(x => Math.Abs((x.Timestamp - ev.Timestamp).TotalSeconds)).FirstOrDefault();
            if (shot is null) continue;
            var file = Path.Combine(Root, $"{Safe(target)}-{StableHash(target):x8}.png");
            File.Copy(shot.FilePath, file, true);
            index.RemoveAll(x => string.Equals(x.Target, target, StringComparison.OrdinalIgnoreCase));
            index.Add(new BaselineEntry { Target = target, FilePath = file, UpdatedAt = DateTimeOffset.Now });
            saved++;
        }
        await File.WriteAllTextAsync(IndexPath, JsonSerializer.Serialize(index, JsonOptions));
        return saved;
    }

    public static async Task<VisualComparisonResult> AnalyzeAsync(TestSession session)
    {
        var index = await LoadIndexAsync();
        var result = new VisualComparisonResult();
        var actions = session.Events.Where(ReportLanguage.IsAction).OrderBy(e => e.Timestamp).ToList();
        foreach (var group in actions.GroupBy(ReportLanguage.Target, StringComparer.OrdinalIgnoreCase))
        {
            var target = group.Key?.Trim();
            if (string.IsNullOrWhiteSpace(target)) continue;
            var ev = group.Last();
            var shot = session.Screenshots.Where(x => File.Exists(x.FilePath) && x.Kind is not (ScreenshotKind.BugOriginal or ScreenshotKind.BugMarked))
                .OrderBy(x => Math.Abs((x.Timestamp - ev.Timestamp).TotalSeconds)).FirstOrDefault();
            if (shot is null) continue;
            var baseline = index.FirstOrDefault(x => string.Equals(x.Target, target, StringComparison.OrdinalIgnoreCase) && File.Exists(x.FilePath));
            var item = new VisualComparisonItem { Target = target, CurrentEvidencePath = shot.FilePath, BaselinePath = baseline?.FilePath ?? "" };
            if (baseline is null) item.Status = "No baseline";
            else
            {
                item.DifferencePercent = CompareImages(baseline.FilePath, shot.FilePath);
                item.Status = item.DifferencePercent >= 12 ? "Changed" : item.DifferencePercent >= 4 ? "Minor change" : "Stable";
            }
            result.Items.Add(item);
        }
        return result;
    }

    private static async Task<List<BaselineEntry>> LoadIndexAsync()
    {
        try { if (File.Exists(IndexPath)) return JsonSerializer.Deserialize<List<BaselineEntry>>(await File.ReadAllTextAsync(IndexPath)) ?? []; } catch { }
        return [];
    }

    private static double CompareImages(string a, string b)
    {
        try
        {
            var pa = LoadPreview(a, 96, 60); var pb = LoadPreview(b, 96, 60);
            var len = Math.Min(pa.Length, pb.Length); if (len == 0) return 100;
            long diff = 0; for (var i=0;i<len;i++) diff += Math.Abs(pa[i]-pb[i]);
            return Math.Min(100, diff / (double)(len * 255) * 100.0 * 2.2);
        }
        catch { return 100; }
    }

    private static byte[] LoadPreview(string path, int w, int h)
    {
        using var fs = File.OpenRead(path);
        var dec = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var src = dec.Frames[0];
        var scale = Math.Min(w/(double)src.PixelWidth, h/(double)src.PixelHeight);
        var tb = new TransformedBitmap(src, new ScaleTransform(scale, scale)); tb.Freeze();
        var converted = new FormatConvertedBitmap(tb, PixelFormats.Gray8, null, 0); converted.Freeze();
        var bytes = new byte[converted.PixelWidth * converted.PixelHeight]; converted.CopyPixels(bytes, converted.PixelWidth, 0); return bytes;
    }
    private static uint StableHash(string value){unchecked{uint h=2166136261;foreach(var c in value.ToLowerInvariant()){h^=c;h*=16777619;}return h;}}
    private static string Safe(string value){var invalid=Path.GetInvalidFileNameChars();var s=new string(value.Select(c=>invalid.Contains(c)?'_':c).ToArray()).Trim();return s.Length>40?s[..40]:s;}
}

public static class EvidenceQualityService
{
    public static EvidenceQualityResult Analyze(TestSession s)
    {
        var r = new EvidenceQualityResult();
        var actions = s.Events.Where(ReportLanguage.IsAction).Where(e=>!e.ExcludeFromReport).OrderBy(e=>e.Timestamp).ToList();
        var missingEvidence = actions.Count(e => ReportLanguage.EvidenceFor(s,e) is null);
        if (missingEvidence > 0) r.Issues.Add(new EvidenceQualityIssue{Code="missing-evidence",Severity=missingEvidence>3?Severity.High:Severity.Medium,MessageEnglish=$"{missingEvidence} report step(s) have no nearby visual evidence.",MessageSpanish=$"{missingEvidence} paso(s) del reporte no tienen evidencia visual cercana."});
        var duplicates = CountDuplicateEvidence(s.Screenshots.Where(x=>File.Exists(x.FilePath) && x.Kind is not (ScreenshotKind.BugOriginal or ScreenshotKind.BugMarked)).Select(x=>x.FilePath).ToList());
        if(duplicates>0) r.Issues.Add(new EvidenceQualityIssue{Code="duplicate-evidence",Severity=Severity.Low,MessageEnglish=$"{duplicates} likely duplicate screenshot(s) detected.",MessageSpanish=$"Se detectaron {duplicates} captura(s) probablemente duplicada(s)."});
        var bugWithoutMarked = s.Bugs.Count(b=>string.IsNullOrWhiteSpace(b.MarkedScreenshotPath)||!File.Exists(b.MarkedScreenshotPath));
        if(bugWithoutMarked>0) r.Issues.Add(new EvidenceQualityIssue{Code="bug-evidence",Severity=Severity.High,MessageEnglish=$"{bugWithoutMarked} bug(s) have no marked evidence.",MessageSpanish=$"{bugWithoutMarked} bug(s) no tienen evidencia marcada."});
        var weakBugs=s.Bugs.Count(b=>string.IsNullOrWhiteSpace(b.Expected)||string.IsNullOrWhiteSpace(b.Actual));
        if(weakBugs>0) r.Issues.Add(new EvidenceQualityIssue{Code="bug-description",Severity=Severity.Medium,MessageEnglish=$"{weakBugs} bug(s) are missing expected or actual result details.",MessageSpanish=$"A {weakBugs} bug(s) les falta detalle de resultado esperado o real."});
        if(actions.Count==0) r.Issues.Add(new EvidenceQualityIssue{Code="no-actions",Severity=Severity.High,MessageEnglish="No reportable user interactions were captured.",MessageSpanish="No se capturaron interacciones de usuario reportables."});
        var penalty=r.Issues.Sum(x=>x.Severity switch{Severity.Critical=>30,Severity.High=>20,Severity.Medium=>10,_=>4});
        r.ReadinessPercent=Math.Max(0,100-penalty); return r;
    }
    private static int CountDuplicateEvidence(List<string> files)
    {
        var seen=new HashSet<string>();var d=0;foreach(var f in files){try{var info=new FileInfo(f);var key=$"{info.Length}:{QuickHash(f)}";if(!seen.Add(key))d++;}catch{}}return d;
    }
    private static uint QuickHash(string file){unchecked{uint h=2166136261;using var fs=File.OpenRead(file);var buffer=new byte[4096];var n=fs.Read(buffer,0,buffer.Length);for(var i=0;i<n;i++){h^=buffer[i];h*=16777619;}return h;}}
}
