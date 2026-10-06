using System.Text.Json;
using QAFlowRecorder.Models;

namespace QAFlowRecorder.Services;

public static class RiskAssessmentService
{
    public static RiskAssessment Analyze(TestSession session)
    {
        var actions = session.Events.Where(ReportLanguage.IsAction).OrderBy(e => e.Timestamp).ToList();
        var groups = actions.GroupBy(ReportLanguage.Target, StringComparer.OrdinalIgnoreCase);
        var result = new RiskAssessment();
        foreach (var g in groups)
        {
            var list = g.ToList();
            var repeated = list.GroupBy(e => $"{e.Kind}|{e.TargetTitle}|{e.WindowTitle}", StringComparer.OrdinalIgnoreCase)
                .Sum(x => Math.Max(0, x.Count() - 1));
            var returns = CountReturns(actions, g.Key);
            var waits = session.Events.Count(e => e.Kind == EventKind.Wait &&
                string.Equals(ReportLanguage.Target(e), g.Key, StringComparison.OrdinalIgnoreCase));
            var findings = session.Bugs.Count(b => ContainsTarget(b.Screen, g.Key)) +
                           session.Annotations.Count(a => (a.Result == AnnotationResult.Error || a.Result == AnnotationResult.Doubt) &&
                               list.Any(e => e.Id == a.EventId));
            var score = Math.Min(100, Math.Min(35, list.Count * 2) + repeated * 7 + returns * 8 + waits * 10 + findings * 22);
            var level = score >= 70 ? RiskLevel.Critical : score >= 45 ? RiskLevel.High : score >= 20 ? RiskLevel.Medium : RiskLevel.Low;
            var reasonsEn = new List<string>(); var reasonsEs = new List<string>();
            if (findings > 0) { reasonsEn.Add($"{findings} finding(s)"); reasonsEs.Add($"{findings} hallazgo(s)"); }
            if (repeated > 0) { reasonsEn.Add($"{repeated} repeated action(s)"); reasonsEs.Add($"{repeated} acción(es) repetida(s)"); }
            if (returns > 0) { reasonsEn.Add($"{returns} return visit(s)"); reasonsEs.Add($"{returns} retorno(s)"); }
            if (waits > 0) { reasonsEn.Add($"{waits} significant wait(s)"); reasonsEs.Add($"{waits} espera(s) significativa(s)"); }
            if (reasonsEn.Count == 0) { reasonsEn.Add("normal interaction pattern"); reasonsEs.Add("patrón de interacción normal"); }
            result.Areas.Add(new RiskArea { Target=g.Key, Score=score, Level=level, Interactions=list.Count, RepeatedActions=repeated, Returns=returns, LongWaits=waits, Findings=findings, RationaleEnglish=string.Join(", ", reasonsEn), RationaleSpanish=string.Join(", ", reasonsEs) });
        }
        result.Areas = result.Areas.OrderByDescending(a => a.Score).ThenBy(a => a.Target).ToList();
        return result;
    }

    private static int CountReturns(List<QaEvent> events, string target)
    {
        var compact = new List<string>();
        string last = "";
        foreach (var e in events)
        {
            var current = ReportLanguage.Target(e);
            if (string.IsNullOrWhiteSpace(current)) continue;
            if (compact.Count == 0 || !string.Equals(current, last, StringComparison.OrdinalIgnoreCase))
            {
                compact.Add(current);
                last = current;
            }
        }
        var seen = false; var left = false; var returns = 0;
        foreach (var current in compact)
        {
            if (string.Equals(current, target, StringComparison.OrdinalIgnoreCase))
            {
                if (seen && left) returns++;
                seen = true; left = false;
            }
            else if (seen) left = true;
        }
        return returns;
    }
    private static bool ContainsTarget(string a,string b) => !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) && (a.Contains(b,StringComparison.OrdinalIgnoreCase) || b.Contains(a,StringComparison.OrdinalIgnoreCase));
}

public sealed class ReusableFlowLibraryService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _file;
    public ReusableFlowLibraryService()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QAFlow Recorder 1.0");
        Directory.CreateDirectory(root); _file = Path.Combine(root, "reusable-flows.json");
    }
    public async Task<List<ReusableFlow>> LoadAsync()
    {
        try { if (File.Exists(_file)) return JsonSerializer.Deserialize<List<ReusableFlow>>(await File.ReadAllTextAsync(_file)) ?? []; } catch { }
        return [];
    }
    public async Task<ReusableFlow> SaveFromSessionAsync(TestSession session)
    {
        var flows = await LoadAsync();
        var baseName = string.IsNullOrWhiteSpace(session.BaseName) ? session.Name : session.BaseName;
        var flow = new ReusableFlow { Name=baseName, TestType=session.Template, CreatedAt=DateTimeOffset.Now, UpdatedAt=DateTimeOffset.Now,
            Steps = session.Events.Where(ReportLanguage.IsAction).OrderBy(e=>e.Timestamp).Select((e,i)=>new ReusableFlowStep { Order=i+1, Kind=e.Kind, Target=ReportLanguage.Target(e), Description=SmartNarrativeService.Describe(e,false), ControlName=e.ControlName, ControlType=e.ControlType, AutomationId=e.AutomationId, Important=e.IsImportant }).ToList() };
        var existing = flows.FirstOrDefault(f => string.Equals(f.Name, flow.Name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) { flow.Id=existing.Id; flow.CreatedAt=existing.CreatedAt; flows.Remove(existing); }
        flows.Add(flow); await SaveAsync(flows); return flow;
    }
    public async Task DeleteAsync(Guid id) { var flows=await LoadAsync(); flows.RemoveAll(f=>f.Id==id); await SaveAsync(flows); }
    private Task SaveAsync(List<ReusableFlow> flows) => File.WriteAllTextAsync(_file, JsonSerializer.Serialize(flows.OrderBy(f=>f.Name).ToList(), JsonOptions));
}

public static class ReferenceFlowComparisonService
{
    public static ReferenceFlowComparison Compare(TestSession session)
    {
        var expected=session.ReferenceFlowSteps.OrderBy(s=>s.Order).ToList();
        var actual=session.Events.Where(ReportLanguage.IsAction).OrderBy(e=>e.Timestamp).ToList();
        var cmp=new ReferenceFlowComparison{ExpectedSteps=expected.Count};
        var cursor=0;
        foreach(var step in expected)
        {
            var found=-1;
            for(var i=cursor;i<actual.Count;i++) if(IsMatch(step,actual[i])) { found=i; break; }
            if(found>=0){cmp.MatchedSteps++;cursor=found+1;} else cmp.MissingDescriptions.Add(step.Description);
        }
        cmp.MissingSteps=cmp.MissingDescriptions.Count; cmp.ExtraSteps=Math.Max(0,actual.Count-cmp.MatchedSteps); return cmp;
    }
    private static bool IsMatch(ReusableFlowStep expected,QaEvent actual)
    {
        if(expected.Kind!=actual.Kind) return false;
        var target=ReportLanguage.Target(actual);
        var targetMatches=string.IsNullOrWhiteSpace(expected.Target) || string.IsNullOrWhiteSpace(target) || expected.Target.Contains(target,StringComparison.OrdinalIgnoreCase) || target.Contains(expected.Target,StringComparison.OrdinalIgnoreCase);
        if(!targetMatches) return false;

        if(!string.IsNullOrWhiteSpace(expected.AutomationId) && !string.IsNullOrWhiteSpace(actual.AutomationId))
            return string.Equals(expected.AutomationId,actual.AutomationId,StringComparison.OrdinalIgnoreCase);
        if(!string.IsNullOrWhiteSpace(expected.ControlName) && !string.IsNullOrWhiteSpace(actual.ControlName))
            return string.Equals(expected.ControlName,actual.ControlName,StringComparison.OrdinalIgnoreCase) &&
                   (string.IsNullOrWhiteSpace(expected.ControlType) || string.IsNullOrWhiteSpace(actual.ControlType) || string.Equals(expected.ControlType,actual.ControlType,StringComparison.OrdinalIgnoreCase));
        return true;
    }
}

public sealed record DuplicateBugMatch(BugRecord Bug, TestSession Session, double Similarity);

public static class DuplicateBugService
{
    public static async Task<DuplicateBugMatch?> FindBestAsync(BugRecord candidate)
    {
        var sessions=await new SessionStore().LoadAllAsync();
        DuplicateBugMatch? best=null;
        foreach(var s in sessions)
        foreach(var bug in s.Bugs)
        {
            var sim=Similarity(candidate,bug);
            if(best is null || sim>best.Similarity) best=new DuplicateBugMatch(bug,s,sim);
        }
        return best is { Similarity: >= 0.62 } ? best : null;
    }
    private static double Similarity(BugRecord a,BugRecord b)
    {
        var textA=$"{a.Screen} {a.Title} {a.Expected} {a.Actual} {a.Note}";
        var textB=$"{b.Screen} {b.Title} {b.Expected} {b.Actual} {b.Note}";
        var ta=Tokens(textA); var tb=Tokens(textB);
        var union=ta.Union(tb).Count(); var j=union==0?0:ta.Intersect(tb).Count()/(double)union;
        var screen=!string.IsNullOrWhiteSpace(a.Screen)&&!string.IsNullOrWhiteSpace(b.Screen)&&(a.Screen.Contains(b.Screen,StringComparison.OrdinalIgnoreCase)||b.Screen.Contains(a.Screen,StringComparison.OrdinalIgnoreCase))?1:0;
        var sev=a.Severity==b.Severity?1:0;
        return Math.Min(1, j*.72 + screen*.20 + sev*.08);
    }
    private static HashSet<string> Tokens(string value) => value.ToLowerInvariant().Split(new[]{' ','\t','\r','\n','.',',',';',':','/','\\','-','_','(',')','[',']'},StringSplitOptions.RemoveEmptyEntries).Where(x=>x.Length>2).ToHashSet();
}
