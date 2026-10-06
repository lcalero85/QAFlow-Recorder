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
        if (_testCase.Steps.Where(s => !s.IsManualCheckpoint && !s.Action.Equals("Input", StringComparison.OrdinalIgnoreCase) && !s.Action.Equals("Select", StringComparison.OrdinalIgnoreCase))
            .Any(s => string.IsNullOrWhiteSpace(s.SelectorProfile.PrimaryValue) && !s.Action.Equals("Navigate", StringComparison.OrdinalIgnoreCase) && !s.Action.Equals("Scroll", StringComparison.OrdinalIgnoreCase)))
        {
            var go = AppDialogWindow.ShowQuestion(this, "QAFlow Recorder",
                IsSpanish ? "Algunos pasos automáticos todavía no tienen un selector DOM estable. QAFlow puede ejecutar los pasos resolubles y detenerse si encuentra uno ambiguo. ¿Continuar?" : "Some automatic steps do not yet have a stable DOM selector. QAFlow can run resolvable steps and stop on ambiguity. Continue?",
                _settings.UiLanguage);
            if (!go) return;
        }

        var selectedDataSet = DataSetBox.SelectedItem as AutomationDataSet;
        var requiresDataSet = _testCase.Steps.Any(s => !string.IsNullOrWhiteSpace(s.ValueExpression) && s.ValueExpression.Contains("${", StringComparison.Ordinal));
        if (requiresDataSet && selectedDataSet is null)
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

        try
        {
            var service = new WebAutomationExecutionService(_store);
            _run = await Task.Run(() => service.ExecuteAsync(_project, _suite, _testCase, selectedDataSet, ManualCheckpointAsync, Progress, _cts.Token));
            StatusBadge.Text = _run.Status.ToString().ToUpperInvariant();
            RunStatusText.Text = IsSpanish
                ? $"Ejecución finalizada: {_run.Status} · {_run.PassRate}% aprobado · {_run.DurationDisplay}"
                : $"Run finished: {_run.Status} · {_run.PassRate}% passed · {_run.DurationDisplay}";
            OpenReportButton.IsEnabled = File.Exists(_run.HtmlReportPath);
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
        if (_run is null || !File.Exists(_run.HtmlReportPath)) return;
        Process.Start(new ProcessStartInfo(_run.HtmlReportPath) { UseShellExecute = true });
    }
}
