using QAFlowRecorder.Models;

namespace QAFlowRecorder.Services;

/// <summary>
/// Local-only visual baselines for hybrid web automation. Baselines are keyed by
/// the stable automation test-step Guid so they remain independent from Recorder baselines.
/// </summary>
public static class AutomationVisualBaselineService
{
    private static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "QAFlowRecorder", "automation-visual-baselines");

    public static string GetBaselinePath(Guid stepId) => Path.Combine(Root, $"{stepId:N}.png");

    public static bool HasBaseline(Guid stepId) => File.Exists(GetBaselinePath(stepId));

    public static string SaveBaseline(Guid stepId, string evidencePath)
    {
        if (string.IsNullOrWhiteSpace(evidencePath) || !File.Exists(evidencePath))
            throw new FileNotFoundException("The evidence image used as visual baseline was not found.", evidencePath);
        Directory.CreateDirectory(Root);
        var target = GetBaselinePath(stepId);
        File.Copy(evidencePath, target, true);
        return target;
    }

    public static double Compare(Guid stepId, string currentEvidencePath)
    {
        var baseline = GetBaselinePath(stepId);
        if (!File.Exists(baseline))
            throw new InvalidOperationException("No visual baseline exists for this automation step. Create one from a previous run before enabling VisualBaselineMatch.");
        if (string.IsNullOrWhiteSpace(currentEvidencePath) || !File.Exists(currentEvidencePath))
            throw new FileNotFoundException("Current visual evidence was not found.", currentEvidencePath);
        var difference = VisualBaselineService.CompareEvidenceFiles(baseline, currentEvidencePath);
        return Math.Max(0, Math.Min(100, 100.0 - difference));
    }
}
