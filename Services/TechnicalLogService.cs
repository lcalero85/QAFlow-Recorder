using System.Text.Json;

namespace QAFlowRecorder.Services;

/// <summary>
/// Local structured diagnostics for recoverable technical failures. The log is intentionally
/// stored outside user reports and never includes secrets or typed field values.
/// </summary>
public static class TechnicalLogService
{
    private static readonly object Sync = new();
    private static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QAFlowRecorder", "logs");
    private static readonly string LogFile = Path.Combine(Root, "technical.jsonl");
    private const long MaxBytes = 4 * 1024 * 1024;

    public static string CurrentLogPath => LogFile;

    public static void Info(string component, string action, string message) => Write("info", component, action, message, null);
    public static void Warn(string component, string action, string message, Exception? ex = null) => Write("warning", component, action, message, ex);
    public static void Error(string component, string action, Exception ex) => Write("error", component, action, ex.Message, ex);

    private static void Write(string level, string component, string action, string message, Exception? ex)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(Root);
                RotateIfNeeded();
                var record = new
                {
                    timestamp = DateTimeOffset.Now,
                    level,
                    component,
                    action,
                    message = Sanitize(message),
                    exception = ex?.GetType().Name,
                    hresult = ex?.HResult
                };
                File.AppendAllText(LogFile, JsonSerializer.Serialize(record) + Environment.NewLine);
            }
        }
        catch
        {
            // Diagnostics must never interfere with recording or automation.
        }
    }

    private static void RotateIfNeeded()
    {
        if (!File.Exists(LogFile) || new FileInfo(LogFile).Length < MaxBytes) return;
        var archived = Path.Combine(Root, "technical.previous.jsonl");
        if (File.Exists(archived)) File.Delete(archived);
        File.Move(LogFile, archived);
    }

    private static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var text = value.Replace("\r", " ").Replace("\n", " ").Trim();
        return text.Length <= 500 ? text : text[..500];
    }
}
