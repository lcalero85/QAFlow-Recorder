using QAFlowRecorder.Models;

namespace QAFlowRecorder.Services;

public static class SmartCheckpointService
{
    public static void Enrich(IReadOnlyList<QaEvent> existing, QaEvent current)
    {
        var previous = existing.LastOrDefault();
        var target = PreferredTarget(current);
        if (previous is null)
        {
            Mark(current, "SessionStart", target);
            return;
        }

        var previousTarget = PreferredTarget(previous);
        var applicationChanged = !string.Equals(previous.ApplicationName, current.ApplicationName, StringComparison.OrdinalIgnoreCase)
                                 && !string.IsNullOrWhiteSpace(current.ApplicationName);
        var windowChanged = !string.Equals(previousTarget, target, StringComparison.OrdinalIgnoreCase)
                            && !string.IsNullOrWhiteSpace(target);
        var longPause = current.Timestamp - previous.Timestamp >= TimeSpan.FromSeconds(20);

        if (current.Kind is EventKind.ConsoleError or EventKind.HttpError or EventKind.ResourceError)
            Mark(current, "Finding", target);
        else if (current.Kind == EventKind.Dialog)
            Mark(current, "Dialog", target);
        else if (current.Kind == EventKind.Submit)
            Mark(current, "Submit", target);
        else if (applicationChanged)
            Mark(current, "ApplicationChange", target);
        else if (windowChanged)
            Mark(current, "WindowChange", target);
        else if (longPause)
            Mark(current, "LongPause", target);
    }

    private static void Mark(QaEvent e, string kind, string target)
    {
        e.IsCheckpoint = true;
        e.CheckpointKind = kind;
        e.CheckpointTitle = string.IsNullOrWhiteSpace(target) ? "Desktop activity" : target;
    }

    private static string PreferredTarget(QaEvent e)
    {
        if (!string.IsNullOrWhiteSpace(e.WindowTitle)) return e.WindowTitle.Trim();
        if (!string.IsNullOrWhiteSpace(e.TargetTitle)) return e.TargetTitle.Trim();
        if (!string.IsNullOrWhiteSpace(e.ApplicationName)) return e.ApplicationName.Trim();
        return "";
    }
}
