using QAFlowRecorder.Models;

namespace QAFlowRecorder.Services;

public static class Deduplicator
{
    public static List<QaEvent> Process(IEnumerable<QaEvent> source)
    {
        var events = source.OrderBy(e => e.Timestamp).Select(Clone).ToList();
        var output = new List<QaEvent>();

        for (var i = 0; i < events.Count; i++)
        {
            var e = events[i];

            if (e.Kind == EventKind.Input)
            {
                var last = e;
                var j = i + 1;
                while (j < events.Count && events[j].Timestamp - e.Timestamp < TimeSpan.FromSeconds(60))
                {
                    if (events[j].Kind == EventKind.Navigation) break;
                    if (events[j].Kind == EventKind.Input && events[j].Selector == e.Selector) { last = events[j]; i = j; }
                    j++;
                }
                output.Add(last);
                continue;
            }

            if (e.Kind == EventKind.Click)
            {
                if (output.LastOrDefault() is { Kind: EventKind.Click } prev && prev.Selector == e.Selector && e.Timestamp - prev.Timestamp < TimeSpan.FromSeconds(5))
                    continue;
            }

            if (e.Kind == EventKind.Scroll)
            {
                var last = e;
                while (i + 1 < events.Count && events[i + 1].Kind == EventKind.Scroll && events[i + 1].Timestamp - last.Timestamp < TimeSpan.FromSeconds(10))
                { last = events[++i]; }
                e = last;
            }

            if (output.Count > 0)
            {
                var gap = e.Timestamp - output[^1].Timestamp;
                if (gap > TimeSpan.FromSeconds(10))
                    output.Add(new QaEvent { Kind = EventKind.Wait, Timestamp = output[^1].Timestamp.AddMilliseconds(1), Url = output[^1].Url, Description = $"Pausa de {Math.Round(gap.TotalSeconds)}s" });
            }
            output.Add(e);
        }

        // A→B→A in <30s: keep B only if it contains meaningful actions.
        for (var i = 0; i + 2 < output.Count; i++)
        {
            if (output[i].Kind == EventKind.Navigation && output[i + 1].Kind == EventKind.Navigation && output[i + 2].Kind == EventKind.Navigation &&
                output[i].Url == output[i + 2].Url && output[i + 2].Timestamp - output[i].Timestamp < TimeSpan.FromSeconds(30))
            {
                var bUrl = output[i + 1].Url;
                var hasActions = output.Skip(i + 2).TakeWhile(x => x.Kind != EventKind.Navigation).Any(x => x.Url == bUrl && x.Kind is EventKind.Click or EventKind.Input or EventKind.Select or EventKind.Submit);
                if (!hasActions) output.RemoveAt(i + 1);
            }
        }
        return output;
    }

    private static QaEvent Clone(QaEvent e) => new()
    {
        Id=e.Id, Timestamp=e.Timestamp, Kind=e.Kind, Url=e.Url, Description=e.Description, Selector=e.Selector, Value=e.Value,
        Method=e.Method, StatusCode=e.StatusCode, X=e.X, Y=e.Y, Stack=e.Stack, Severity=e.Severity, IsAnnotationPoint=e.IsAnnotationPoint, IsExploration=e.IsExploration
    };
}
