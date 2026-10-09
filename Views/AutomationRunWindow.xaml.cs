using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using QAFlowRecorder.Models;
using QAFlowRecorder.Services;

namespace QAFlowRecorder.Views;

public partial class AutomationRunWindow : Window
{
    private readonly AutomationProject _project;
    private readonly AutomationSuite _suite;
    private readonly AutomationTestCase _testCase;
    private readonly AppSettings _settings;
    private readonly AutomationRunStore _store = new();
    private readonly ObservableCollection<AutomationStepRun> _steps = [];
    private CancellationTokenSource? _cts;
    private AutomationTestRun? _run;
    private string _reportPath = "";

    public AutomationRunWindow(AutomationProject project, AutomationSuite suite, AutomationTestCase testCase, AppSettings settings)
    {
        InitializeComponent();
        WindowThemeHelper.ApplyDarkTitleBar(this);
        _project = project; _suite = suite; _testCase = testCase; _settings = settings;
        ProjectText.Text = project.Name;
        CaseText.Text = testCase.DisplayName;
        BrowserText.Text = project.DefaultBrowser;
        ScopeText.Text = project.BaseUrl;
        DataSetBox.ItemsSource = project.DataSets;
        if (project.DataSets.Count > 0)
        {
            var preferred = project.DataSets.FirstOrDefault(d => d.Id == testCase.DefaultDataSetId) ?? project.DataSets[0];
            DataSetBox.SelectedItem = preferred;
        }
        RunStepsGrid.ItemsSource = _steps;
        ApplyLanguage();
    }

    private bool IsSpanish => _settings.UiLanguage.StartsWith("Esp", StringComparison.OrdinalIgnoreCase);

    private void ApplyLanguage()
    {
        if (!IsSpanish) return;
        Title = "QAFlow · Ejecución de automatización web";
        RunTitleText.Text = "Ejecución de automatización web";
        RunSubtitleText.Text = "La ejecución Selenium permanece limitada a la URL configurada del proyecto.";
        RunStatusText.Text = "Lista para ejecutar.";
        OpenReportButton.Content = "Abrir reporte HTML";
        CancelRunButton.Content = "Detener";
        StartRunButton.Content = "▶ Ejecutar caso";
        DataSetLabel.Text = "DATOS DE PRUEBA";
    }

    private async void StartRun_Click(object sender, RoutedEventArgs e)
    {
        if (_cts is not null) return;
        if (_testCase.Steps.Count == 0)
        {
            AppDialogWindow.ShowInfo(this, "QAFlow Recorder", IsSpanish ? "Este caso no tiene pasos para ejecutar." : "This test case has no steps to execute.", _settings.UiLanguage);
            return;
        }
        if (_testCase.Steps.Where(s => !s.IsManualCheckpoint && ActionRequiresElement(s.Action))
            .Any(s => string.IsNullOrWhiteSpace(s.SelectorProfile.PrimaryValue)))
        {
            var go = AppDialogWindow.ShowQuestion(this, "QAFlow Recorder",
                IsSpanish ? "Algunos pasos automáticos todavía no tienen un selector DOM estable. QAFlow puede ejecutar los pasos resolubles y detenerse si encuentra uno ambiguo. ¿Continuar?" : "Some automatic steps do not yet have a stable DOM selector. QAFlow can run resolvable steps and stop on ambiguity. Continue?",
                _settings.UiLanguage);
            if (!go) return;
        }

        var selectedDataSet = DataSetBox.SelectedItem as AutomationDataSet;
        var dataIterations = AutomationLifecycleService.ResolveDataIterations(_project, _testCase, selectedDataSet);
        var requiresDataSet = _testCase.Steps.Any(s => !string.IsNullOrWhiteSpace(s.ValueExpression) && s.ValueExpression.Contains("${", StringComparison.Ordinal));
        if (requiresDataSet && dataIterations.All(d => d is null))
        {
            AppDialogWindow.ShowInfo(this, "QAFlow Recorder", IsSpanish ? "Este caso utiliza variables de datos. Selecciona un conjunto de datos antes de ejecutar." : "This test case uses data variables. Select a test data set before running.", _settings.UiLanguage);
            return;
        }

        _cts = new CancellationTokenSource();
        StartRunButton.IsEnabled = false;
        CancelRunButton.IsEnabled = true;
        OpenReportButton.IsEnabled = false;
        StatusBadge.Text = IsSpanish ? "EJECUTANDO" : "RUNNING";
        RunStatusText.Text = IsSpanish ? "Iniciando navegador y ejecución..." : "Starting browser and execution...";
        _steps.Clear();
        _reportPath = "";

        try
        {
            var service = new WebAutomationExecutionService(_store);
            var runs = new List<AutomationTestRun>();
            foreach (var iteration in dataIterations)
            {
                _cts.Token.ThrowIfCancellationRequested();
                _steps.Clear();
                RunStatusText.Text = _testCase.DataDrivenEnabled
                    ? (IsSpanish ? $"Iteración: {iteration?.Name ?? "Sin datos"}..." : $"Iteration: {iteration?.Name ?? "No data"}...")
                    : (IsSpanish ? "Ejecutando caso..." : "Running test case...");
                _run = await Task.Run(() => service.ExecuteAsync(_project, _suite, _testCase, iteration, ManualCheckpointAsync, Progress, _cts.Token));
                runs.Add(_run);
            }
            if (runs.Count > 1)
            {
                _reportPath = await AutomationDataDrivenHtmlReportService.GenerateAsync(_project, _suite, _testCase, runs);
                var passed = runs.Count(r => r.Status == AutomationRunStatus.Passed);
                var failed = runs.Count - passed;
                StatusBadge.Text = failed == 0 ? "PASSED" : "FAILED";
                RunStatusText.Text = IsSpanish
                    ? $"Data-driven finalizado: {passed}/{runs.Count} iteraciones aprobadas."
                    : $"Data-driven run finished: {passed}/{runs.Count} iterations passed.";
            }
            else if (_run is not null)
            {
                _reportPath = _run.HtmlReportPath;
                StatusBadge.Text = _run.Status.ToString().ToUpperInvariant();
                RunStatusText.Text = IsSpanish
                    ? $"Ejecución finalizada: {_run.Status} · {_run.PassRate}% aprobado · {_run.DurationDisplay}"
                    : $"Run finished: {_run.Status} · {_run.PassRate}% passed · {_run.DurationDisplay}";
            }
            OpenReportButton.IsEnabled = File.Exists(_reportPath);
        }
        catch (Exception ex)
        {
            StatusBadge.Text = "FAILED";
            RunStatusText.Text = ex.Message;
            AppDialogWindow.ShowInfo(this, IsSpanish ? "Error de ejecución" : "Execution error", ex.Message, _settings.UiLanguage);
        }
        finally
        {
            _cts?.Dispose(); _cts = null;
            StartRunButton.IsEnabled = true;
            CancelRunButton.IsEnabled = false;
        }
    }

    private static bool ActionRequiresElement(string? action)
    {
        var a = (action ?? "").Trim().ToLowerInvariant();
        return a is not ("navigate" or "scroll" or "validatedownload" or "validate download" or "setcookie" or "set cookie" or "deletecookie" or "delete cookie" or "clearcookies" or "clear cookies" or "setlocalstorage" or "set local storage" or "removelocalstorage" or "remove local storage" or "clearlocalstorage" or "clear local storage" or "setsessionstorage" or "set session storage" or "removesessionstorage" or "remove session storage" or "clearsessionstorage" or "clear session storage");
    }

    private Task<bool> ManualCheckpointAsync(AutomationTestStep step)
    {
        return Dispatcher.InvokeAsync(() => AppDialogWindow.ShowQuestion(this,
            IsSpanish ? "Checkpoint manual" : "Manual checkpoint",
            IsSpanish
                ? $"QAFlow pausó la automatización.\n\nPaso {step.Order}: {step.Target}\n\nRealiza o valida este paso manualmente en el navegador y selecciona Sí si el resultado fue correcto."
                : $"QAFlow paused automation.\n\nStep {step.Order}: {step.Target}\n\nPerform or validate this step manually in the browser, then choose Yes if the result is correct.",
            _settings.UiLanguage)).Task;
    }

    private void Progress(AutomationTestRun run, AutomationStepRun step)
    {
        Dispatcher.Invoke(() =>
        {
            var existing = _steps.FirstOrDefault(x => x.Id == step.Id);
            if (existing is null) _steps.Add(step);
            RunStepsGrid.Items.Refresh();
            StatusBadge.Text = run.Status.ToString().ToUpperInvariant();
            RunStatusText.Text = IsSpanish ? $"Paso {step.Order}: {step.Target}" : $"Step {step.Order}: {step.Target}";
        });
    }

    private void CancelRun_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private void OpenReport_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_reportPath) || !File.Exists(_reportPath)) return;
        Process.Start(new ProcessStartInfo(_reportPath) { UseShellExecute = true });
    }
}
