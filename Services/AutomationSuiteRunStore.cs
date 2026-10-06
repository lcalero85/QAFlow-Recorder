using System.Text.Json;
using QAFlowRecorder.Models;

namespace QAFlowRecorder.Services;

public sealed class AutomationSuiteRunStore
{
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };
    public string Root { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QAFlowRecorder", "automation-suite-runs");
    public AutomationSuiteRunStore() => Directory.CreateDirectory(Root);

    public string CreateRunFolder(AutomationSuiteRun run)
    {
        var safe = string.Concat(run.SuiteName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var folder = Path.Combine(Root, $"{DateTime.Now:yyyyMMdd-HHmmss}-{safe}-{run.Id:N}");
        Directory.CreateDirectory(folder);
        run.RunFolder = folder;
        return folder;
    }

    public async Task SaveAsync(AutomationSuiteRun run)
    {
        if (string.IsNullOrWhiteSpace(run.RunFolder)) CreateRunFolder(run);
        await File.WriteAllTextAsync(Path.Combine(run.RunFolder, "suite-run.json"), JsonSerializer.Serialize(run, _json));
    }

    public async Task<List<AutomationSuiteRun>> LoadAllAsync()
    {
        var list = new List<AutomationSuiteRun>();
        if (!Directory.Exists(Root)) return list;
        foreach (var file in Directory.EnumerateFiles(Root, "suite-run.json", SearchOption.AllDirectories))
        {
            try
            {
                var item = JsonSerializer.Deserialize<AutomationSuiteRun>(await File.ReadAllTextAsync(file), _json);
                if (item is not null) list.Add(item);
            }
            catch { }
        }
        return list.OrderByDescending(x => x.StartedAt).ToList();
    }
}
