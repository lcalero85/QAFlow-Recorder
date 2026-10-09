using Microsoft.Win32;
using System.Diagnostics;

namespace QAFlowRecorder.Services;

public sealed record BrowserCapabilityStatus(string Browser, bool Detected, string ExecutablePath, string Version, string Message)
{
    public string Display => Detected
        ? $"{Browser}: detected{(string.IsNullOrWhiteSpace(Version) ? "" : $" · v{Version}")}{(string.IsNullOrWhiteSpace(ExecutablePath) ? "" : $" · {ExecutablePath}")}"
        : $"{Browser}: not detected · {Message}";
}

public static class AutomationBrowserCapabilityService
{
    public static IReadOnlyList<BrowserCapabilityStatus> CheckMany(IEnumerable<string> browsers)
        => browsers.Distinct(StringComparer.OrdinalIgnoreCase).Select(Check).ToList();

    public static BrowserCapabilityStatus Check(string browser)
    {
        var normalized = (browser ?? "").Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return new BrowserCapabilityStatus("Unknown", false, "", "", "Browser name is empty.");

        var candidates = GetCandidates(normalized).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var found = candidates.FirstOrDefault(File.Exists);
        if (!string.IsNullOrWhiteSpace(found))
            return CreateDetected(normalized, found, "Browser executable detected.");

        var appPath = TryReadAppPath(normalized);
        if (!string.IsNullOrWhiteSpace(appPath) && File.Exists(appPath))
            return CreateDetected(normalized, appPath, "Browser executable detected through Windows App Paths.");

        var pathHit = TryFindOnPath(GetExecutableName(normalized));
        if (!string.IsNullOrWhiteSpace(pathHit))
            return CreateDetected(normalized, pathHit, "Browser executable detected on PATH.");

        return new BrowserCapabilityStatus(normalized, false, "", "",
            "No standard browser executable was found. QAFlow will not disable the run because portable/custom installations may still be resolved by Selenium Manager.");
    }


    private static BrowserCapabilityStatus CreateDetected(string browser, string executablePath, string message)
    {
        var version = TryGetVersion(executablePath);
        return new BrowserCapabilityStatus(browser, true, executablePath, version, message);
    }

    private static string TryGetVersion(string executablePath)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(executablePath);
            var value = info.ProductVersion;
            if (string.IsNullOrWhiteSpace(value)) value = info.FileVersion;
            if (string.IsNullOrWhiteSpace(value)) return "";
            var normalized = value.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0].Trim();
            return normalized;
        }
        catch (Exception ex) { TechnicalLogService.Warn(nameof(AutomationBrowserCapabilityService), "ReadBrowserVersion", ex.Message, ex); return ""; }
    }

    private static IEnumerable<string> GetCandidates(string browser)
    {
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pfx86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return browser.ToLowerInvariant() switch
        {
            "chrome" => new[]
            {
                Path.Combine(pf, "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(pfx86, "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(local, "Google", "Chrome", "Application", "chrome.exe")
            },
            "edge" => new[]
            {
                Path.Combine(pf, "Microsoft", "Edge", "Application", "msedge.exe"),
                Path.Combine(pfx86, "Microsoft", "Edge", "Application", "msedge.exe"),
                Path.Combine(local, "Microsoft", "Edge", "Application", "msedge.exe")
            },
            "firefox" => new[]
            {
                Path.Combine(pf, "Mozilla Firefox", "firefox.exe"),
                Path.Combine(pfx86, "Mozilla Firefox", "firefox.exe"),
                Path.Combine(local, "Mozilla Firefox", "firefox.exe")
            },
            _ => Array.Empty<string>()
        };
    }

    private static string GetExecutableName(string browser) => browser.ToLowerInvariant() switch
    {
        "chrome" => "chrome.exe",
        "edge" => "msedge.exe",
        "firefox" => "firefox.exe",
        _ => browser.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? browser : browser + ".exe"
    };

    private static string? TryReadAppPath(string browser)
    {
        var exe = GetExecutableName(browser);
        var subKey = $@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{exe}";
        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            try
            {
                using var key = root.OpenSubKey(subKey);
                var raw = key?.GetValue(null)?.ToString()?.Trim().Trim('"');
                if (!string.IsNullOrWhiteSpace(raw)) return raw;
            }
            catch (Exception ex) { TechnicalLogService.Warn(nameof(AutomationBrowserCapabilityService), "ReadAppPath", ex.Message, ex); }
        }
        return null;
    }

    private static string? TryFindOnPath(string executable)
    {
        try
        {
            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var part in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var candidate = Path.Combine(part.Trim('"'), executable);
                if (File.Exists(candidate)) return candidate;
            }
        }
        catch (Exception ex) { TechnicalLogService.Warn(nameof(AutomationBrowserCapabilityService), "FindOnPath", ex.Message, ex); }
        return null;
    }
}
