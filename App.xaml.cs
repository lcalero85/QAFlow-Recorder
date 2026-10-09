using System.Windows;
using QAFlowRecorder.Services;

namespace QAFlowRecorder;

public partial class App : Application
{
    private async void Application_Startup(object sender, StartupEventArgs e)
    {
        var args = e.Args;
        var runSuiteIndex = Array.FindIndex(args, a => a.Equals("--run-suite", StringComparison.OrdinalIgnoreCase));
        if (runSuiteIndex >= 0 && args.Length > runSuiteIndex + 2 && Guid.TryParse(args[runSuiteIndex + 1], out var projectId) && Guid.TryParse(args[runSuiteIndex + 2], out var suiteId))
        {
            Guid? environmentId = null;
            var envIndex = Array.FindIndex(args, a => a.Equals("--environment", StringComparison.OrdinalIgnoreCase));
            if (envIndex >= 0 && args.Length > envIndex + 1 && Guid.TryParse(args[envIndex + 1], out var parsedEnv)) environmentId = parsedEnv;
            string? browserOverride = null;
            var browserIndex = Array.FindIndex(args, a => a.Equals("--browser", StringComparison.OrdinalIgnoreCase));
            if (browserIndex >= 0 && args.Length > browserIndex + 1) browserOverride = args[browserIndex + 1];
            var sendReport = true;
            var sendIndex = Array.FindIndex(args, a => a.Equals("--send-report", StringComparison.OrdinalIgnoreCase));
            if (sendIndex >= 0 && args.Length > sendIndex + 1 && bool.TryParse(args[sendIndex + 1], out var parsedSend)) sendReport = parsedSend;
            var browserHeadless = false;
            var browserHeadlessIndex = Array.FindIndex(args, a => a.Equals("--browser-headless", StringComparison.OrdinalIgnoreCase));
            if (browserHeadlessIndex >= 0)
            {
                browserHeadless = browserHeadlessIndex + 1 >= args.Length || !bool.TryParse(args[browserHeadlessIndex + 1], out var parsedBrowserHeadless) || parsedBrowserHeadless;
            }
            // Backward compatibility: --headless continues to mean non-interactive QAFlow execution.
            // Browser headless mode is opt-in through --browser-headless.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var exitCode = await new AutomationHeadlessSuiteRunner().RunAsync(projectId, suiteId, environmentId, sendReport, default, browserOverride, browserHeadless);
            Environment.ExitCode = exitCode;
            Shutdown(exitCode);
            return;
        }

        var startupSettings = await new SessionStore().LoadSettingsAsync();
        ThemeManager.ApplyTheme(startupSettings.UiTheme);

        var main = new MainWindow();
        MainWindow = main;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        main.Show();
    }
}
