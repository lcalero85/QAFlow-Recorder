using System.IO;
using System.Text.Json;
using QAFlowRecorder.Models;

namespace QAFlowRecorder.Services;

public sealed class SessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    public string Root { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QAFlow Recorder 1.0");
    public string SessionRoot => Path.Combine(Root, "Sessions");
    public string SettingsFile => Path.Combine(Root, "settings.json");

    public SessionStore()
    {
        Directory.CreateDirectory(SessionRoot);
    }

    public async Task SaveAsync(TestSession session)
    {
        await _saveGate.WaitAsync();
        try
        {
            var dir = Path.Combine(SessionRoot, session.Id.ToString("N"));
            Directory.CreateDirectory(dir);
            var target = Path.Combine(dir, "session.json");
            var temp = target + ".tmp";
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(session, JsonOptions));
            File.Move(temp, target, true);
            TrimHistory();
        }
        finally
        {
            _saveGate.Release();
        }
    }

    public async Task<TestSession?> LoadRecoverableAsync()
    {
        var all = await LoadAllAsync();
        return all
            .Where(s => s.FinishedAt is null && s.State is SessionState.Recording or SessionState.Paused)
            .OrderByDescending(s => s.RecoveryCheckpointAt ?? s.StartedAt)
            .FirstOrDefault();
    }

    public async Task<List<TestSession>> LoadAllAsync()
    {
        var list = new List<TestSession>();
        foreach (var file in Directory.EnumerateFiles(SessionRoot, "session.json", SearchOption.AllDirectories))
        {
            try
            {
                var s = JsonSerializer.Deserialize<TestSession>(await File.ReadAllTextAsync(file));
                if (s is not null) list.Add(s);
            }
            catch (Exception ex) { TechnicalLogService.Warn(nameof(SessionStore), "LoadSession", ex.Message, ex); }
        }
        return list.OrderByDescending(s => s.StartedAt).Take(50).ToList();
    }

    public async Task<AppSettings> LoadSettingsAsync()
    {
        try
        {
            if (File.Exists(SettingsFile))
                return JsonSerializer.Deserialize<AppSettings>(await File.ReadAllTextAsync(SettingsFile)) ?? new();
        }
        catch (Exception ex) { TechnicalLogService.Warn(nameof(SessionStore), "LoadSettings", ex.Message, ex); }
        return new();
    }

    public Task SaveSettingsAsync(AppSettings settings)
    {
        Directory.CreateDirectory(Root);
        return File.WriteAllTextAsync(SettingsFile, JsonSerializer.Serialize(settings, JsonOptions));
    }


    public Task DeleteAsync(TestSession session)
    {
        if (session is null) return Task.CompletedTask;
        var dir = Path.Combine(SessionRoot, session.Id.ToString("N"));
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
        catch (IOException) { throw; }
        catch (UnauthorizedAccessException) { throw; }
        return Task.CompletedTask;
    }

    public Task ClearHistoryAsync()
    {
        if (!Directory.Exists(SessionRoot)) return Task.CompletedTask;
        foreach (var dir in Directory.EnumerateDirectories(SessionRoot))
        {
            try { Directory.Delete(dir, true); }
            catch (DirectoryNotFoundException) { }
        }
        Directory.CreateDirectory(SessionRoot);
        return Task.CompletedTask;
    }

    public string SessionFolder(TestSession session)
    {
        var path = Path.Combine(SessionRoot, session.Id.ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public string EvidenceFolder(TestSession session)
    {
        var path = Path.Combine(SessionRoot, session.Id.ToString("N"), "evidence");
        Directory.CreateDirectory(path);
        return path;
    }

    public string VideoFolder(TestSession session)
    {
        var path = Path.Combine(SessionRoot, session.Id.ToString("N"), "video");
        Directory.CreateDirectory(path);
        return path;
    }

    private void TrimHistory()
    {
        var dirs = new DirectoryInfo(SessionRoot).EnumerateDirectories().OrderByDescending(d => d.LastWriteTimeUtc).ToList();
        foreach (var d in dirs.Skip(50))
        {
            try { d.Delete(true); } catch (Exception ex) { TechnicalLogService.Warn(nameof(SessionStore), "TrimHistory", ex.Message, ex); }
        }
    }
}
