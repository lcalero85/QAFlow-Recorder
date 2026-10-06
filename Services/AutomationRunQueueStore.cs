using System.Text.Json;
using QAFlowRecorder.Models;

namespace QAFlowRecorder.Services;

public sealed class AutomationRunQueueStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string _root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QAFlowRecorder", "automation", "queue");
    private string QueueFile => Path.Combine(_root, "queue.json");
    private string SettingsFile => Path.Combine(_root, "settings.json");

    public async Task<List<AutomationQueueItem>> LoadAsync()
    {
        try
        {
            if (!File.Exists(QueueFile)) return [];
            return JsonSerializer.Deserialize<List<AutomationQueueItem>>(await File.ReadAllTextAsync(QueueFile), Options) ?? [];
        }
        catch { return []; }
    }

    public async Task SaveAsync(IEnumerable<AutomationQueueItem> items)
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(QueueFile, JsonSerializer.Serialize(items.OrderByDescending(x => x.CreatedAt).Take(500).ToList(), Options));
    }

    public async Task<AutomationQueueSettings> LoadSettingsAsync()
    {
        try
        {
            if (!File.Exists(SettingsFile)) return new();
            return JsonSerializer.Deserialize<AutomationQueueSettings>(await File.ReadAllTextAsync(SettingsFile), Options) ?? new();
        }
        catch { return new(); }
    }

    public async Task SaveSettingsAsync(AutomationQueueSettings settings)
    {
        Directory.CreateDirectory(_root);
        settings.MaxConcurrency = Math.Clamp(settings.MaxConcurrency, 1, 4);
        await File.WriteAllTextAsync(SettingsFile, JsonSerializer.Serialize(settings, Options));
    }
}
