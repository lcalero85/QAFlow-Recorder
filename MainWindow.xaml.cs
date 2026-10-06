using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using QAFlowRecorder.Models;
using QAFlowRecorder.Services;
using QAFlowRecorder.Views;

namespace QAFlowRecorder;

public partial class MainWindow : Window
{
    private readonly SessionStore _store = new();
    private readonly GlobalActivityRecorder _desktopRecorder = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _periodic = new();
    private TestSession? _session;
    private AppSettings _settings = new();
    private List<TestSession> _history = [];
    private List<ReusableFlow> _reusableFlows = [];
    private FloatingRecorderBar? _floatingBar;
    private bool _isFinishing;
    private bool _isSplitting;
    private HwndSource? _hwndSource;
    private WebInstrumentationBridge? _webBridge;

    private static readonly TimeSpan PartLimit = TimeSpan.FromHours(2);
    private const int HotkeyToggle = 101;
    private const int HotkeyAnnotate = 102;
    private const int HotkeyPause = 103;
    private const int HotkeyHistory = 104;
    private const int HotkeyRestore = 105;
    private const int HotkeyBug = 106;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint WmHotkey = 0x0312;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
        _timer.Tick += SessionTimer_Tick;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        SourceInitialized += MainWindow_SourceInitialized;
        Closed += MainWindow_Closed;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = await _store.LoadSettingsAsync();
        _desktopRecorder.EventCaptured += DesktopRecorder_EventCaptured;
        _webBridge = new WebInstrumentationBridge(_settings);
        _webBridge.ObservationReceived += WebBridge_ObservationReceived;
        _webBridge.Start();
        LoadSettingsToUi();
        await ReloadHistoryAsync();
        await ReloadReferenceFlowsAsync();
        ApplyLanguage();
        StateText.Text = Localizer.T("NoSession", _settings.UiLanguage);
        DockToRight();
    }

    private void DockToRight()
    {
        var work = SystemParameters.WorkArea;
        Height = Math.Min(860, work.Height - 24);
        Top = work.Top + Math.Max(12, (work.Height - Height) / 2);
        Left = work.Right - Width - 12;
    }

    private void DesktopRecorder_EventCaptured(QaEvent ev)
    {
        if (_session is null || _session.State != SessionState.Recording) return;
        var targetWindow = _desktopRecorder.LastTargetWindow;
        Dispatcher.BeginInvoke(new Action(async () =>
        {
            if (_session is null || _session.State != SessionState.Recording) return;
            SmartCheckpointService.Enrich(_session.Events, ev);
            _session.Events.Add(ev);
            RefreshMetrics();
            try
            {
                await CaptureEvidenceForEventAsync(ev, targetWindow);
            }
            catch { }
        }));
    }


    private void WebBridge_ObservationReceived(WebDomObservation observation)
    {
        if (_session is null || _session.State != SessionState.Recording) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_session is null || _session.State != SessionState.Recording) return;
            var expectedKind = observation.EventType.ToLowerInvariant() switch
            {
                "click" => EventKind.Click,
                "input" => EventKind.Input,
                "select" => EventKind.Select,
                "submit" => EventKind.Submit,
                "navigation" => EventKind.Navigation,
                _ => EventKind.Note
            };

            var now = DateTimeOffset.Now;
            var candidate = _session.Events
                .Where(e => e.Kind == expectedKind || (expectedKind == EventKind.Select && e.Kind == EventKind.Input))
                .Where(e => now - e.Timestamp < TimeSpan.FromSeconds(4))
                .OrderByDescending(e => e.Timestamp)
                .FirstOrDefault();

            if (candidate is null) return;
            candidate.Url = observation.Url;
            candidate.WebDomEnriched = true;
            candidate.WebElementTag = observation.TagName;
            candidate.WebElementText = observation.Text;
            candidate.WebElementId = observation.Id;
            candidate.WebElementName = observation.Name;
            candidate.WebElementRole = observation.Role;
            candidate.WebAriaLabel = observation.AriaLabel;
            candidate.WebDataTestId = observation.DataTestId;
            candidate.WebCssSelector = observation.CssSelector;
            candidate.WebXPath = observation.XPath;
            candidate.WebPageTitle = observation.PageTitle;
            SelectorIntelligenceService.ApplyPrimaryToEvent(candidate);

            var label = FirstWebLabel(observation);
            if (!string.IsNullOrWhiteSpace(label))
            {
                candidate.ControlName = label;
                candidate.TargetTitle = string.IsNullOrWhiteSpace(observation.PageTitle) ? candidate.TargetTitle : observation.PageTitle;
                candidate.Description = expectedKind switch
                {
                    EventKind.Click => $"Selected '{label}' in {observation.PageTitle}",
                    EventKind.Input => $"Entered or modified information in '{label}' in {observation.PageTitle}",
                    EventKind.Select => $"Selected an option in '{label}' in {observation.PageTitle}",
                    EventKind.Submit => $"Submitted '{label}' in {observation.PageTitle}",
                    _ => candidate.Description
                };
            }
        }));
    }

    private static string FirstWebLabel(WebDomObservation o)
    {
        foreach (var value in new[] { o.AriaLabel, o.Text, o.Name, o.Id })
            if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
        return string.IsNullOrWhiteSpace(o.TagName) ? "" : o.TagName;
    }

    private async Task CaptureEvidenceForEventAsync(QaEvent ev, IntPtr targetWindow)
    {
        if (_session is null) return;
        var barWasVisible = _floatingBar?.IsVisible == true;
        if (barWasVisible) _floatingBar!.Hide();
        try
        {
            await Task.Delay(90);
            var bitmap = DesktopCaptureService.CaptureWindowFromScreen(targetWindow)
                         ?? DesktopCaptureService.CapturePrimaryScreen();
            if (bitmap is null) return;
            if (_settings.PrivacyMasking == PrivacyMaskMode.Automatic && ev.Kind == EventKind.Input) ev.MaskSensitiveEvidence = true;
            var protectedBitmap = PrivacyMaskService.Apply(bitmap, ev, targetWindow, _settings.PrivacyMasking);
            var path = DesktopCaptureService.SavePng(protectedBitmap, _store.EvidenceFolder(_session), "interaction");
            _session.Screenshots.Add(new EvidenceShot
            {
                Timestamp = DateTimeOffset.Now,
                Kind = ScreenshotKind.FlowAction,
                TargetTitle = ev.TargetTitle,
                Url = "",
                FilePath = path,
                FullPage = false,
                EventId = ev.Id,
                SensitiveMaskApplied = !ReferenceEquals(protectedBitmap, bitmap)
            });
        }
        finally
        {
            if (barWasVisible && _floatingBar is not null) _floatingBar.Show();
        }
    }

    private async Task<string?> CaptureDesktopEvidenceAsync(ScreenshotKind kind, string? targetTitle = null)
    {
        if (_session is null) return null;
        var barWasVisible = _floatingBar?.IsVisible == true;
        if (barWasVisible) _floatingBar!.Hide();
        try
        {
            await Task.Delay(120);
            var hwnd = _desktopRecorder.LastTargetWindow;
            if (hwnd == IntPtr.Zero || DesktopCaptureService.IsHandleOwnedByCurrentProcess(hwnd))
                hwnd = DesktopCaptureService.GetForegroundWindowHandle();
            var bitmap = DesktopCaptureService.CaptureWindowFromScreen(hwnd)
                         ?? DesktopCaptureService.CapturePrimaryScreen();
            if (bitmap is null) return null;
            var path = DesktopCaptureService.SavePng(bitmap, _store.EvidenceFolder(_session), kind.ToString().ToLowerInvariant());
            _session.Screenshots.Add(new EvidenceShot
            {
                Timestamp = DateTimeOffset.Now,
                Kind = kind,
                TargetTitle = string.IsNullOrWhiteSpace(targetTitle) ? _desktopRecorder.LastTargetTitle : targetTitle!,
                Url = "",
                FilePath = path,
                FullPage = false
            });
            return path;
        }
        finally
        {
            if (barWasVisible && _floatingBar is not null) _floatingBar.Show();
        }
    }

    private async void Start_Click(object sender, RoutedEventArgs e) => await StartSessionAsync();

    private async Task StartSessionAsync()
    {
        if (_session is { State: SessionState.Recording or SessionState.Paused }) return;
        var baseName = string.IsNullOrWhiteSpace(SessionNameBox.Text)
            ? $"{(_settings.UiLanguage.StartsWith("Esp", StringComparison.OrdinalIgnoreCase) ? "Sesión QA" : "QA Session")} {DateTime.Now:yyyy-MM-dd HH:mm}"
            : SessionNameBox.Text.Trim();

        var session = new TestSession
        {
            Name = baseName,
            BaseName = baseName,
            Tester = _settings.QaName,
            Template = TemplateBox.SelectedIndex switch { 1 => "Regression", 2 => "UAT", 3 => "Smoke", 4 => "Exploratory", _ => "Functional Normal" },
            StartedAt = DateTimeOffset.Now,
            Resolution = $"{SystemParameters.PrimaryScreenWidth:0}x{SystemParameters.PrimaryScreenHeight:0}",
            State = SessionState.Recording,
            PartNumber = 1,
            CharterObjective = CharterObjectiveBox.Text.Trim(),
            CharterScope = CharterScopeBox.Text.Trim(),
            CharterRisks = CharterRisksBox.Text.Trim(),
            CharterTestData = CharterDataBox.Text.Trim(),
            CharterConstraints = CharterConstraintsBox.Text.Trim(),
            CharterPlannedMinutes = int.TryParse(CharterMinutesBox.Text, out var plannedMinutes) ? Math.Max(0, plannedMinutes) : 0
        };
        if (ReferenceFlowBox.SelectedItem is ReusableFlow referenceFlow)
        {
            session.ReferenceFlowId = referenceFlow.Id;
            session.ReferenceFlowName = referenceFlow.Name;
            session.ReferenceFlowSteps = referenceFlow.Steps.Select(x => new ReusableFlowStep { Order=x.Order, Kind=x.Kind, Target=x.Target, Description=x.Description, ControlName=x.ControlName, ControlType=x.ControlType, AutomationId=x.AutomationId, Important=x.Important }).ToList();
        }
        session.SeriesId = session.Id;
        _session = session;

        _desktopRecorder.Start();
        StartButton.IsEnabled = false;
        PauseButton.IsEnabled = true;
        FinishButton.IsEnabled = true;
        SessionNameBox.IsEnabled = false;
        TemplateBox.IsEnabled = false;
        CharterExpander.IsEnabled = false;
        ReferenceFlowBox.IsEnabled = false; ManageFlowsButton.IsEnabled = false;
        StateDot.Fill = new SolidColorBrush(Color.FromRgb(255, 101, 119));
        StateText.Text = _settings.UiLanguage.StartsWith("Esp", StringComparison.OrdinalIgnoreCase) ? "GRABANDO" : "RECORDING";
        _timer.Start();
        if (_settings.PeriodicScreenshot)
        {
            _periodic.Interval = TimeSpan.FromSeconds(Math.Max(10, _settings.PeriodicSeconds));
            _periodic.Tick -= Periodic_Tick;
            _periodic.Tick += Periodic_Tick;
            _periodic.Start();
        }
        RefreshMetrics();
        StatusBar.Text = $"{Localizer.T("SessionStarted", _settings.UiLanguage)}: {_session.DisplayName}";
        StartRecordingUiMode();
        await Task.CompletedTask;
    }

    private async void SessionTimer_Tick(object? sender, EventArgs e)
    {
        RefreshMetrics();
        if (_session is { State: SessionState.Recording } && _session.Duration >= PartLimit && !_isSplitting)
            await SplitSessionAsync();
    }

    private async Task SplitSessionAsync()
    {
        if (_session is null || _isSplitting || _session.State != SessionState.Recording) return;
        _isSplitting = true;
        try
        {
            var current = _session;
            current.State = SessionState.Paused;
            current.IsMultipart = true;
            current.BaseName = string.IsNullOrWhiteSpace(current.BaseName) ? current.Name : current.BaseName;
            if (current.SeriesId == Guid.Empty) current.SeriesId = current.Id;
            _floatingBar?.SetPaused(true);

            RestoreMainWindow();
            var es = _settings.UiLanguage.StartsWith("Esp", StringComparison.OrdinalIgnoreCase);
            AppDialogWindow.ShowInfo(this,
                es ? "Límite de sesión alcanzado" : "Session part limit reached",
                es
                    ? $"Esta parte alcanzó 2 horas. QAFlow guardará la Parte {current.PartNumber} y continuará automáticamente en la Parte {current.PartNumber + 1}, asociada a la misma sesión principal."
                    : $"This part reached 2 hours. QAFlow will save Part {current.PartNumber} and automatically continue with Part {current.PartNumber + 1}, linked to the same main session.",
                _settings.UiLanguage);

            current.State = SessionState.Finished;
            current.FinishedAt = DateTimeOffset.Now;
            current.Events = Deduplicator.Process(current.Events);

            var next = new TestSession
            {
                Name = current.BaseName,
                BaseName = current.BaseName,
                Tester = current.Tester,
                Template = current.Template,
                StartedAt = DateTimeOffset.Now,
                Resolution = current.Resolution,
                State = SessionState.Recording,
                SeriesId = current.SeriesId,
                PreviousPartId = current.Id,
                PartNumber = current.PartNumber + 1,
                IsMultipart = true
            };
            current.NextPartId = next.Id;
            await _store.SaveAsync(current);
            _session = next;
            await _store.SaveAsync(next);
            await ReloadHistoryAsync();

            SessionNameBox.Text = next.BaseName;
            RefreshMetrics();
            StateDot.Fill = new SolidColorBrush(Color.FromRgb(255, 101, 119));
            StateText.Text = es ? $"GRABANDO · PARTE {next.PartNumber}" : $"RECORDING · PART {next.PartNumber}";
            StatusBar.Text = es ? $"Continuando en Parte {next.PartNumber}" : $"Continuing with Part {next.PartNumber}";
            RebuildFloatingBar();
            Hide();
        }
        finally
        {
            _isSplitting = false;
        }
    }

    private async void Periodic_Tick(object? sender, EventArgs e)
    {
        if (_session is { State: SessionState.Recording }) await CaptureDesktopEvidenceAsync(ScreenshotKind.Periodic);
    }

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        if (_session.State == SessionState.Recording)
        {
            _session.State = SessionState.Paused;
            _session.PauseStartedAt = DateTimeOffset.Now;
            PauseButton.Content = "▶  " + Localizer.T("Resume", _settings.UiLanguage);
            StateDot.Fill = new SolidColorBrush(Color.FromRgb(244, 184, 96));
            StateText.Text = _settings.UiLanguage.StartsWith("Esp", StringComparison.OrdinalIgnoreCase) ? "PAUSADO" : "PAUSED";
            _floatingBar?.SetPaused(true);
        }
        else if (_session.State == SessionState.Paused)
        {
            if (_session.PauseStartedAt is { } p) _session.PausedTime += DateTimeOffset.Now - p;
            _session.PauseStartedAt = null;
            _session.State = SessionState.Recording;
            PauseButton.Content = "⏸  " + Localizer.T("Pause", _settings.UiLanguage);
            StateDot.Fill = new SolidColorBrush(Color.FromRgb(255, 101, 119));
            StateText.Text = _settings.UiLanguage.StartsWith("Esp", StringComparison.OrdinalIgnoreCase) ? "GRABANDO" : "RECORDING";
            _floatingBar?.SetPaused(false);
        }
    }

    private async void Finish_Click(object sender, RoutedEventArgs e)
    {
        if (!_isFinishing) await FinishSessionAsync(true);
    }

    private async Task FinishSessionAsync(bool showExport)
    {
        if (_session is null || _isFinishing || _session.State == SessionState.Finished) return;
        _isFinishing = true;
        FinishButton.IsEnabled = false;
        try
        {
            if (_session.State == SessionState.Paused && _session.PauseStartedAt is { } p)
                _session.PausedTime += DateTimeOffset.Now - p;

            try { await CaptureDesktopEvidenceAsync(ScreenshotKind.Final); } catch { }
            _desktopRecorder.Stop();
            _timer.Stop();
            _periodic.Stop();

            _session.State = SessionState.Finished;
            _session.FinishedAt = DateTimeOffset.Now;
            _session.Events = Deduplicator.Process(_session.Events);
            await _store.SaveAsync(_session);
            await ReloadHistoryAsync();

            StartButton.IsEnabled = true;
            PauseButton.IsEnabled = false;
            SessionNameBox.IsEnabled = true;
            TemplateBox.IsEnabled = true;
            CharterExpander.IsEnabled = true;
            ReferenceFlowBox.IsEnabled = true; ManageFlowsButton.IsEnabled = true;
            StateDot.Fill = new SolidColorBrush(Color.FromRgb(61, 220, 151));
            StateText.Text = Localizer.T("Finished", _settings.UiLanguage);
            RefreshMetrics();

            CloseFloatingBar();
            RestoreMainWindow();

            if (showExport)
            {
                var allForReview = await _store.LoadAllAsync();
                var reviewSeriesId = _session.SeriesId == Guid.Empty ? _session.Id : _session.SeriesId;
                var reviewParts = allForReview
                    .Where(s => (s.SeriesId == Guid.Empty ? s.Id : s.SeriesId) == reviewSeriesId)
                    .OrderBy(s => s.PartNumber)
                    .ToList();
                if (!reviewParts.Any(s => s.Id == _session.Id)) reviewParts.Add(_session);

                foreach (var part in reviewParts.OrderBy(s => s.PartNumber))
                {
                    if (!part.Events.Any(ReportLanguage.IsAction)) continue;
                    var review = new SessionReviewWindow(part, _settings.UiLanguage) { Owner = this };
                    if (review.ShowDialog() != true) return;
                    await _store.SaveAsync(part);
                    if (part.Id == _session.Id) _session = part;
                }

                await ReloadReferenceFlowsAsync();
                var dlg = new ExportWindow(_session, _settings.UiLanguage) { Owner = this };
                if (dlg.ShowDialog() == true) await ExportSessionAsync(dlg);
            }
        }
        catch (Exception ex)
        {
            var es = _settings.UiLanguage.StartsWith("Esp", StringComparison.OrdinalIgnoreCase);
            AppDialogWindow.ShowError(this, es ? "No se pudo finalizar la sesión" : "Unable to finish session", ex.Message, _settings.UiLanguage);
        }
        finally
        {
            _isFinishing = false;
            FinishButton.IsEnabled = _session is { State: SessionState.Recording or SessionState.Paused };
        }
    }

    private async Task ExportSessionAsync(ExportWindow dlg)
    {
        if (_session is null) return;
        var es = _settings.UiLanguage.StartsWith("Esp", StringComparison.OrdinalIgnoreCase);
        try
        {
            StatusBar.Text = Localizer.T("Exporting", _settings.UiLanguage);
            var all = await _store.LoadAllAsync();
            var seriesId = _session.SeriesId == Guid.Empty ? _session.Id : _session.SeriesId;
            var parts = all.Where(s => (s.SeriesId == Guid.Empty ? s.Id : s.SeriesId) == seriesId).OrderBy(s => s.PartNumber).ToList();
            if (!parts.Any(s => s.Id == _session.Id)) parts.Add(_session);
            parts = parts.OrderBy(s => s.PartNumber).ToList();

            var path = await new ExportService(_settings).ExportSeriesAsync(parts, dlg.ExportTc, dlg.ExportBug, dlg.ExportPdf, dlg.ExportZip);
            StatusBar.Text = $"{Localizer.T("Exported", _settings.UiLanguage)}: {path}";
            var open = AppDialogWindow.ShowQuestion(this, "QAFlow Recorder",
                es ? $"Exportación completada.\n\n¿Deseas abrir la ubicación?" : "Export completed.\n\nOpen the location?",
                _settings.UiLanguage);
            if (open) Process.Start(new ProcessStartInfo("explorer.exe", dlg.ExportZip ? $"/select,\"{path}\"" : $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppDialogWindow.ShowError(this, es ? "Error de exportación" : "Export error", ex.Message, _settings.UiLanguage);
        }
    }

    private async Task AnnotateAsync()
    {
        if (_session is null || _session.State != SessionState.Recording)
        {
            AppDialogWindow.ShowInfo(this, "QAFlow Recorder", _settings.UiLanguage.StartsWith("Esp", StringComparison.OrdinalIgnoreCase) ? "Inicia una sesión antes de anotar." : "Start a session before adding an annotation.", _settings.UiLanguage);
            return;
        }
        var dlg = new AnnotationWindow(_settings.UiLanguage) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Result is null) return;
        var a = dlg.Result;
        a.Timestamp = DateTimeOffset.Now;
        var ev = _session.Events.LastOrDefault();
        a.EventId = ev?.Id;
        if (ev is not null) ev.IsAnnotationPoint = true;
        if (dlg.CaptureRequested) a.ScreenshotPath = await CaptureDesktopEvidenceAsync(ScreenshotKind.Annotation, ev?.TargetTitle) ?? "";
        _session.Annotations.Add(a);
        RefreshMetrics();
        StatusBar.Text = Localizer.T("AnnotationSaved", _settings.UiLanguage);
    }

    private async Task CaptureManualAsync() => await CaptureDesktopEvidenceAsync(ScreenshotKind.Manual);

    private async Task ReportBugAsync()
    {
        if (_session is null || _session.State is not (SessionState.Recording or SessionState.Paused)) return;
        var wasRecording = _session.State == SessionState.Recording;
        if (wasRecording) Pause_Click(this, new RoutedEventArgs());
        var targetHwnd = _desktopRecorder.LastTargetWindow;
        var targetTitle = _desktopRecorder.LastTargetTitle;
        try
        {
            if (_floatingBar is not null) _floatingBar.Hide();
            if (IsVisible) Hide();
            await Task.Delay(250);
            if (targetHwnd == IntPtr.Zero || DesktopCaptureService.IsHandleOwnedByCurrentProcess(targetHwnd))
            {
                targetHwnd = DesktopCaptureService.GetForegroundWindowHandle();
                targetTitle = DesktopCaptureService.GetWindowTitle(targetHwnd);
            }
            var bitmap = DesktopCaptureService.CaptureWindowFromScreen(targetHwnd) ?? DesktopCaptureService.CapturePrimaryScreen();
            if (bitmap is null) return;

            var bugFolder = Path.Combine(_store.SessionFolder(_session), "bugs", DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
            Directory.CreateDirectory(bugFolder);
            var original = DesktopCaptureService.SavePng(bitmap, bugFolder, "original");
            var markup = new ScreenshotMarkupWindow(bitmap, bugFolder, _settings.UiLanguage);
            if (markup.ShowDialog() != true) return;
            var marked = string.IsNullOrWhiteSpace(markup.SavedPath) ? original : markup.SavedPath;
            var details = new BugDetailsWindow(marked, bugFolder, _settings.UiLanguage, "", targetTitle);
            if (details.ShowDialog() == true && details.Bug is not null)
            {
                details.Bug.OriginalScreenshotPath = original;
                if (string.IsNullOrWhiteSpace(details.Bug.MarkedScreenshotPath)) details.Bug.MarkedScreenshotPath = marked;
                details.Bug.Url = "";
                details.Bug.Timestamp = DateTimeOffset.Now;
                _session.Bugs.Add(details.Bug);
                await _store.SaveAsync(_session);
                StatusBar.Text = _settings.UiLanguage.StartsWith("Esp", StringComparison.OrdinalIgnoreCase) ? $"Bug guardado: {_session.Bugs.Count}" : $"Bug saved: {_session.Bugs.Count}";
            }
        }
        finally
        {
            if (_floatingBar is not null) _floatingBar.Show();
            if (wasRecording && _session?.State == SessionState.Paused) Pause_Click(this, new RoutedEventArgs());
            if (_settings.ShowFloatingBar) Hide(); else RestoreMainWindow();
        }
    }

    private void RefreshMetrics()
    {
        if (_session is null)
        {
            TimeMetric.Text = "00:00:00"; ActionsMetric.Text = "0"; PagesMetric.Text = "0"; NotesMetric.Text = "0 / 0"; return;
        }
        TimeMetric.Text = _session.DurationText;
        ActionsMetric.Text = _session.ActionCount.ToString();
        PagesMetric.Text = _session.PageCount.ToString();
        NotesMetric.Text = $"{_session.Annotations.Count} / {_session.ErrorCount}";
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string name) return;
        if (name == "Projects")
        {
            OpenAutomationWorkspace();
            return;
        }
        ShowView(name);
    }

    private void OpenAutomationWorkspace()
    {
        var capabilities = new CapabilityManager(_settings);
        if (!capabilities.AutomationEnabled)
        {
            AppDialogWindow.ShowInfo(this, "QAFlow Recorder",
                _settings.UiLanguage.StartsWith("Esp", StringComparison.OrdinalIgnoreCase)
                    ? "Activa Hybrid Testing en Settings para usar Projects, Suites y Test Cases."
                    : "Enable Hybrid Testing in Settings to use Projects, Suites and Test Cases.",
                _settings.UiLanguage);
            return;
        }
        if (!capabilities.TryValidateAutomationConfiguration(out var error))
        {
            AppDialogWindow.ShowInfo(this, "QAFlow Recorder",
                _settings.UiLanguage.StartsWith("Esp", StringComparison.OrdinalIgnoreCase)
                    ? "Configura una URL base http/https válida antes de abrir el espacio de automatización."
                    : error,
                _settings.UiLanguage);
            ShowView("Settings");
            AutomationBaseUrlBox.Focus();
            return;
        }
        var window = new AutomationWorkspaceWindow(_settings) { Owner = this };
        window.ShowDialog();
    }

    private void ShowView(string name)
    {
        RecorderView.Visibility = name == "Recorder" ? Visibility.Visible : Visibility.Collapsed;
        HistoryView.Visibility = name == "History" ? Visibility.Visible : Visibility.Collapsed;
        SettingsView.Visibility = name == "Settings" ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task ReloadHistoryAsync()
    {
        _history = await _store.LoadAllAsync();
        HistoryGrid.ItemsSource = _history;
    }

    private void HistorySearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        var q = HistorySearch.Text.Trim();
        HistoryGrid.ItemsSource = string.IsNullOrWhiteSpace(q)
            ? _history
            : _history.Where(s => s.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase) || s.StartedDisplay.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private void HistoryGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (HistoryGrid.SelectedItem is not TestSession s) return;
        _session = s;
        SessionNameBox.Text = string.IsNullOrWhiteSpace(s.BaseName) ? s.Name : s.BaseName;
        TemplateBox.SelectedIndex = s.Template switch { "Regression" => 1, "UAT" => 2, "Smoke" => 3, "Exploratory" => 4, _ => 0 };
        RefreshMetrics();
        ShowView("Recorder");
        StateText.Text = Localizer.T("Historical", _settings.UiLanguage);
    }

    private async Task ReloadReferenceFlowsAsync()
    {
        _reusableFlows = await new ReusableFlowLibraryService().LoadAsync();
        var selected = ReferenceFlowBox.SelectedValue is Guid g ? g : (Guid?)null;
        ReferenceFlowBox.ItemsSource = null;
        ReferenceFlowBox.ItemsSource = _reusableFlows;
        if (selected is Guid id) ReferenceFlowBox.SelectedValue = id;
    }

    private async void ManageFlows_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new FlowLibraryWindow(_settings.UiLanguage) { Owner = this };
        if (dlg.ShowDialog() == true && dlg.SelectedFlow is not null)
        {
            await ReloadReferenceFlowsAsync();
            ReferenceFlowBox.SelectedValue = dlg.SelectedFlow.Id;
        }
        else await ReloadReferenceFlowsAsync();
    }

    private void LoadSettingsToUi()
    {
        QaNameBox.Text = _settings.QaName;
        FloatingBarSettingsCheck.IsChecked = _settings.ShowFloatingBar;
        ShowFloatingBarCheck.IsChecked = _settings.ShowFloatingBar;
        PeriodicShotCheck.IsChecked = _settings.PeriodicScreenshot;
        PeriodicSecondsBox.Text = _settings.PeriodicSeconds.ToString();
        AppendixCheck.IsChecked = _settings.IncludeTechnicalAppendix;
        QualitySlider.Value = _settings.ImageQuality;
        LanguageBox.SelectedIndex = _settings.DocumentLanguage.StartsWith("Esp", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        UiLanguageBox.SelectedIndex = _settings.UiLanguage.StartsWith("Esp", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        FileTemplateBox.Text = _settings.FileNameTemplate;
        ExportFolderBox.Text = _settings.ExportFolder;
        PrivacyMaskBox.SelectedIndex = _settings.PrivacyMasking switch { PrivacyMaskMode.Manual => 1, PrivacyMaskMode.Off => 2, _ => 0 };
        CoverageTargetsBox.Text = _settings.ExpectedCoverageTargets;
        VisualComparisonCheck.IsChecked = _settings.IncludeVisualComparison;
        EvidenceQualityCheck.IsChecked = _settings.IncludeEvidenceQuality;
        ProductModeBox.SelectedIndex = _settings.ProductMode == ProductMode.HybridTesting ? 1 : 0;
        AutomationBaseUrlBox.Text = _settings.WebAutomationBaseUrl;
        AutomationAllowedDomainsBox.Text = _settings.WebAutomationAllowedDomains;
        AutomationBrowserBox.SelectedIndex = string.Equals(_settings.WebAutomationDefaultBrowser, "Edge", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        CaptureOutOfScopeCheck.IsChecked = _settings.CaptureOutOfScopeAsEvidence;
        RefreshProductModeUi();
        RefreshCapabilityNavigation();
    }

    private async void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        _settings.QaName = QaNameBox.Text.Trim();
        _settings.ShowFloatingBar = FloatingBarSettingsCheck.IsChecked == true;
        ShowFloatingBarCheck.IsChecked = _settings.ShowFloatingBar;
        _settings.PeriodicScreenshot = PeriodicShotCheck.IsChecked == true;
        _settings.PeriodicSeconds = int.TryParse(PeriodicSecondsBox.Text, out var sec) ? Math.Max(10, sec) : 30;
        _settings.IncludeTechnicalAppendix = AppendixCheck.IsChecked == true;
        _settings.ImageQuality = (int)QualitySlider.Value;
        _settings.DocumentLanguage = LanguageBox.SelectedIndex == 1 ? "Español" : "English";
        _settings.PrivacyMasking = PrivacyMaskBox.SelectedIndex switch { 1 => PrivacyMaskMode.Manual, 2 => PrivacyMaskMode.Off, _ => PrivacyMaskMode.Automatic };
        _settings.ExpectedCoverageTargets = CoverageTargetsBox.Text.Trim();
        _settings.IncludeVisualComparison = VisualComparisonCheck.IsChecked == true;
        _settings.IncludeEvidenceQuality = EvidenceQualityCheck.IsChecked == true;
        _settings.FileNameTemplate = string.IsNullOrWhiteSpace(FileTemplateBox.Text) ? "qa-report-{sesion}-{fecha}.pdf" : FileTemplateBox.Text.Trim();
        _settings.ExportFolder = string.IsNullOrWhiteSpace(ExportFolderBox.Text) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "QAFlow Recorder") : ExportFolderBox.Text.Trim();
        _settings.ProductMode = ProductModeBox.SelectedIndex == 1 ? ProductMode.HybridTesting : ProductMode.RecorderOnly;
        _settings.WebAutomationBaseUrl = AutomationBaseUrlBox.Text.Trim();
        _settings.WebAutomationAllowedDomains = AutomationAllowedDomainsBox.Text.Trim();
        _settings.WebAutomationDefaultBrowser = AutomationBrowserBox.SelectedIndex == 1 ? "Edge" : "Chrome";
        _settings.CaptureOutOfScopeAsEvidence = CaptureOutOfScopeCheck.IsChecked == true;

        var capabilities = new CapabilityManager(_settings);
        if (!capabilities.TryValidateAutomationConfiguration(out var automationError))
        {
            StatusBar.Text = _settings.UiLanguage.StartsWith("Esp", StringComparison.OrdinalIgnoreCase)
                ? "Configura una URL http/https válida para habilitar la automatización web."
                : automationError;
            AutomationBaseUrlBox.Focus();
            return;
        }

        await _store.SaveSettingsAsync(_settings);
        _webBridge?.UpdateSettings(_settings);
        RefreshCapabilityNavigation();
        StatusBar.Text = Localizer.T("SettingsSaved", _settings.UiLanguage);
    }

    private void ProductModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        RefreshProductModeUi();
    }

    private void RefreshProductModeUi()
    {
        if (AutomationSettingsPanel is null || ProductModeBox is null) return;
        var hybrid = ProductModeBox.SelectedIndex == 1;
        AutomationSettingsPanel.Visibility = hybrid ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshCapabilityNavigation()
    {
        if (NavProjects is null) return;
        var enabled = _settings.ProductMode == ProductMode.HybridTesting;
        NavProjects.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        NavProjects.IsEnabled = enabled;
    }

    private void UiLanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        _settings.UiLanguage = UiLanguageBox.SelectedIndex == 1 ? "Español" : "English";
        ApplyLanguage();
    }

    private void LanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        _settings.DocumentLanguage = LanguageBox.SelectedIndex == 1 ? "Español" : "English";
    }

    private void ApplyLanguage()
    {
        var l = _settings.UiLanguage;
        var es = l.StartsWith("Esp", StringComparison.OrdinalIgnoreCase);
        ProductSubtitleText.Text = es ? "Grabador de evidencia QA de escritorio" : "Desktop QA evidence recorder";
        if (_session is null) StateText.Text = Localizer.T("NoSession", l);
        NavRecorder.Content = "●  " + Localizer.T("Recorder", l);
        NavProjects.Content = es ? "Proyectos" : "Projects";
        NavProjects.ToolTip = es ? "Proyectos de automatización web" : "Web automation projects";
        NavHistory.Content = Localizer.T("History", l);
        NavSettings.Content = Localizer.T("Settings", l);
        RecorderTitleText.Text = es ? "Captura tu flujo QA" : "Capture your QA flow";
        RecorderSubtitleText.Text = es ? "Trabaja con normalidad. QAFlow registra interacciones y evidencia visual del escritorio y crea un reporte QA claro." : "Work naturally. QAFlow records desktop interactions and visual evidence, then builds a clear QA report.";
        SessionNameLabel.Text = Localizer.T("SessionName", l);
        TestTypeLabel.Text = Localizer.T("TestType", l);
        CharterExpander.Header = es ? "Charter exploratorio (opcional)" : "Exploratory charter (optional)";
        CharterObjectiveLabel.Text = es ? "Objetivo" : "Objective";
        CharterScopeLabel.Text = es ? "Alcance" : "Scope";
        CharterRisksLabel.Text = es ? "Riesgos a observar" : "Risks to observe";
        CharterDataLabel.Text = es ? "Datos de prueba" : "Test data";
        CharterConstraintsLabel.Text = es ? "Restricciones / notas" : "Constraints / notes";
        CharterMinutesLabel.Text = es ? "Duración prevista (minutos)" : "Planned duration (minutes)";
        ReferenceFlowLabel.Text = es ? "Flujo de referencia (opcional)" : "Reference flow (optional)";
        ManageFlowsButton.Content = es ? "Biblioteca" : "Library";
        ShowFloatingBarCheck.Content = es ? "Mostrar barra de control durante la grabación" : "Show recording control bar";
        StartButton.Content = es ? "▶  Iniciar sesión QA" : "▶  Start QA session";
        PauseButton.Content = (_session?.State == SessionState.Paused ? "▶  " : "⏸  ") + (_session?.State == SessionState.Paused ? Localizer.T("Resume", l) : Localizer.T("Pause", l));
        FinishButton.Content = "■  " + Localizer.T("Finish", l);
        PrivacyTitleText.Text = es ? "LOCAL POR DISEÑO" : "LOCAL BY DESIGN";
        PrivacyBodyText.Text = es ? "La evidencia permanece en esta PC. QAFlow nunca guarda los caracteres que escribes." : "Evidence stays on this PC. QAFlow never records the characters you type.";
        TimeMetricLabel.Text = Localizer.T("Time", l).ToUpperInvariant();
        ActionsMetricLabel.Text = es ? "INTERACCIONES" : "INTERACTIONS";
        PagesMetricLabel.Text = es ? "OBJETIVOS" : "TARGETS";
        NotesMetricLabel.Text = es ? "HALLAZGOS" : "FINDINGS";
        HotkeysText.Text = es ? "Ctrl+Shift+R finalizar · Ctrl+Shift+A anotar · Ctrl+Shift+P pausar · Ctrl+Shift+B bug" : "Ctrl+Shift+R finish · Ctrl+Shift+A annotate · Ctrl+Shift+P pause · Ctrl+Shift+B bug";
        SessionNameBox.ToolTip = es ? "Ej. Validación de checkout" : "E.g. Checkout validation";

        HistoryTitleText.Text = Localizer.T("History", l);
        HistorySubtitleText.Text = es ? "Revisa sesiones guardadas y grabaciones divididas en partes." : "Review saved sessions and multipart recordings.";
        HistorySearch.ToolTip = es ? "Buscar por sesión o fecha" : "Search by session or date";
        if (HistoryGrid.Columns.Count >= 3)
        {
            HistoryGrid.Columns[0].Header = es ? "Sesión" : "Session";
            HistoryGrid.Columns[1].Header = es ? "Inicio" : "Started";
            HistoryGrid.Columns[2].Header = es ? "Duración" : "Duration";
        }

        SettingsTitleText.Text = Localizer.T("Settings", l);
        SettingsSubtitleText.Text = es ? "Idioma, evidencia, almacenamiento y capacidades del producto." : "Language, evidence, storage and product capabilities.";
        ProductModeTitleText.Text = es ? "Modo del producto" : "Product mode";
        ProductModeHelpText.Text = es ? "Usa QAFlow como grabador universal o habilita el espacio híbrido de automatización web." : "Keep QAFlow as a universal recorder, or enable the web-only hybrid automation workspace.";
        AutomationConfigTitleText.Text = es ? "Configuración de automatización web" : "Web automation configuration";
        AutomationConfigScopeText.Text = es ? "Automation v1 está limitada a aplicaciones web configuradas." : "Automation v1 is limited to configured web applications.";
        AutomationBaseUrlLabel.Text = es ? "Software bajo prueba · URL base" : "Software under test · Base URL";
        AutomationAllowedDomainsLabel.Text = es ? "Dominios permitidos / confiables (opcional)" : "Trusted / allowed domains (optional)";
        AutomationBrowserLabel.Text = es ? "Navegador predeterminado" : "Default browser";
        CaptureOutOfScopeCheck.Content = es ? "Capturar actividad fuera de la URL solo como evidencia" : "Capture activity outside the URL as evidence only";
        AutomationSafetyText.Text = es ? "La actividad de escritorio y sitios no relacionados nunca se convertirá en pasos automatizados en esta versión." : "Desktop and unrelated web activity will never be converted to automated steps in this version.";
        QaNameLabel.Text = Localizer.T("QaName", l);
        FloatingBarSettingsCheck.Content = es ? "Mostrar barra de control durante la grabación" : "Show recording control bar";
        PeriodicShotCheck.Content = es ? "Captura periódica de evidencia" : "Periodic evidence capture";
        IntervalLabel.Text = Localizer.T("Interval", l);
        AppendixCheck.Content = Localizer.T("Appendix", l);
        QualityLabel.Text = Localizer.T("Quality", l);
        DocLanguageLabel.Text = Localizer.T("DocLanguage", l);
        PrivacyMaskLabel.Text = es ? "Enmascarado de datos sensibles" : "Sensitive data masking";
        CoverageTargetsLabel.Text = es ? "Objetivos de cobertura (opcional)" : "Coverage targets (optional)";
        CoverageHelpText.Text = es ? "Si lo dejas vacío, QAFlow muestra la cobertura observada sin afirmar cobertura completa de la aplicación." : "If left empty, QAFlow reports the observed coverage footprint without claiming full application coverage.";
        VisualComparisonCheck.Content = es ? "Incluir comparación visual con baseline" : "Include baseline visual comparison";
        EvidenceQualityCheck.Content = es ? "Incluir evaluación de calidad de evidencia" : "Include evidence quality assessment";
        if (PrivacyMaskBox.Items.Count >= 3)
        {
            ((ComboBoxItem)PrivacyMaskBox.Items[0]).Content = es ? "Automático" : "Automatic";
            ((ComboBoxItem)PrivacyMaskBox.Items[1]).Content = es ? "Manual" : "Manual";
            ((ComboBoxItem)PrivacyMaskBox.Items[2]).Content = es ? "Desactivado" : "Off";
        }
        ExportFolderLabel.Text = Localizer.T("ExportFolder", l);
        FileTemplateLabel.Text = es ? "Plantilla de nombre de archivo" : "File name template";
        SaveSettingsButton.Content = Localizer.T("SaveSettings", l);
        PinButton.ToolTip = es ? "Mantener QAFlow siempre visible" : "Keep QAFlow always on top";
        StatusBar.Text = Localizer.T("Ready", l);

        if (TemplateBox.Items.Count >= 5)
        {
            ((ComboBoxItem)TemplateBox.Items[0]).Content = es ? "Funcional Normal" : "Functional Normal";
            ((ComboBoxItem)TemplateBox.Items[1]).Content = es ? "Regresión" : "Regression";
            ((ComboBoxItem)TemplateBox.Items[2]).Content = "UAT";
            ((ComboBoxItem)TemplateBox.Items[3]).Content = "Smoke";
            ((ComboBoxItem)TemplateBox.Items[4]).Content = es ? "Exploratorio" : "Exploratory";
        }
    }

    private void StartRecordingUiMode()
    {
        _settings.ShowFloatingBar = ShowFloatingBarCheck.IsChecked == true;
        FloatingBarSettingsCheck.IsChecked = _settings.ShowFloatingBar;
        RebuildFloatingBar();
        Hide();
    }

    private void RebuildFloatingBar()
    {
        CloseFloatingBar();
        if (!_settings.ShowFloatingBar || _session is null) return;
        _floatingBar = new FloatingRecorderBar(_session.DisplayName, () => _session?.DurationText ?? "00:00:00", _settings.UiLanguage);
        _floatingBar.PauseRequested += () => Dispatcher.Invoke(() => Pause_Click(this, new RoutedEventArgs()));
        _floatingBar.AnnotateRequested += () => Dispatcher.Invoke(async () => await AnnotateAsync());
        _floatingBar.CaptureRequested += () => Dispatcher.Invoke(async () => await CaptureManualAsync());
        _floatingBar.BugRequested += () => Dispatcher.Invoke(async () => await ReportBugAsync());
        _floatingBar.RestoreRequested += () => Dispatcher.Invoke(RestoreMainWindow);
        _floatingBar.FinishRequested += () => FinishSessionAsync(true);
        _floatingBar.Show();
    }

    private void RestoreMainWindow()
    {
        if (!IsVisible) Show();
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        DockToRight();
    }

    private void CloseFloatingBar()
    {
        if (_floatingBar is null) return;
        var bar = _floatingBar;
        _floatingBar = null;
        bar.Close();
    }

    private async void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != (ModifierKeys.Control | ModifierKeys.Shift)) return;
        if (e.Key == Key.R) { if (_session is { State: SessionState.Recording or SessionState.Paused }) await FinishSessionAsync(true); else await StartSessionAsync(); e.Handled = true; }
        else if (e.Key == Key.A) { await AnnotateAsync(); e.Handled = true; }
        else if (e.Key == Key.P) { Pause_Click(this, new RoutedEventArgs()); e.Handled = true; }
        else if (e.Key == Key.B) { await ReportBugAsync(); e.Handled = true; }
        else if (e.Key == Key.O) { RestoreMainWindow(); e.Handled = true; }
    }

    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        PinButton.Opacity = Topmost ? 1.0 : 0.45;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Left) DragMove(); }
    private void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        _hwndSource = HwndSource.FromHwnd(hwnd);
        _hwndSource?.AddHook(WndProc);
        RegisterHotKey(hwnd, HotkeyToggle, ModControl | ModShift, (uint)KeyInterop.VirtualKeyFromKey(Key.R));
        RegisterHotKey(hwnd, HotkeyAnnotate, ModControl | ModShift, (uint)KeyInterop.VirtualKeyFromKey(Key.A));
        RegisterHotKey(hwnd, HotkeyPause, ModControl | ModShift, (uint)KeyInterop.VirtualKeyFromKey(Key.P));
        RegisterHotKey(hwnd, HotkeyHistory, ModControl | ModShift, (uint)KeyInterop.VirtualKeyFromKey(Key.L));
        RegisterHotKey(hwnd, HotkeyRestore, ModControl | ModShift, (uint)KeyInterop.VirtualKeyFromKey(Key.O));
        RegisterHotKey(hwnd, HotkeyBug, ModControl | ModShift, (uint)KeyInterop.VirtualKeyFromKey(Key.B));
    }

    private async void HandleHotkey(int id)
    {
        switch (id)
        {
            case HotkeyToggle: if (_session is { State: SessionState.Recording or SessionState.Paused }) await FinishSessionAsync(true); else { RestoreMainWindow(); await StartSessionAsync(); } break;
            case HotkeyAnnotate: await AnnotateAsync(); break;
            case HotkeyPause: Pause_Click(this, new RoutedEventArgs()); break;
            case HotkeyHistory: RestoreMainWindow(); ShowView("History"); break;
            case HotkeyRestore: RestoreMainWindow(); break;
            case HotkeyBug: await ReportBugAsync(); break;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey) { handled = true; HandleHotkey(wParam.ToInt32()); }
        return IntPtr.Zero;
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        CloseFloatingBar();
        _desktopRecorder.Stop();
        if (_webBridge is not null)
        {
            _webBridge.ObservationReceived -= WebBridge_ObservationReceived;
            _webBridge.Dispose();
            _webBridge = null;
        }
        var hwnd = new WindowInteropHelper(this).Handle;
        foreach (var id in new[] { HotkeyToggle, HotkeyAnnotate, HotkeyPause, HotkeyHistory, HotkeyRestore, HotkeyBug }) UnregisterHotKey(hwnd, id);
        _hwndSource?.RemoveHook(WndProc);
    }

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
