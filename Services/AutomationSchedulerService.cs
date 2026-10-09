using System.Diagnostics;
using QAFlowRecorder.Models;

namespace QAFlowRecorder.Services;

public sealed class AutomationSchedulerService
{
    public string BuildTaskName(AutomationProject project, AutomationSchedule schedule)
        => $"QAFlow\\{Sanitize(project.Name)}-{schedule.Id:N}";

    public async Task<string> RegisterAsync(AutomationProject project, AutomationSchedule schedule, CancellationToken ct = default)
    {
        if (!schedule.Enabled) throw new InvalidOperationException("The schedule is disabled.");
        if (project.Suites.All(s => s.Id != schedule.SuiteId)) throw new InvalidOperationException("The selected suite no longer exists.");
        if (!TimeSpan.TryParse(schedule.TimeOfDay, out var time)) throw new InvalidOperationException("Schedule time must use HH:mm format.");

        var exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "QAFlowRecorder.exe");
        if (!File.Exists(exe)) throw new FileNotFoundException("QAFlowRecorder.exe was not found.", exe);

        var taskName = BuildTaskName(project, schedule);
        var envArg = schedule.EnvironmentId.HasValue ? $" --environment {schedule.EnvironmentId.Value}" : "";
        var browserHeadlessArg = schedule.BrowserHeadless ? " --browser-headless true" : "";
        var command = $"\"{exe}\" --run-suite {project.Id} {schedule.SuiteId}{envArg} --send-report {schedule.SendReport.ToString().ToLowerInvariant()} --noninteractive{browserHeadlessArg}";
        var args = new List<string> { "/Create", "/TN", Quote(taskName), "/TR", Quote(command), "/ST", time.ToString(@"hh\:mm"), "/F" };
        if (schedule.Frequency == AutomationScheduleFrequency.Weekly)
        {
            var days = NormalizeDays(schedule.DaysOfWeek);
            args.AddRange(["/SC", "WEEKLY", "/D", days]);
        }
        else args.AddRange(["/SC", "DAILY"]);

        var result = await RunAsync("schtasks.exe", string.Join(' ', args), ct);
        if (result.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error);
        schedule.TaskName = taskName;
        schedule.LastRegisteredAt = DateTimeOffset.Now;
        return taskName;
    }

    public async Task RemoveAsync(AutomationSchedule schedule, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(schedule.TaskName)) return;
        var result = await RunAsync("schtasks.exe", $"/Delete /TN {Quote(schedule.TaskName)} /F", ct);
        if (result.ExitCode != 0 && !result.Error.Contains("cannot find", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error);
        schedule.TaskName = "";
        schedule.LastRegisteredAt = null;
    }

    private static string NormalizeDays(string value)
    {
        var allowed = new HashSet<string>(["MON","TUE","WED","THU","FRI","SAT","SUN"], StringComparer.OrdinalIgnoreCase);
        var days = value.Split([',',';',' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.ToUpperInvariant()).Where(allowed.Contains).Distinct().ToList();
        return days.Count == 0 ? "MON" : string.Join(',', days);
    }
    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";
    private static string Sanitize(string value) => string.Concat(value.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-')).Trim('-');

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(string file, string args, CancellationToken ct)
    {
        using var p = new Process { StartInfo = new ProcessStartInfo(file, args) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true } };
        p.Start();
        var outputTask = p.StandardOutput.ReadToEndAsync(ct);
        var errorTask = p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        return (p.ExitCode, await outputTask, await errorTask);
    }
}
