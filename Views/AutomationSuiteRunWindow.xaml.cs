using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using QAFlowRecorder.Models;
using QAFlowRecorder.Services;

namespace QAFlowRecorder.Views;

public partial class AutomationSuiteRunWindow : Window
{
    private readonly AutomationProject _project;
    private readonly AutomationSuite _suite;
    private readonly AppSettings _settings;
    private readonly AutomationRunStore _runStore = new();
    private readonly AutomationSuiteRunStore _suiteStore = new();
    private readonly ObservableCollection<AutomationSuiteCaseResult> _results = [];
    private CancellationTokenSource? _cts;
    private AutomationSuiteRun? _suiteRun;

    public AutomationSuiteRunWindow(AutomationProject project, AutomationSuite suite, AppSettings settings)
    {
        InitializeComponent();
        WindowThemeHelper.ApplyDarkTitleBar(this);
        _project = project; _suite = suite; _settings = settings;
        ProjectText.Text = project.Name; SuiteText.Text = suite.Name; BrowserText.Text = project.DefaultBrowser;
        EnvironmentBox.ItemsSource = project.Environments;
        EnvironmentBox.SelectedItem = project.Environments.FirstOrDefault(e => e.IsDefault) ?? project.Environments.FirstOrDefault();
        DataSetBox.ItemsSource = project.DataSets;
        CasesGrid.ItemsSource = _results;
        ApplyLanguage();
    }

    private bool IsSpanish => _settings.UiLanguage.StartsWith("Esp", StringComparison.OrdinalIgnoreCase);

    private void ApplyLanguage()
    {
        if (!IsSpanish) return;
        Title = "QAFlow · Ejecución de suite"; TitleText.Text = "Ejecución de suite";
        SubtitleText.Text = "Ejecuta los casos secuencialmente y consolida los resultados en un solo reporte.";
        DataLabel.Text = "DATOS DE PRUEBA (OPCIONAL)"; EnvironmentLabel.Text = "AMBIENTE"; StatusText.Text = "Lista para ejecutar la suite.";
        OpenReportButton.Content = "Abrir reporte de suite"; StopButton.Content = "Detener"; RunButton.Content = "▶ Ejecutar suite";
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        if (_cts is not null) return;
        var cases = _suite.TestCases.Where(c => c.Status != AutomationCaseStatus.Archived).OrderBy(c => c.Key).ThenBy(c => c.Name).ToList();
        if (cases.Count == 0)
        {
            AppDialogWindow.ShowInfo(this, "QAFlow Recorder", IsSpanish ? "La suite no contiene casos ejecutables." : "The suite has no executable test cases.", _settings.UiLanguage); return;
        }
        var selectedEnvironment = EnvironmentBox.SelectedItem as AutomationEnvironmentProfile;
        var selectedProject = AutomationEnvironmentService.Apply(_project, selectedEnvironment);
        if (!Uri.TryCreate(selectedProject.BaseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            AppDialogWindow.ShowInfo(this, "QAFlow Recorder", IsSpanish ? "Configura una URL base web válida en el ambiente antes de ejecutar la suite." : "Configure a valid web Base URL in the environment before running the suite.", _settings.UiLanguage); return;
        }

        _cts = new CancellationTokenSource(); _results.Clear();
        RunButton.IsEnabled = false; StopButton.IsEnabled = true; OpenReportButton.IsEnabled = false;
        StatusBadge.Text = IsSpanish ? "EJECUTANDO" : "RUNNING";
        var environment = selectedEnvironment;
        var effectiveProject = selectedProject;
        var overrideDataSet = DataSetBox.SelectedItem as AutomationDataSet ?? _project.DataSets.FirstOrDefault(d => d.Id == environment?.DefaultDataSetId);
        BrowserText.Text = effectiveProject.DefaultBrowser;
        _suiteRun = new AutomationSuiteRun { ProjectId = effectiveProject.Id, SuiteId = _suite.Id, ProjectName = effectiveProject.Name, SuiteName = _suite.Name, BaseUrl = effectiveProject.BaseUrl, Browser = effectiveProject.DefaultBrowser, DataSetName = overrideDataSet?.Name ?? "", EnvironmentName = environment?.Name ?? "Default", Status = AutomationSuiteRunStatus.Running, StartedAt = DateTimeOffset.Now };
        _suiteStore.CreateRunFolder(_suiteRun); await _suiteStore.SaveAsync(_suiteRun);

        try
        {
            var beforeSuite = AutomationLifecycleService.Component(effectiveProject, _suite.BeforeSuiteComponentId);
            if (beforeSuite is not null)
            {
                StatusText.Text = IsSpanish ? "Ejecutando fixture Before Suite..." : "Running Before Suite fixture...";
                var fixtureResult = await ExecuteSuiteFixtureAsync(effectiveProject, beforeSuite, "BeforeSuite", overrideDataSet, _cts.Token);
                _suiteRun.Cases.Add(fixtureResult); _results.Add(fixtureResult); await _suiteStore.SaveAsync(_suiteRun);
                if (fixtureResult.Status != AutomationRunStatus.Passed)
                    throw new InvalidOperationException(IsSpanish ? "El fixture Before Suite falló; los casos fueron omitidos." : "Before Suite fixture failed; test cases were skipped.");
            }

            foreach (var tc in cases)
            {
                _cts.Token.ThrowIfCancellationRequested();
                if (!AutomationLifecycleService.DependencySatisfied(tc, _suiteRun.Cases))
                {
                    var blocked = new AutomationSuiteCaseResult { TestCaseId = tc.Id, TestCaseKey = tc.Key, TestCaseName = tc.Name, Status = AutomationRunStatus.Blocked, FailureMessage = IsSpanish ? "Dependencia requerida no aprobada." : "Required dependency did not pass." };
                    _suiteRun.Cases.Add(blocked); _results.Add(blocked); await _suiteStore.SaveAsync(_suiteRun); continue;
                }
                StatusText.Text = IsSpanish ? $"Ejecutando {tc.DisplayName}..." : $"Running {tc.DisplayName}...";
                var fallbackDataSet = overrideDataSet ?? effectiveProject.DataSets.FirstOrDefault(d => d.Id == tc.DefaultDataSetId);
                var iterations = AutomationLifecycleService.ResolveDataIterations(effectiveProject, tc, fallbackDataSet);
                if (tc.Steps.Count == 0)
                {
                    var empty = new AutomationSuiteCaseResult { TestCaseId = tc.Id, TestCaseKey = tc.Key, TestCaseName = tc.Name, Status = AutomationRunStatus.Blocked, FailureMessage = IsSpanish ? "Caso sin pasos." : "Test case has no steps." };
                    _suiteRun.Cases.Add(empty); _results.Add(empty); await _suiteStore.SaveAsync(_suiteRun); continue;
                }

                foreach (var dataSet in iterations)
                {
                    _cts.Token.ThrowIfCancellationRequested();
                    var service = new WebAutomationExecutionService(_runStore);
                    var run = await Task.Run(() => service.ExecuteAsync(effectiveProject, _suite, tc, dataSet, step => ManualCheckpointAsync(tc, step), (_, _) => { }, _cts.Token));
                    var result = new AutomationSuiteCaseResult { TestCaseId = tc.Id, TestRunId = run.Id, TestCaseKey = tc.Key, TestCaseName = tc.Name, DataSetName = dataSet?.Name ?? "", Status = run.Status, PassRate = run.PassRate, Duration = run.DurationDisplay, HtmlReportPath = run.HtmlReportPath, FailureMessage = run.FailureMessage };
                    _suiteRun.Cases.Add(result);
                    Dispatcher.Invoke(() => _results.Add(result));
                    await _suiteStore.SaveAsync(_suiteRun);
                }
            }
            _suiteRun.Status = _suiteRun.Cases.Where(c => string.IsNullOrWhiteSpace(c.LifecycleStage)).Any(c => c.Status == AutomationRunStatus.Failed) ? AutomationSuiteRunStatus.Failed : _suiteRun.Cases.Where(c => string.IsNullOrWhiteSpace(c.LifecycleStage)).Any(c => c.Status is AutomationRunStatus.NeedsReview or AutomationRunStatus.Blocked) ? AutomationSuiteRunStatus.NeedsReview : AutomationSuiteRunStatus.Passed;
        }
        catch (OperationCanceledException)
        {
            _suiteRun.Status = AutomationSuiteRunStatus.Aborted; _suiteRun.FailureMessage = IsSpanish ? "Ejecución detenida por el usuario." : "Suite execution stopped by the user.";
        }
        catch (Exception ex)
        {
            _suiteRun.Status = AutomationSuiteRunStatus.Failed; _suiteRun.FailureMessage = ex.Message;
        }
        finally
        {
            try
            {
                var afterSuite = AutomationLifecycleService.Component(effectiveProject, _suite.AfterSuiteComponentId);
                if (afterSuite is not null)
                {
                    StatusText.Text = IsSpanish ? "Ejecutando fixture After Suite..." : "Running After Suite fixture...";
                    var fixtureResult = await ExecuteSuiteFixtureAsync(effectiveProject, afterSuite, "AfterSuite", overrideDataSet, CancellationToken.None);
                    _suiteRun.Cases.Add(fixtureResult); _results.Add(fixtureResult);
                    if (fixtureResult.Status != AutomationRunStatus.Passed && _suiteRun.Status == AutomationSuiteRunStatus.Passed) _suiteRun.Status = AutomationSuiteRunStatus.NeedsReview;
                }
            }
            catch (Exception ex)
            {
                if (string.IsNullOrWhiteSpace(_suiteRun.FailureMessage)) _suiteRun.FailureMessage = "After Suite: " + ex.Message;
                if (_suiteRun.Status == AutomationSuiteRunStatus.Passed) _suiteRun.Status = AutomationSuiteRunStatus.NeedsReview;
            }
            _suiteRun.FinishedAt = DateTimeOffset.Now;
            _suiteRun.HtmlReportPath = await AutomationSuiteHtmlReportService.GenerateAsync(_suiteRun);
            await _suiteStore.SaveAsync(_suiteRun);
            StatusBadge.Text = _suiteRun.Status.ToString().ToUpperInvariant();
            StatusText.Text = IsSpanish ? $"Suite finalizada: {_suiteRun.Status} · {_suiteRun.PassRate}% aprobada · {_suiteRun.DurationDisplay}" : $"Suite finished: {_suiteRun.Status} · {_suiteRun.PassRate}% passed · {_suiteRun.DurationDisplay}";
            OpenReportButton.IsEnabled = File.Exists(_suiteRun.HtmlReportPath);
            RunButton.IsEnabled = true; StopButton.IsEnabled = false;

            if (_project.EmailDelivery?.Enabled == true && _project.EmailDelivery.AutoSendAfterSuiteRun)
            {
                try
                {
                    StatusText.Text += IsSpanish ? " · Enviando reporte..." : " · Sending report...";
                    await new AutomationReportDeliveryService().SendSuiteRunAsync(_project, _suiteRun);
                    StatusText.Text = IsSpanish ? "Suite finalizada y reporte enviado por correo." : "Suite finished and report delivered by email.";
                }
                catch (Exception ex)
                {
                    StatusText.Text = IsSpanish ? $"Suite finalizada. El correo no pudo enviarse: {ex.Message}" : $"Suite finished. Email delivery failed: {ex.Message}";
                }
            }
            _cts?.Dispose(); _cts = null;
        }
    }

    private async Task<AutomationSuiteCaseResult> ExecuteSuiteFixtureAsync(AutomationProject project, ReusableAutomationComponent component, string stage, AutomationDataSet? dataSet, CancellationToken token)
    {
        var fixtureCase = AutomationLifecycleService.CreateSuiteFixture(project, component.Id, stage == "BeforeSuite" ? "SETUP" : "TEARDOWN", component.Name);
        var service = new WebAutomationExecutionService(_runStore);
        var run = await Task.Run(() => service.ExecuteAsync(project, _suite, fixtureCase, dataSet, step => ManualCheckpointAsync(fixtureCase, step), (_, _) => { }, token));
        return new AutomationSuiteCaseResult
        {
            TestCaseId = fixtureCase.Id, TestRunId = run.Id, TestCaseKey = fixtureCase.Key, TestCaseName = fixtureCase.Name,
            DataSetName = dataSet?.Name ?? "", LifecycleStage = stage, Status = run.Status, PassRate = run.PassRate,
            Duration = run.DurationDisplay, HtmlReportPath = run.HtmlReportPath, FailureMessage = run.FailureMessage
        };
    }

    private Task<bool> ManualCheckpointAsync(AutomationTestCase testCase, AutomationTestStep step) => Dispatcher.InvokeAsync(() => AppDialogWindow.ShowQuestion(this,
        IsSpanish ? "Checkpoint manual" : "Manual checkpoint",
        IsSpanish ? $"{testCase.DisplayName}\n\nPaso {step.Order}: {step.Target}\n\nRealiza o valida el paso y selecciona Sí si fue correcto." : $"{testCase.DisplayName}\n\nStep {step.Order}: {step.Target}\n\nPerform or validate this step, then choose Yes if it passed.", _settings.UiLanguage)).Task;


    private void EnvironmentBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var effective = AutomationEnvironmentService.Apply(_project, EnvironmentBox.SelectedItem as AutomationEnvironmentProfile);
        BrowserText.Text = effective.DefaultBrowser;
        if (EnvironmentBox.SelectedItem is AutomationEnvironmentProfile env && env.DefaultDataSetId.HasValue)
            DataSetBox.SelectedItem = _project.DataSets.FirstOrDefault(d => d.Id == env.DefaultDataSetId.Value);
    }

    private void Stop_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();
    private void OpenReport_Click(object sender, RoutedEventArgs e)
    {
        if (_suiteRun is null || !File.Exists(_suiteRun.HtmlReportPath)) return;
        Process.Start(new ProcessStartInfo(_suiteRun.HtmlReportPath) { UseShellExecute = true });
    }
}
