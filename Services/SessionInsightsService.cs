using QAFlowRecorder.Models;

namespace QAFlowRecorder.Services;

public sealed record SessionInsights(
    int Interactions,
    int Applications,
    int Checkpoints,
    int Bugs,
    int RepeatedActions,
    int LongPauses,
    int ReturnVisits,
    string MostActiveTarget,
    int MostActiveTargetInteractions,
    string SummaryEnglish,
    string SummarySpanish);

public static class SessionInsightsService
{
    public static SessionInsights Analyze(TestSession session)
    {
        var events = Deduplicator.Process(session.Events)
            .Where(ReportLanguage.IsAction)
            .Where(e => !e.ExcludeFromReport)
            .OrderBy(e => e.Timestamp)
            .ToList();
        string Target(QaEvent e) => !string.IsNullOrWhiteSpace(e.TargetTitle) ? e.TargetTitle : !string.IsNullOrWhiteSpace(e.WindowTitle) ? e.WindowTitle : !string.IsNullOrWhiteSpace(e.ApplicationName) ? e.ApplicationName : "Desktop";
        var groups = events.GroupBy(Target, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).ToList();
        var repeats = 0;
        var pauses = 0;
        var returns = 0;
        var seenTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? previousTarget = null;
        for (var i=0;i<events.Count;i++)
        {
            var current = events[i];
            var target = Target(current);
            if (i > 0)
            {
                var previous = events[i-1];
                if (current.Timestamp - previous.Timestamp > TimeSpan.FromSeconds(10)) pauses++;
                if (current.Kind == previous.Kind && string.Equals(target, Target(previous), StringComparison.OrdinalIgnoreCase) && current.Timestamp - previous.Timestamp < TimeSpan.FromSeconds(5)) repeats++;
                if (!string.Equals(target, previousTarget, StringComparison.OrdinalIgnoreCase) && seenTargets.Contains(target)) returns++;
            }
            seenTargets.Add(target);
            previousTarget = target;
        }
        var most = groups.FirstOrDefault();
        var mostTarget = most?.Key ?? "—";
        var mostCount = most?.Count() ?? 0;
        var apps = events.Select(e => e.ApplicationName).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var checkpoints = events.Count(e => e.IsCheckpoint);
        var en = $"{events.Count} interactions across {apps} application(s), {checkpoints} checkpoint(s), {session.Bugs.Count} documented bug(s), {repeats} repeated action(s), {pauses} significant pause(s), and {returns} return visit(s). Most activity occurred in {mostTarget}.";
        var es = $"{events.Count} interacciones en {apps} aplicación(es), {checkpoints} hito(s), {session.Bugs.Count} bug(s) documentado(s), {repeats} acción(es) repetida(s), {pauses} pausa(s) significativa(s) y {returns} retorno(s) a pantallas previas. La mayor actividad ocurrió en {mostTarget}.";
        return new SessionInsights(events.Count, apps, checkpoints, session.Bugs.Count, repeats, pauses, returns, mostTarget, mostCount, en, es);
    }
}
