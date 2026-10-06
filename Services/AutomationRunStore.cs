using System.Text.Json;
using QAFlowRecorder.Models;

namespace QAFlowRecorder.Services;

public sealed class AutomationRunStore
{
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };
    public string Root { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QAFlowRecorder", "automation-runs");

    public AutomationRunStore() => Directory.CreateDirectory(Root);

    public string CreateRunFolder(AutomationTestRun run)
    {
        var safe = string.Concat((run.TestCaseKey + "-" + run.TestCaseName).Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var folder = Path.Combine(Root, $"{DateTime.Now:yyyyMMdd-HHmmss}-{safe}-{run.Id:N}");
        Directory.CreateDirectory(folder);
        Directory.CreateDirectory(Path.Combine(folder, "evidence"));
        run.RunFolder = folder;
        return folder;
    }

    public async Task SaveAsync(AutomationTestRun run)
    {
        if (string.IsNullOrWhiteSpace(run.RunFolder)) CreateRunFolder(run);
        await File.WriteAllTextAsync(Path.Combine(run.RunFolder, "run.json"), JsonSerializer.Serialize(run, _json));
    }

    public async Task<List<AutomationTestRun>> LoadAllAsync()
    {
        var result = new List<AutomationTestRun>();
        if (!Directory.Exists(Root)) return result;
        foreach (var file in Directory.EnumerateFiles(Root, "run.json", SearchOption.AllDirectories))
        {
            try
            {
                var run = JsonSerializer.Deserialize<AutomationTestRun>(await File.ReadAllTextAsync(file), _json);
                if (run is not null) result.Add(run);
            }
            catch { }
        }
        return result.OrderByDescending(x => x.StartedAt).ToList();
    }
}
