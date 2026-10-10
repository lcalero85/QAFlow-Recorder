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
    private List<SessionTemplateDefinition> _sessionTemplates = [];
    private FloatingRecorderBar? _floatingBar;
    private bool _isFinishing;
    private bool _isSplitting;
    private HwndSource? _hwndSource;
    private WebInstrumentationBridge? _webBridge;
    private List<DisplayMonitorInfo> _displayMonitors = [];
    private CaptureAreaSelection? _customCaptureArea;
    private DateTimeOffset _lastRecoverySave = DateTimeOffset.MinValue;
    private VideoRecordingService? _videoRecorder;

    private static readonly TimeSpan PartLimit = TimeSpan.FromHours(2);
    private const int HotkeyToggle = 101;
    private const int HotkeyAnnotate = 102;
    private const int HotkeyPause = 103;
    private const int HotkeyHistory = 104;
    private const int HotkeyRestore = 105;
    private const int HotkeyBug = 106;
    private const int HotkeyBookmark = 107;
    private const int HotkeyChecklist = 108;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint WmHotkey = 0x0312;

    public MainWindow()
    {
        InitializeComponent();
        // Position the panel before WPF shows the native window. Doing this in
        // Loaded caused a visible jump from the default launch position to the
        // right edge of the work area.
        DockToRight();
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
        RefreshRecordingScreens();
        await ReloadHistoryAsync();
        await ReloadReferenceFlowsAsync();
        await ReloadSessionTemplatesAsync();
        ApplyLanguage();
        StateText.Text = Localizer.T("NoSession", _settings.UiLanguage);
        await TryRecoverInterruptedSessionAsync();
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
            var captureBounds = GetActiveCaptureBounds();
            var bitmap = CaptureConfiguredBitmap(targetWindow);
            if (bitmap is null) return;
            if (_settings.PrivacyMasking == PrivacyMaskMode.Automatic && ev.Kind == EventKind.Input) ev.MaskSensitiveEvidence = true;
            var protectedBitmap = PrivacyMaskService.Apply(bitmap, ev, targetWindow, _settings.PrivacyMasking, captureBounds);
            var zoneProtectedBitmap = PrivacyMaskService.ApplyZones(protectedBitmap, _settings.PrivacyZones, ev.WindowTitle);
            var path = DesktopCaptureService.SavePng(zoneProtectedBitmap, _store.EvidenceFolder(_session), "interaction");
            _session.Screenshots.Add(new EvidenceShot
            {
                Timestamp = DateTimeOffset.Now,
                Kind = ScreenshotKind.FlowAction,
                TargetTitle = ev.TargetTitle,
                Url = "",
                FilePath = path,
                FullPage = false,
                EventId = ev.Id,
                SensitiveMaskApplied = !ReferenceEquals(protectedBitmap, bitmap) || !ReferenceEquals(zoneProtectedBitmap, protectedBitmap)
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
            var bitmap = CaptureConfiguredBitmap(hwnd);
            if (bitmap is null) return null;
            var windowTitle = DesktopCaptureService.GetWindowTitle(hwnd);
            var protectedBitmap = PrivacyMaskService.ApplyZones(bitmap, _settings.PrivacyZones, windowTitle);
            var path = DesktopCaptureService.SavePng(protectedBitmap, _store.EvidenceFolder(_session), kind.ToString().ToLowerInvariant());
            _session.Screenshots.Add(new EvidenceShot
            {
                Timestamp = DateTimeOffset.Now,
                Kind = kind,
                TargetTitle = string.IsNullOrWhiteSpace(targetTitle) ? _desktopRecorder.LastTargetTitle : targetTitle!,
                Url = "",
                FilePath = path,
                FullPage = false,
                SensitiveMaskApplied = !ReferenceEquals(protectedBitmap, bitmap)
            });
            return path;
        }
        finally
        {
            if (barWasVisible && _floatingBar is not null) _floatingBar.Show();
        }
    }

    private void RefreshRecordingScreens()
    {
        var previousDevice = (RecordingScreenBox.SelectedItem as DisplayMonitorInfo)?.DeviceName;
        _displayMonitors = DesktopCaptureService.GetMonitors().ToList();
        var es = _settings.UiLanguage.StartsWith("Esp", StringComparison.OrdinalIgnoreCase);
        for (var i = 0; i < _displayMonitors.Count; i++)
        {
            var m = _displayMonitors[i];
            m.DisplayName = $"{(m.IsPrimary ? "★ " : "")}{(es ? "Pantalla" : "Screen")} {i + 1} · {m.Bounds.Width}×{m.Bounds.Height}{(m.IsPrimary ? (es ? " · Principal" : " · Primary") : "")}";
        }
        RecordingScreenBox.ItemsSource = _displayMonitors;
        var selected = _displayMonitors.FirstOrDefault(x => x.DeviceName.Equals(previousDevice, StringComparison.OrdinalIgnoreCase))
                       ?? _displayMonitors.FirstOrDefault(x => x.IsPrimary)
                       ?? _displayMonitors.FirstOrDefault();
        RecordingScreenBox.SelectedItem = selected;
        if (_customCaptureArea is not null && selected is not null && !_customCaptureArea.MonitorDeviceName.Equals(selected.DeviceName, StringComparison.OrdinalIgnoreCase))
            _customCaptureArea = null;
        UpdateCaptureAreaStatus();
    }

    private DisplayMonitorInfo? SelectedRecordingMonitor => RecordingScreenBox.SelectedItem as DisplayMonitorInfo;

    private Int32Rect? GetActiveCaptureBounds()
    {
        if (_customCaptureArea is { IsValid: true }) return _customCaptureArea.Bounds;
        return SelectedRecordingMonitor?.Bounds;
    }

    private string GetCaptureResolutionText()
    {
        var bounds = GetActiveCaptureBounds();
        return bounds is { } b ? $"{b.Width}x{b.Height}" : $"{SystemParameters.PrimaryScreenWidth:0}x{SystemParameters.PrimaryScreenHeight:0}";
    }

    private System.Windows.Media.Imaging.BitmapSource? CaptureConfiguredBitmap(IntPtr fallbackWindow)
    {
        if (_customCaptureArea is { IsValid: true })
            return DesktopCaptureService.CaptureRegion(_customCaptureArea.Bounds);
        if (SelectedRecordingMonitor is { } monitor)
            return DesktopCaptureService.CaptureMonitor(monitor);
        return DesktopCaptureService.CaptureWindowFromScreen(fallbackWindow) ?? DesktopCaptureService.CapturePrimaryScreen();
    }

    private void RefreshScreens_Click(object sender, RoutedEventArgs e) => RefreshRecordingScreens();

    private void RecordingScreenBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (_customCaptureArea is not null && SelectedRecordingMonitor is { } monitor &&
            !_customCaptureArea.MonitorDeviceName.Equals(monitor.DeviceName, StringComparison.OrdinalIgnoreCase))
            _customCaptureArea = null;
        UpdateCaptureAreaStatus();
    }

    private void SelectCaptureArea_Click(object sender, RoutedEventArgs e)
    {
        var monitor = SelectedRecordingMonitor;
        if (monitor is null)
        {
            RefreshRecordingScreens();
            monitor = SelectedRecordingMonitor;
        }
        if (monitor is null) return;

        var wasVisible = IsVisible;
        Hide();
        try
        {
            var selector = new CaptureAreaSelectorWindow(monitor, _settings.UiLanguage);
            if (selector.ShowDialog() == true && selector.Selection is { IsValid: true } selection)
                _customCaptureArea = selection;
        }
        finally
        {
            if (wasVisible) { Show(); Activate(); }
            UpdateCaptureAreaStatus();
        }
    }

    private void ClearCaptureArea_Click(object sender, RoutedEventArgs e)
    {
        _customCaptureArea = null;
        UpdateCaptureAreaStatus();
    }

    private void UpdateCaptureAreaStatus()
    {
        if (CaptureAreaStatusText is null) return;
        var es = _settings.UiLanguage.StartsWith("Esp", StringComparison.OrdinalIgnoreCase);
        var monitor = SelectedRecordingMonitor;
        if (_customCaptureArea is { IsValid: true } area)
        {
            CaptureAreaStatusText.Text = es
                ? $"Área personalizada · {area.Bounds.Width}×{area.Bounds.Height} px · {monitor?.DisplayName ?? "pantalla"}"
                : $"Custom area · {area.Bounds.Width}×{area.Bounds.Height} px · {monitor?.DisplayName ?? "screen"}";
            ClearCaptureAreaButton.IsEnabled = _session is null || _session.State is not (SessionState.Recording or SessionState.Paused);
        }
        else
        {
            CaptureAreaStatusText.Text = es
                ? $"Pantalla completa · {monitor?.DisplayName ?? "pantalla principal"}"
                : $"Full screen · {monitor?.DisplayName ?? "primary screen"}";
            ClearCaptureAreaButton.IsEnabled = false;
        }
    }

    private async void Start_Click(object sender, RoutedEventArgs e) => await StartSessionAsync();

    private async Task StartSessionAsync()
    {
        if (_session is { State: SessionState.Recording or SessionState.Paused }) return;
        var baseName = string.IsNullOrWhiteSpace(SessionNameBox.Text)
            ? $"{(_settings.UiLanguage.StartsWith("Esp", StringComparison.OrdinalIgnoreCase) ? "Sesión QA" : "QA Session")} {DateTime.Now:yyyy-MM-dd HH:mm}"
            : SessionNameBox.Text.Trim();
        var initialHwnd = DesktopCaptureService.GetForegroundWindowHandle();
        var initialIsQaFlow = DesktopCaptureService.IsHandleOwnedByCurrentProcess(initialHwnd);
        var initialWindow = initialIsQaFlow ? "" : DesktopCaptureService.GetWindowTitle(initialHwnd);
        var initialApp = initialIsQaFlow ? "" : DesktopCaptureService.GetWindowProcessName(initialHwnd);

        var session = new TestSession
        {
            Name = baseName,
            BaseName = baseName,
            Folder = SessionFolderBox.Text.Trim(),
            Tags = SessionTagsBox.Text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Tester = _settings.QaName,
            Template = TemplateBox.SelectedIndex switch { 1 => "Regression", 2 => "UAT", 3 => "Smoke", 4 => "Exploratory", _ => "Functional Normal" },
            StartedAt = DateTimeOffset.Now,
            Resolution = GetCaptureResolutionText(),
            CaptureScreen = SelectedRecordingMonitor?.DisplayName ?? "Primary screen",
            CaptureDeviceName = SelectedRecordingMonitor?.DeviceName ?? "",
            CustomCaptureArea = _customCaptureArea is { IsValid: true },
            CaptureAreaX = _customCaptureArea?.Bounds.X ?? SelectedRecordingMonitor?.Bounds.X ?? 0,
            CaptureAreaY = _customCaptureArea?.Bounds.Y ?? SelectedRecordingMonitor?.Bounds.Y ?? 0,
            CaptureAreaWidth = GetActiveCaptureBounds()?.Width ?? 0,
            CaptureAreaHeight = GetActiveCaptureBounds()?.Height ?? 0,
            BuildVersion = BuildVersionBox.Text.Trim(),
            FunctionalRisk = RiskValue(FunctionalRiskBox.SelectedIndex),
            VisualRisk = RiskValue(VisualRiskBox.SelectedIndex),
            RegressionRisk = RiskValue(RegressionRiskBox.SelectedIndex),
            DataRisk = RiskValue(DataRiskBox.SelectedIndex),
            RiskSummaryNotes = RiskSummaryNotesBox.Text.Trim(),
            RecoveryCheckpointAt = DateTimeOffset.Now,
            VideoRecordingEnabled = RecordVideoCheck.IsChecked == true,
            VideoFrameRate = 4,
            EnvironmentSnapshot = new EnvironmentSnapshot
            {
                CapturedAt = DateTimeOffset.Now,
                OperatingSystem = Environment.OSVersion.VersionString,
                Resolution = GetCaptureResolutionText(),
                TimeZone = TimeZoneInfo.Local.DisplayName,
                MachineName = Environment.MachineName,
                ActiveApplication = initialApp,
                ActiveWindow = initialWindow,
                BuildVersion = BuildVersionBox.Text.Trim()
            },
            ManualChecklist = ChecklistDefinitionBox.Text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select((text, index) => new ManualChecklistItem { Order = index + 1, Text = text }).ToList(),
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
        if (_session.VideoRecordingEnabled) await StartVideoRecordingAsync(_session);
        StartButton.IsEnabled = false;
        PauseButton.IsEnabled = true;
        FinishButton.IsEnabled = true;
        SessionTemplateBox.IsEnabled = false; ManageSessionTemplatesButton.IsEnabled = false;
        SessionNameBox.IsEnabled = false;
        OrganizationExpander.IsEnabled = false;
        RiskSummaryExpander.IsEnabled = true;
        TemplateBox.IsEnabled = false;
        CharterExpander.IsEnabled = false;
        ChecklistExpander.IsEnabled = false;
        ReferenceFlowBox.IsEnabled = false; ManageFlowsButton.IsEnabled = false;
        RecordingScreenBox.IsEnabled = false; RefreshScreensButton.IsEnabled = false; SelectCaptureAreaButton.IsEnabled = false; ClearCaptureAreaButton.IsEnabled = false; RecordVideoCheck.IsEnabled = false;
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
        StatusBar.Text = _session.VideoRecordingEnabled
            ? $"{Localizer.T("SessionStarted", _settings.UiLanguage)}: {_session.DisplayName} · {(_settings.UiLanguage.StartsWith("Esp", StringComparison.OrdinalIgnoreCase) ? "video activo" : "video active")}"
            : $"{Localizer.T("SessionStarted", _settings.UiLanguage)}: {_session.DisplayName}";
        await SaveRecoveryCheckpointAsync(force: true);
        StartRecordingUiMode();
    }

    private async void SessionTimer_Tick(object? sender, EventArgs e)
    {
        RefreshMetrics();
        if (_session is { State: SessionState.Recording or SessionState.Paused } && DateTimeOffset.Now - _lastRecoverySave >= TimeSpan.FromSeconds(5))
            await SaveRecoveryCheckpointAsync();
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

            await StopVideoRecordingAsync();
            current.State = SessionState.Finished;
            current.FinishedAt = DateTimeOffset.Now;
            current.Events = Deduplicator.Process(current.Events);

            var next = new TestSession
            {
                Name = current.BaseName,
                BaseName = current.BaseName,
                Folder = current.Folder,
                Tags = [.. current.Tags],
                Tester = current.Tester,
                Template = current.Template,
                StartedAt = DateTimeOffset.Now,
                Resolution = current.Resolution,
                CaptureScreen = current.CaptureScreen,
                CaptureDeviceName = current.CaptureDeviceName,
                CustomCaptureArea = current.CustomCaptureArea,
                CaptureAreaX = current.CaptureAreaX,
                CaptureAreaY = current.CaptureAreaY,
                CaptureAreaWidth = current.CaptureAreaWidth,
                CaptureAreaHeight = current.CaptureAreaHeight,
                BuildVersion = current.BuildVersion,
                FunctionalRisk = current.FunctionalRisk,
                VisualRisk = current.VisualRisk,
                RegressionRisk = current.RegressionRisk,
                DataRisk = current.DataRisk,
                RiskSummaryNotes = current.RiskSummaryNotes,
                RecoveryCheckpointAt = DateTimeOffset.Now,
                VideoRecordingEnabled = current.VideoRecordingEnabled,
                VideoFrameRate = current.VideoFrameRate,
                EnvironmentSnapshot = current.EnvironmentSnapshot,
                ManualChecklist = current.ManualChecklist.Select(x => new ManualChecklistItem { Id=x.Id, Order=x.Order, Text=x.Text, Status=x.Status, Note=x.Note }).ToList(),
                State = SessionState.Recording,
                SeriesId = current.SeriesId,
                PreviousPartId = current.Id,
                PartNumber = current.PartNumber + 1,
                IsMultipart = true
            };
            current.NextPartId = next.Id;
            await _store.SaveAsync(current);
            _session = next;
            if (next.VideoRecordingEnabled) await StartVideoRecordingAsync(next);
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
            _videoRecorder?.Pause();
            _ = SaveRecoveryCheckpointAsync(force: true);
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
            _videoRecorder?.Resume();
            _ = SaveRecoveryCheckpointAsync(force: true);
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

            _session.FunctionalRisk = RiskValue(FunctionalRiskBox.SelectedIndex);
            _session.VisualRisk = RiskValue(VisualRiskBox.SelectedIndex);
            _session.RegressionRisk = RiskValue(RegressionRiskBox.SelectedIndex);
            _session.DataRisk = RiskValue(DataRiskBox.SelectedIndex);
            _session.RiskSummaryNotes = RiskSummaryNotesBox.Text.Trim();

            try { await CaptureDesktopEvidenceAsync(ScreenshotKind.Final); } catch { }
            _desktopRecorder.Stop();
            _timer.Stop();
            _periodic.Stop();
            await StopVideoRecordingAsync();

            _session.State = SessionState.Finished;
            _session.FinishedAt = DateTimeOffset.Now;
            _session.RecoveryCheckpointAt = DateTimeOffset.Now;
            _session.Events = Deduplicator.Process(_session.Events);
            await _store.SaveAsync(_session);
            await ReloadHistoryAsync();

            StartButton.IsEnabled = true;
            PauseButton.IsEnabled = false;
            SessionTemplateBox.IsEnabled = true; ManageSessionTemplatesButton.IsEnabled = true;
            SessionNameBox.IsEnabled = true;
            OrganizationExpander.IsEnabled = true;
            RiskSummaryExpander.IsEnabled = true;
            TemplateBox.IsEnabled = true;
            CharterExpander.IsEnabled = true;
            ChecklistExpander.IsEnabled = true;
            ReferenceFlowBox.IsEnabled = true; ManageFlowsButton.IsEnabled = true;
            RecordingScreenBox.IsEnabled = true; RefreshScreensButton.IsEnabled = true; SelectCaptureAreaButton.IsEnabled = true; ClearCaptureAreaButton.IsEnabled = _customCaptureArea is not null; RecordVideoCheck.IsEnabled = true;
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

            var path = await new ExportService(_settings).ExportSeriesAsync(parts, dlg.ExportTc, dlg.ExportBug, dlg.ExportPdf, dlg.ExportVideo, dlg.ExportZip);
            StatusBar.Text = $"{Localizer.T("Exported", _settings.UiLanguage)}: {Path.GetFileName(path)}";
            StatusBar.ToolTip = path;
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
            var bitmap = CaptureConfiguredBitmap(targetHwnd);
            if (bitmap is null) return;
            var protectedBitmap = PrivacyMaskService.ApplyZones(bitmap, _settings.PrivacyZones, targetTitle);

            var bugFolder = Path.Combine(_store.SessionFolder(_session), "bugs", DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
            Directory.CreateDirectory(bugFolder);
            var original = DesktopCaptureService.SavePng(protectedBitmap, bugFolder, "original");
            var markup = new ScreenshotMarkupWindow(protectedBitmap, bugFolder, _settings.UiLanguage);
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
            : _history.Where(s => s.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase)
                || s.StartedDisplay.Contains(q, StringComparison.OrdinalIgnoreCase)
                || s.Folder.Contains(q, StringComparison.OrdinalIgnoreCase)
                || s.Tags.Any(t => t.Contains(q, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    private async void DeleteHistorySelected_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryGrid.SelectedItem is not TestSession selected)
        {
            AppDialogWindow.ShowInfo(this, "QAFlow Recorder",
                _settings.UiLanguage.StartsWith("Esp", StringComparison.OrdinalIgnoreCase)
                    ? "Selecciona una sesión del historial para eliminarla."
                    : "Select a history session to delete.", _settings.UiLanguage);
            return;
        }

        if (_session?.Id == selected.Id && _session.State is SessionState.Recording or SessionState.Paused)
        {
            AppDialogWindow.ShowInfo(this, "QAFlow Recorder",
                _settings.UiLanguage.StartsWith("Esp", StringComparison.OrdinalIgnoreCase)
                    ? "No puedes eliminar una sesión mientras está activa."
                    : "An active session cannot be deleted.", _settings.UiLanguage);
            return;
        }

        var name = string.IsNullOrWhiteSpace(selected.DisplayName) ? selected.Name : selected.DisplayName;
        var confirm = AppDialogWindow.ShowQuestion(this, "QAFlow Recorder",
            _settings.UiLanguage.StartsWith("Esp", StringComparison.OrdinalIgnoreCase)
                ? $"¿Eliminar permanentemente la sesión '{name}' y su evidencia local?"
                : $"Permanently delete session '{name}' and its local evidence?", _settings.UiLanguage);
        if (!confirm) return;

        try
        {
            await _store.DeleteAsync(selected);
            if (_session?.Id == selected.Id && _session.State == SessionState.Finished) _session = null;
            await ReloadHistoryAsync();
            HistorySearch.Clear();
        }
        catch (Exception ex)
        {
            AppDialogWindow.ShowError(this, "QAFlow Recorder",
                (_settings.UiLanguage.StartsWith("Esp", StringComparison.OrdinalIgnoreCase) ? "No se pudo eliminar la sesión: " : "Could not delete the session: ") + ex.Message,
                _settings.UiLanguage);
        }
    }

    private async void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        if (_session?.State is SessionState.Recording or SessionState.Paused)
        {
            AppDialogWindow.ShowInfo(this, "QAFlow Recorder",
                _settings.UiLanguage.StartsWith("Esp", StringComparison.OrdinalIgnoreCase)
                    ? "Finaliza o pausa definitivamente la sesión activa antes de limpiar el historial."
                    : "Finish the active session before clearing history.", _settings.UiLanguage);
            return;
        }

        if (_history.Count == 0) return;
        var confirm = AppDialogWindow.ShowQuestion(this, "QAFlow Recorder",
            _settings.UiLanguage.StartsWith("Esp", StringComparison.OrdinalIgnoreCase)
                ? "¿Eliminar todo el historial de sesiones y su evidencia local? Esta acción no se puede deshacer."
                : "Delete all session history and its local evidence? This cannot be undone.", _settings.UiLanguage);
        if (!confirm) return;

        try
        {
            await _store.ClearHistoryAsync();
            _history.Clear();
            HistoryGrid.ItemsSource = null;
            HistoryGrid.ItemsSource = _history;
            HistorySearch.Clear();
            if (_session?.State == SessionState.Finished) _session = null;
        }
        catch (Exception ex)
        {
            AppDialogWindow.ShowError(this, "QAFlow Recorder",
                (_settings.UiLanguage.StartsWith("Esp", StringComparison.OrdinalIgnoreCase) ? "No se pudo limpiar el historial: " : "Could not clear history: ") + ex.Message,
                _settings.UiLanguage);
        }
    }

    private void TimelineHistory_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryGrid.SelectedItem is not TestSession selected)
        {
            AppDialogWindow.ShowInfo(this, "QAFlow Recorder",
                _settings.UiLanguage.StartsWith("Esp", StringComparison.OrdinalIgnoreCase) ? "Selecciona una sesión para abrir su línea de tiempo." : "Select a session to open its evidence timeline.",
                _settings.UiLanguage);
            return;
        }
        var dlg = new EvidenceTimelineWindow(selected, _settings.UiLanguage) { Owner = this };
        dlg.ShowDialog();
    }

    private void HistoryGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (HistoryGrid.SelectedItem is not TestSession s) return;
        _session = s;
        SessionNameBox.Text = string.IsNullOrWhiteSpace(s.BaseName) ? s.Name : s.BaseName;
        SessionFolderBox.Text = s.Folder;
        SessionTagsBox.Text = string.Join(", ", s.Tags);
        TemplateBox.SelectedIndex = s.Template switch { "Regression" => 1, "UAT" => 2, "Smoke" => 3, "Exploratory" => 4, _ => 0 };
        RefreshMetrics();
        ShowView("Recorder");
        StateText.Text = Localizer.T("Historical", _settings.UiLanguage);
    }

    private async Task ReloadSessionTemplatesAsync()
    {
        _sessionTemplates = await new SessionTemplateService().LoadAsync();
        var display = new List<SessionTemplateDefinition> { new() { Id = Guid.Empty, Name = _settings.UiLanguage.StartsWith("Esp", StringComparison.OrdinalIgnoreCase) ? "Sin plantilla" : "No template", BuiltIn = false } };
        display.AddRange(_sessionTemplates);
        SessionTemplateBox.ItemsSource = null;
        SessionTemplateBox.ItemsSource = display;
        SessionTemplateBox.SelectedIndex = 0;
    }

    private void ApplySessionTemplate(SessionTemplateDefinition template)
    {
        TemplateBox.SelectedIndex = template.TestType switch { "Regression" => 1, "UAT" => 2, "Smoke" => 3, "Exploratory" => 4, _ => 0 };
        if (!string.IsNullOrWhiteSpace(template.DefaultTags)) SessionTagsBox.Text = template.DefaultTags;
        CharterObjectiveBox.Text = template.CharterObjective;
        CharterRisksBox.Text = template.CharterRisks;
        ChecklistDefinitionBox.Text = template.ChecklistText;
        PeriodicShotCheck.IsChecked = template.PeriodicCapture;
        PeriodicSecondsBox.Text = template.PeriodicSeconds.ToString();
        PrivacyMaskBox.SelectedIndex = template.PrivacyMasking switch { PrivacyMaskMode.Manual => 1, PrivacyMaskMode.Off => 2, _ => 0 };
        _settings.PeriodicScreenshot = template.PeriodicCapture;
        _settings.PeriodicSeconds = template.PeriodicSeconds;
        _settings.PrivacyMasking = template.PrivacyMasking;
    }

    private void SessionTemplateBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || SessionTemplateBox.SelectedItem is not SessionTemplateDefinition template || template.Id == Guid.Empty) return;
        ApplySessionTemplate(template);
    }

    private async void ManageSessionTemplates_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SessionTemplateWindow(_settings.UiLanguage) { Owner = this };
        dlg.ShowDialog();
        await ReloadSessionTemplatesAsync();
    }

    private async Task AddBookmarkAsync()
    {
        if (_session is null || _session.State != SessionState.Recording) return;
        var dlg = new BookmarkWindow(_settings.UiLanguage);
        if (IsVisible) dlg.Owner = this;
        if (dlg.ShowDialog() != true) return;
        var bookmark = new SessionBookmark
        {
            Timestamp = DateTimeOffset.Now,
            Category = dlg.Category,
            Note = dlg.Note,
            ApplicationName = _desktopRecorder.LastApplicationName,
            WindowTitle = _desktopRecorder.LastTargetTitle
        };
        try
        {
            bookmark.ScreenshotPath = await CaptureDesktopEvidenceAsync(ScreenshotKind.Manual, bookmark.WindowTitle) ?? "";
            var shot = _session.Screenshots.LastOrDefault(x => string.Equals(x.FilePath, bookmark.ScreenshotPath, StringComparison.OrdinalIgnoreCase));
            if (shot is not null) shot.TargetTitle = $"★ {bookmark.DisplayText}" + (string.IsNullOrWhiteSpace(bookmark.WindowTitle) ? "" : $" — {bookmark.WindowTitle}");
        }
        catch { }
        _session.Bookmarks.Add(bookmark);
        var ev = new QaEvent
        {
            Timestamp = bookmark.Timestamp,
            Kind = EventKind.Note,
            ApplicationName = bookmark.ApplicationName,
            WindowTitle = bookmark.WindowTitle,
            TargetTitle = bookmark.WindowTitle,
            Description = bookmark.DisplayText,
            IsImportant = true,
            IsCheckpoint = true,
            CheckpointKind = "Bookmark",
            CheckpointTitle = bookmark.DisplayText
        };
        _session.Events.Add(ev);
        await _store.SaveAsync(_session);
        StatusBar.Text = _settings.UiLanguage.StartsWith("Esp", StringComparison.OrdinalIgnoreCase) ? "Marcador agregado a la sesión." : "Bookmark added to the session.";
    }

    private void OpenChecklist()
    {
        if (_session is null || _session.State is not (SessionState.Recording or SessionState.Paused)) return;
        if (_session.ManualChecklist.Count == 0)
        {
            AppDialogWindow.ShowInfo(this, "QAFlow Recorder", _settings.UiLanguage.StartsWith("Esp", StringComparison.OrdinalIgnoreCase) ? "Esta sesión no tiene checklist manual." : "This session has no manual checklist.", _settings.UiLanguage);
            return;
        }
        var dlg = new ManualChecklistWindow(_session.ManualChecklist, _settings.UiLanguage);
        if (IsVisible) dlg.Owner = this;
        dlg.ShowDialog();
        _ = _store.SaveAsync(_session);
    }

    private void CompareHistory_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryGrid.SelectedItem is not TestSession selected)
        {
            AppDialogWindow.ShowInfo(this, "QAFlow Recorder", _settings.UiLanguage.StartsWith("Esp", StringComparison.OrdinalIgnoreCase) ? "Selecciona una sesión para comparar." : "Select a session to compare.", _settings.UiLanguage);
            return;
        }
        var baseName = string.IsNullOrWhiteSpace(selected.BaseName) ? selected.Name : selected.BaseName;
        var previous = _history.Where(x => x.Id != selected.Id && x.StartedAt < selected.StartedAt)
            .OrderByDescending(x => string.Equals(string.IsNullOrWhiteSpace(x.BaseName) ? x.Name : x.BaseName, baseName, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(x => x.StartedAt)
            .FirstOrDefault();
        if (previous is null)
        {
            AppDialogWindow.ShowInfo(this, "QAFlow Recorder", _settings.UiLanguage.StartsWith("Esp", StringComparison.OrdinalIgnoreCase) ? "No hay una sesión anterior disponible para comparar." : "No previous session is available for comparison.", _settings.UiLanguage);
            return;
        }
        new SessionComparisonWindow(SessionComparisonService.Compare(selected, previous), _settings.UiLanguage) { Owner = this }.ShowDialog();
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
        ThemeBox.SelectedIndex = string.Equals(_settings.UiTheme, ThemeManager.Aero, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        FileTemplateBox.Text = _settings.FileNameTemplate;
        CompanyLogoPathBox.Text = _settings.CompanyLogoPath;
        ExportFolderBox.Text = _settings.ExportFolder;
        PrivacyMaskBox.SelectedIndex = _settings.PrivacyMasking switch { PrivacyMaskMode.Manual => 1, PrivacyMaskMode.Off => 2, _ => 0 };
        PrivacyZonesHelpText.Text = (_settings.UiLanguage.StartsWith("Esp", StringComparison.OrdinalIgnoreCase) ? "Zonas de privacidad configuradas: " : "Configured privacy zones: ") + _settings.PrivacyZones.Count;
        CoverageTargetsBox.Text = _settings.ExpectedCoverageTargets;
        VisualComparisonCheck.IsChecked = _settings.IncludeVisualComparison;
        EvidenceQualityCheck.IsChecked = _settings.IncludeEvidenceQuality;
        ProductModeBox.SelectedIndex = _settings.ProductMode == ProductMode.HybridTesting ? 1 : 0;
        AutomationBaseUrlBox.Text = _settings.WebAutomationBaseUrl;
        AutomationAllowedDomainsBox.Text = _settings.WebAutomationAllowedDomains;
        AutomationBrowserBox.SelectedIndex = string.Equals(_settings.WebAutomationDefaultBrowser, "Firefox", StringComparison.OrdinalIgnoreCase) ? 2 : string.Equals(_settings.WebAutomationDefaultBrowser, "Edge", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        CaptureOutOfScopeCheck.IsChecked = _settings.CaptureOutOfScopeAsEvidence;
        RefreshProductModeUi();
        RefreshCapabilityNavigation();
    }

    private void ManagePrivacyZones_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new PrivacyZoneEditorWindow(_settings.PrivacyZones, _settings.UiLanguage, _desktopRecorder.LastTargetWindow) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        _settings.PrivacyZones = dlg.Zones.Select(z => new PrivacyZone
        {
            Id = z.Id, Name = z.Name, WindowTitleContains = z.WindowTitleContains,
            X = z.X, Y = z.Y, Width = z.Width, Height = z.Height, Enabled = z.Enabled
        }).ToList();
        PrivacyZonesHelpText.Text = (_settings.UiLanguage.StartsWith("Esp", StringComparison.OrdinalIgnoreCase) ? "Zonas de privacidad configuradas: " : "Configured privacy zones: ") + _settings.PrivacyZones.Count;
    }

    private void BrowseCompanyLogo_Click(object sender, RoutedEventArgs e)
    {
        var es = _settings.UiLanguage.StartsWith("Esp", StringComparison.OrdinalIgnoreCase);
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = es ? "Seleccionar logo para informes PDF" : "Select logo for PDF reports",
            Filter = "Logo files|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.svg|PNG|*.png|JPEG|*.jpg;*.jpeg|SVG|*.svg|All files|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dlg.ShowDialog(this) == true)
        {
            CompanyLogoPathBox.Text = dlg.FileName;
            StatusBar.Text = es ? "Logo personalizado seleccionado. Guarda la configuración para aplicarlo a los PDFs." : "Custom logo selected. Save settings to apply it to PDF reports.";
        }
    }

    private void ClearCompanyLogo_Click(object sender, RoutedEventArgs e)
    {
        CompanyLogoPathBox.Text = "";
        StatusBar.Text = _settings.UiLanguage.StartsWith("Esp", StringComparison.OrdinalIgnoreCase)
            ? "Se usará el logo de QAFlow en los informes PDF después de guardar."
            : "QAFlow branding will be used in PDF reports after saving.";
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
        _settings.UiTheme = ThemeBox.SelectedIndex == 1 ? ThemeManager.Aero : ThemeManager.Dark;
        _settings.PrivacyMasking = PrivacyMaskBox.SelectedIndex switch { 1 => PrivacyMaskMode.Manual, 2 => PrivacyMaskMode.Off, _ => PrivacyMaskMode.Automatic };
        _settings.ExpectedCoverageTargets = CoverageTargetsBox.Text.Trim();
        _settings.IncludeVisualComparison = VisualComparisonCheck.IsChecked == true;
        _settings.IncludeEvidenceQuality = EvidenceQualityCheck.IsChecked == true;
        _settings.FileNameTemplate = string.IsNullOrWhiteSpace(FileTemplateBox.Text) ? "qa-report-{sesion}-{fecha}.pdf" : FileTemplateBox.Text.Trim();
        _settings.CompanyLogoPath = File.Exists(CompanyLogoPathBox.Text) ? CompanyLogoPathBox.Text.Trim() : "";
        _settings.ExportFolder = string.IsNullOrWhiteSpace(ExportFolderBox.Text) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "QAFlow Recorder") : ExportFolderBox.Text.Trim();
        _settings.ProductMode = ProductModeBox.SelectedIndex == 1 ? ProductMode.HybridTesting : ProductMode.RecorderOnly;
        _settings.WebAutomationBaseUrl = AutomationBaseUrlBox.Text.Trim();
        _settings.WebAutomationAllowedDomains = AutomationAllowedDomainsBox.Text.Trim();
        _settings.WebAutomationDefaultBrowser = AutomationBrowserBox.SelectedIndex == 2 ? "Firefox" : AutomationBrowserBox.SelectedIndex == 1 ? "Edge" : "Chrome";
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

        ThemeManager.ApplyTheme(_settings.UiTheme);
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
        ProductSubtitleText.Text = es ? "Grabador QA" : "QA evidence recorder";
        if (_session is null) StateText.Text = Localizer.T("NoSession", l);
        NavRecorder.Content = "●  " + Localizer.T("Recorder", l);
        NavProjects.Content = es ? "Proyectos" : "Projects";
        NavProjects.ToolTip = es ? "Proyectos de automatización web" : "Web automation projects";
        NavHistory.Content = Localizer.T("History", l);
        NavSettings.Content = Localizer.T("Settings", l);
        RecorderTitleText.Text = es ? "Captura tu flujo QA" : "Capture your QA flow";
        RecorderSubtitleText.Text = es ? "Trabaja con normalidad. QAFlow registra interacciones y evidencia visual del escritorio y crea un reporte QA claro." : "Work naturally. QAFlow records desktop interactions and visual evidence, then builds a clear QA report.";
        AdvancedSessionExpander.Header = es ? "Opciones avanzadas de sesión" : "Advanced session options";
        SessionTemplateLabel.Text = es ? "Plantilla de sesión (opcional)" : "Session template (optional)";
        ManageSessionTemplatesButton.Content = es ? "Administrar" : "Manage";
        SessionNameLabel.Text = Localizer.T("SessionName", l);
        OrganizationExpander.Header = es ? "Organización (opcional)" : "Organization (optional)";
        SessionFolderLabel.Text = es ? "Carpeta / proyecto" : "Folder / project";
        SessionTagsLabel.Text = es ? "Etiquetas personalizadas" : "Custom tags";
        SessionTagsHelpText.Text = es ? "Crea etiquetas propias para filtros y trazabilidad del reporte, ej. regresión, checkout, staging." : "Create your own labels for filtering and report traceability, e.g. regression, checkout, staging.";
        RiskSummaryExpander.Header = es ? "Resumen de riesgos (opcional)" : "Risk summary (optional)";
        RiskSummaryHelpText.Text = es ? "Clasifica el riesgo residual observado durante esta ejecución. Es contexto QA, no una decisión automática de liberación." : "Classify residual risk observed during this execution. This is QA context, not an automatic release decision.";
        FunctionalRiskLabel.Text = es ? "Funcional" : "Functional";
        VisualRiskLabel.Text = es ? "Visual / UX" : "Visual / UX";
        RegressionRiskLabel.Text = es ? "Regresión" : "Regression";
        DataRiskLabel.Text = es ? "Datos" : "Data";
        RiskSummaryNotesLabel.Text = es ? "Notas de riesgo QA" : "QA risk notes";
        LocalizeRiskBox(FunctionalRiskBox, es); LocalizeRiskBox(VisualRiskBox, es); LocalizeRiskBox(RegressionRiskBox, es); LocalizeRiskBox(DataRiskBox, es);
        BuildVersionLabel.Text = es ? "Build / versión" : "Build / version";
        BuildVersionBox.ToolTip = es ? "Build o versión del software bajo prueba (opcional)" : "Optional software build or release under test";
        SessionFolderBox.ToolTip = es ? "Ej. Sprint 24 / Checkout" : "E.g. Sprint 24 / Checkout";
        SessionTagsBox.ToolTip = es ? "Crea cualquier etiqueta que necesites; sepáralas por coma o punto y coma" : "Create any labels you need; separate them with commas or semicolons";
        TestTypeLabel.Text = Localizer.T("TestType", l);
        CharterExpander.Header = es ? "Charter exploratorio (opcional)" : "Exploratory charter (optional)";
        CharterObjectiveLabel.Text = es ? "Objetivo" : "Objective";
        CharterScopeLabel.Text = es ? "Alcance" : "Scope";
        CharterRisksLabel.Text = es ? "Riesgos a observar" : "Risks to observe";
        CharterDataLabel.Text = es ? "Datos de prueba" : "Test data";
        CharterConstraintsLabel.Text = es ? "Restricciones / notas" : "Constraints / notes";
        CharterMinutesLabel.Text = es ? "Duración prevista (minutos)" : "Planned duration (minutes)";
        ChecklistExpander.Header = es ? "Checklist manual (opcional)" : "Manual checklist (optional)";
        ChecklistHelpText.Text = es ? "Una validación por línea. Actualiza resultados durante la sesión desde la barra de grabación." : "One validation per line. Update results during the session from the recording bar.";
        ReferenceFlowLabel.Text = es ? "Flujo de referencia (opcional)" : "Reference flow (optional)";
        ManageFlowsButton.Content = es ? "Biblioteca" : "Library";
        ShowFloatingBarCheck.Content = es ? "Mostrar barra de control durante la grabación" : "Show recording control bar";
        CaptureAreaTitleText.Text = es ? "Área de grabación" : "Recording area";
        CaptureAreaHelpText.Text = es ? "Elige la pantalla a capturar. Se usa pantalla completa salvo que definas un área personalizada." : "Choose the screen to capture. Full screen is used unless you define a custom area.";
        SelectCaptureAreaButton.Content = es ? "▣  Seleccionar área" : "▣  Select custom area";
        ClearCaptureAreaButton.Content = es ? "Pantalla completa" : "Full screen";
        RefreshScreensButton.ToolTip = es ? "Detectar pantallas nuevamente" : "Detect screens again";
        RecordVideoCheck.Content = es ? "Grabar video de la sesión (opcional)" : "Record session video (optional)";
        VideoRecordingWarningText.Text = es
            ? "El video utiliza más CPU, memoria y espacio en disco. Para un mejor rendimiento, evita sesiones de video muy extensas."
            : "Video recording uses additional CPU, memory and disk space. For best performance, avoid very long video sessions.";
        VideoRecordingSaveText.Text = es
            ? "Al activarlo, QAFlow guarda el video de la sesión localmente y lo deja disponible en Exportar sesión al finalizar la ejecución QA."
            : "When enabled, QAFlow saves the recorded session video locally and makes it available in Export session when the QA session finishes.";
        UpdateCaptureAreaStatus();
        StartButton.Content = es ? "▶  Iniciar sesión QA" : "▶  Start QA session";
        PauseButton.Content = (_session?.State == SessionState.Paused ? "▶  " : "⏸  ") + (_session?.State == SessionState.Paused ? Localizer.T("Resume", l) : Localizer.T("Pause", l));
        FinishButton.Content = "■  " + Localizer.T("Finish", l);
        PrivacyTitleText.Text = es ? "LOCAL POR DISEÑO" : "LOCAL BY DESIGN";
        PrivacyBodyText.Text = es ? "La evidencia permanece en esta PC. QAFlow nunca guarda los caracteres que escribes." : "Evidence stays on this PC. QAFlow never records the characters you type.";
        TimeMetricLabel.Text = Localizer.T("Time", l).ToUpperInvariant();
        ActionsMetricLabel.Text = es ? "INTERACCIONES" : "INTERACTIONS";
        PagesMetricLabel.Text = es ? "OBJETIVOS" : "TARGETS";
        NotesMetricLabel.Text = es ? "HALLAZGOS" : "FINDINGS";
        HotkeysText.Text = es ? "Ctrl+Shift+R finalizar · Ctrl+Shift+M marcar · Ctrl+Shift+K checklist · Ctrl+Shift+B bug" : "Ctrl+Shift+R finish · Ctrl+Shift+M mark · Ctrl+Shift+K checklist · Ctrl+Shift+B bug";
        SessionNameBox.ToolTip = es ? "Ej. Validación de checkout" : "E.g. Checkout validation";

        HistoryTitleText.Text = Localizer.T("History", l);
        HistorySubtitleText.Text = es ? "Revisa sesiones guardadas y grabaciones divididas en partes." : "Review saved sessions and multipart recordings.";
        HistorySearch.ToolTip = es ? "Buscar por sesión, carpeta, etiqueta o fecha" : "Search by session, folder, tag or date";
        TimelineHistoryButton.Content = es ? "Evidencia" : "Timeline";
        TimelineHistoryButton.ToolTip = es ? "Abrir línea de tiempo de evidencia" : "Open evidence timeline";
        DeleteHistoryButton.Content = es ? "Eliminar" : "Delete";
        DeleteHistoryButton.ToolTip = es ? "Eliminar la sesión seleccionada" : "Delete selected session";
        CompareHistoryButton.Content = es ? "Comparar" : "Compare";
        CompareHistoryButton.ToolTip = es ? "Comparar con la ejecución anterior" : "Compare with the previous run";
        ClearHistoryButton.Content = es ? "Limpiar" : "Clear";
        ClearHistoryButton.ToolTip = es ? "Eliminar todo el historial guardado" : "Delete all saved session history";
        if (HistoryGrid.Columns.Count >= 4)
        {
            HistoryGrid.Columns[0].Header = es ? "Sesión" : "Session";
            HistoryGrid.Columns[1].Header = es ? "Carpeta" : "Folder";
            HistoryGrid.Columns[2].Header = es ? "Inicio" : "Started";
            HistoryGrid.Columns[3].Header = es ? "Duración" : "Duration";
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
        QualityLabel.Text = l.StartsWith("Esp", StringComparison.OrdinalIgnoreCase) ? "Calidad de evidencia PDF" : "PDF evidence quality";
        DocLanguageLabel.Text = Localizer.T("DocLanguage", l);
        PrivacyMaskLabel.Text = es ? "Enmascarado de datos sensibles" : "Sensitive data masking";
        ManagePrivacyZonesButton.Content = es ? "Administrar zonas de privacidad" : "Manage privacy zones";
        ManagePrivacyZonesButton.ToolTip = es ? "Define áreas que siempre deben ocultarse en las capturas" : "Define areas that should always be hidden in screenshots";
        PrivacyZonesHelpText.Text = (es ? "Zonas de privacidad configuradas: " : "Configured privacy zones: ") + _settings.PrivacyZones.Count;
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
        PdfBrandingLabel.Text = es ? "Identidad de informes PDF" : "PDF report branding";
        AppearanceTitleText.Text = es ? "Apariencia" : "Appearance";
        AppearanceHelpText.Text = es
            ? "Elige el estilo de la interfaz. Aero ofrece un espacio de trabajo más luminoso con acabado tipo vidrio, conservando los mismos controles y flujo."
            : "Choose the interface style. Aero adds a brighter glass-inspired workspace while preserving the same controls and workflow.";
        BrowseCompanyLogoButton.Content = es ? "Elegir logo de empresa" : "Choose company logo";
        ClearCompanyLogoButton.Content = es ? "Usar logo de QAFlow" : "Use QAFlow logo";
        CompanyLogoHelpText.Text = es
            ? "PNG, JPG, WEBP, BMP o SVG. Un logo empresarial configurado reemplaza la identidad de QAFlow en los PDFs exportados."
            : "PNG, JPG, WEBP, BMP or SVG. A configured company logo replaces QAFlow branding in exported PDFs.";
        ExportFolderLabel.Text = Localizer.T("ExportFolder", l);
        FileTemplateLabel.Text = es ? "Plantilla de nombre de archivo" : "File name template";
        SaveSettingsButton.Content = Localizer.T("SaveSettings", l);
        PinButton.ToolTip = es ? "Mantener QAFlow siempre visible" : "Keep QAFlow always on top";
        DownloadExtensionButton.ToolTip = es ? "Descargar extensión QAFlow para Chrome / Edge" : "Download QAFlow extension for Chrome / Edge";
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


    private static string RiskValue(int index) => index switch
    {
        1 => "Low",
        2 => "Medium",
        3 => "High",
        4 => "Critical",
        _ => "Not assessed"
    };

    private static void LocalizeRiskBox(ComboBox box, bool es)
    {
        if (box.Items.Count < 5) return;
        ((ComboBoxItem)box.Items[0]).Content = es ? "No evaluado" : "Not assessed";
        ((ComboBoxItem)box.Items[1]).Content = es ? "Bajo" : "Low";
        ((ComboBoxItem)box.Items[2]).Content = es ? "Medio" : "Medium";
        ((ComboBoxItem)box.Items[3]).Content = es ? "Alto" : "High";
        ((ComboBoxItem)box.Items[4]).Content = es ? "Crítico" : "Critical";
    }

    private async Task SaveRecoveryCheckpointAsync(bool force = false)
    {
        if (_session is null || _session.State is not (SessionState.Recording or SessionState.Paused)) return;
        if (!force && DateTimeOffset.Now - _lastRecoverySave < TimeSpan.FromSeconds(5)) return;
        _lastRecoverySave = DateTimeOffset.Now;
        _session.RecoveryCheckpointAt = _lastRecoverySave;
        try { await _store.SaveAsync(_session); }
        catch (Exception ex) { TechnicalLogService.Warn(nameof(MainWindow), "RecoveryCheckpoint", ex.Message, ex); }
    }

    private System.Windows.Media.Imaging.BitmapSource? CaptureVideoFrame()
    {
        // Video frames are captured on the recorder background thread. Do not read WPF controls here:
        // the selected screen/custom area is frozen into the session when Start QA session is pressed.
        var session = _session;
        if (session is null || session.State != SessionState.Recording) return null;

        System.Windows.Media.Imaging.BitmapSource? bitmap = null;
        if (session.CaptureAreaWidth > 0 && session.CaptureAreaHeight > 0)
        {
            bitmap = DesktopCaptureService.CaptureRegion(new Int32Rect(
                session.CaptureAreaX,
                session.CaptureAreaY,
                session.CaptureAreaWidth,
                session.CaptureAreaHeight));
        }
        bitmap ??= DesktopCaptureService.CapturePrimaryScreen();
        if (bitmap is null) return null;

        var hwnd = _desktopRecorder.LastTargetWindow;
        if (hwnd == IntPtr.Zero || DesktopCaptureService.IsHandleOwnedByCurrentProcess(hwnd))
            hwnd = DesktopCaptureService.GetForegroundWindowHandle();
        var title = DesktopCaptureService.GetWindowTitle(hwnd);
        return PrivacyMaskService.ApplyZones(bitmap, _settings.PrivacyZones, title);
    }

    private async Task StartVideoRecordingAsync(TestSession session)
    {
        await StopVideoRecordingAsync();
        if (!session.VideoRecordingEnabled) return;
        try
        {
            var segment = session.VideoFilePaths.Count + 1;
            var path = Path.Combine(_store.VideoFolder(session), $"session-video-{segment:00}-{DateTime.Now:yyyyMMdd-HHmmss}.avi");
            session.VideoFilePaths.Add(path);
            _videoRecorder = new VideoRecordingService(CaptureVideoFrame, path, Math.Clamp(session.VideoFrameRate, 1, 10), 72);
            await _videoRecorder.StartAsync();
            TechnicalLogService.Info(nameof(MainWindow), "VideoRecordingStarted", $"Optional session video started at {session.VideoFrameRate} fps.");
        }
        catch (Exception ex)
        {
            _videoRecorder = null;
            TechnicalLogService.Warn(nameof(MainWindow), "StartVideoRecording", ex.Message, ex);
            var es = _settings.UiLanguage.StartsWith("Esp", StringComparison.OrdinalIgnoreCase);
            StatusBar.Text = es ? "La sesión continúa, pero no se pudo iniciar el video opcional." : "The session continues, but optional video recording could not be started.";
        }
    }

    private async Task StopVideoRecordingAsync()
    {
        var recorder = _videoRecorder;
        _videoRecorder = null;
        if (recorder is null) return;
        try
        {
            var completedPath = await recorder.StopAsync();
            if (_session is not null && string.IsNullOrWhiteSpace(completedPath))
            {
                _session.VideoFilePaths.RemoveAll(p => p.Equals(recorder.OutputPath, StringComparison.OrdinalIgnoreCase));
            }
            else if (!string.IsNullOrWhiteSpace(completedPath) && File.Exists(completedPath))
            {
                var bytes = new FileInfo(completedPath).Length;
                TechnicalLogService.Info(nameof(MainWindow), "VideoRecordingSaved", $"Optional session video saved successfully ({bytes} bytes): {completedPath}");
            }
        }
        catch (Exception ex)
        {
            TechnicalLogService.Warn(nameof(MainWindow), "StopVideoRecording", ex.Message, ex);
        }
    }

    private async Task TryRecoverInterruptedSessionAsync()
    {
        var recoverable = await _store.LoadRecoverableAsync();
        if (recoverable is null) return;
        var es = _settings.UiLanguage.StartsWith("Esp", StringComparison.OrdinalIgnoreCase);
        var checkpoint = recoverable.RecoveryCheckpointAt ?? recoverable.StartedAt;
        var recover = AppDialogWindow.ShowQuestion(this,
            es ? "Recuperar sesión interrumpida" : "Recover interrupted session",
            es
                ? $"QAFlow encontró una sesión que no se cerró correctamente: “{recoverable.DisplayName}”.\n\nÚltimo guardado seguro: {checkpoint.LocalDateTime:yyyy-MM-dd HH:mm:ss}.\n\n¿Deseas recuperarla y continuar?"
                : $"QAFlow found a session that did not close normally: “{recoverable.DisplayName}”.\n\nLast safe checkpoint: {checkpoint.LocalDateTime:yyyy-MM-dd HH:mm:ss}.\n\nRecover it and continue?",
            _settings.UiLanguage);

        if (!recover)
        {
            recoverable.State = SessionState.Finished;
            recoverable.FinishedAt = checkpoint;
            recoverable.RecoveryCheckpointAt = checkpoint;
            await _store.SaveAsync(recoverable);
            await ReloadHistoryAsync();
            return;
        }

        var now = DateTimeOffset.Now;
        if (now > checkpoint) recoverable.PausedTime += now - checkpoint;
        recoverable.RecoveredAfterUnexpectedClose = true;
        recoverable.RecoveryCheckpointAt = now;
        if (recoverable.State == SessionState.Paused) recoverable.PauseStartedAt = now;
        _session = recoverable;
        _lastRecoverySave = now;

        if (!string.IsNullOrWhiteSpace(recoverable.CaptureDeviceName))
        {
            var monitor = _displayMonitors.FirstOrDefault(m => m.DeviceName.Equals(recoverable.CaptureDeviceName, StringComparison.OrdinalIgnoreCase));
            if (monitor is not null) RecordingScreenBox.SelectedItem = monitor;
        }
        if (recoverable.CustomCaptureArea && recoverable.CaptureAreaWidth > 0 && recoverable.CaptureAreaHeight > 0)
        {
            _customCaptureArea = new CaptureAreaSelection
            {
                MonitorDeviceName = recoverable.CaptureDeviceName,
                Bounds = new Int32Rect(recoverable.CaptureAreaX, recoverable.CaptureAreaY, recoverable.CaptureAreaWidth, recoverable.CaptureAreaHeight)
            };
        }
        UpdateCaptureAreaStatus();

        SessionNameBox.Text = string.IsNullOrWhiteSpace(recoverable.BaseName) ? recoverable.Name : recoverable.BaseName;
        SessionFolderBox.Text = recoverable.Folder;
        SessionTagsBox.Text = string.Join(", ", recoverable.Tags);
        BuildVersionBox.Text = recoverable.BuildVersion;
        RiskSummaryNotesBox.Text = recoverable.RiskSummaryNotes;
        FunctionalRiskBox.SelectedIndex = RiskIndex(recoverable.FunctionalRisk);
        VisualRiskBox.SelectedIndex = RiskIndex(recoverable.VisualRisk);
        RegressionRiskBox.SelectedIndex = RiskIndex(recoverable.RegressionRisk);
        DataRiskBox.SelectedIndex = RiskIndex(recoverable.DataRisk);
        RecordVideoCheck.IsChecked = recoverable.VideoRecordingEnabled;

        _desktopRecorder.Start();
        if (recoverable.VideoRecordingEnabled)
        {
            await StartVideoRecordingAsync(recoverable);
            if (recoverable.State == SessionState.Paused) _videoRecorder?.Pause();
        }
        StartButton.IsEnabled = false; PauseButton.IsEnabled = true; FinishButton.IsEnabled = true;
        SessionTemplateBox.IsEnabled = false; ManageSessionTemplatesButton.IsEnabled = false;
        SessionNameBox.IsEnabled = false; OrganizationExpander.IsEnabled = false; RiskSummaryExpander.IsEnabled = true;
        TemplateBox.IsEnabled = false; CharterExpander.IsEnabled = false; ChecklistExpander.IsEnabled = false;
        ReferenceFlowBox.IsEnabled = false; ManageFlowsButton.IsEnabled = false;
        RecordingScreenBox.IsEnabled = false; RefreshScreensButton.IsEnabled = false; SelectCaptureAreaButton.IsEnabled = false; ClearCaptureAreaButton.IsEnabled = false; RecordVideoCheck.IsEnabled = false;
        StateDot.Fill = new SolidColorBrush(recoverable.State == SessionState.Paused ? Color.FromRgb(244,184,96) : Color.FromRgb(255,101,119));
        StateText.Text = recoverable.State == SessionState.Paused ? (es ? "PAUSADO · RECUPERADO" : "PAUSED · RECOVERED") : (es ? "GRABANDO · RECUPERADO" : "RECORDING · RECOVERED");
        PauseButton.Content = recoverable.State == SessionState.Paused ? "▶  " + Localizer.T("Resume", _settings.UiLanguage) : "⏸  " + Localizer.T("Pause", _settings.UiLanguage);
        _timer.Start();
        if (_settings.PeriodicScreenshot) { _periodic.Interval = TimeSpan.FromSeconds(Math.Max(10, _settings.PeriodicSeconds)); _periodic.Tick -= Periodic_Tick; _periodic.Tick += Periodic_Tick; _periodic.Start(); }
        RefreshMetrics();
        StatusBar.Text = es ? "Sesión recuperada desde el último punto seguro." : "Session recovered from the last safe checkpoint.";
        await SaveRecoveryCheckpointAsync(force: true);
        RebuildFloatingBar();
    }

    private static int RiskIndex(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "low" => 1, "medium" => 2, "high" => 3, "critical" => 4, _ => 0
    };


    private void DownloadExtension_Click(object sender, RoutedEventArgs e)
    {
        var es = _settings.UiLanguage.StartsWith("Esp", StringComparison.OrdinalIgnoreCase);
        try
        {
            var source = Path.Combine(AppContext.BaseDirectory, "BrowserExtension", "QAFlow_Browser_Extension.zip");
            if (!File.Exists(source))
            {
                // Visual Studio/source fallback.
                var sourceFallback = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "BrowserExtension", "QAFlow_Browser_Extension.zip");
                sourceFallback = Path.GetFullPath(sourceFallback);
                if (File.Exists(sourceFallback)) source = sourceFallback;
            }
            if (!File.Exists(source))
            {
                AppDialogWindow.ShowError(this, es ? "Extensión no disponible" : "Extension unavailable",
                    es ? "No se encontró el paquete de la extensión de navegador en esta instalación." : "The browser extension package was not found in this installation.", _settings.UiLanguage);
                return;
            }

            var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            Directory.CreateDirectory(downloads);
            var destination = Path.Combine(downloads, "QAFlow_Browser_Extension_1.0.zip");
            File.Copy(source, destination, true);
            StatusBar.Text = es ? "Extensión descargada: QAFlow_Browser_Extension_1.0.zip" : "Extension downloaded: QAFlow_Browser_Extension_1.0.zip";
            StatusBar.ToolTip = destination;
            var open = AppDialogWindow.ShowQuestion(this, "QAFlow Recorder",
                es ? "La extensión se copió a Descargas.\n\n¿Abrir la carpeta ahora?" : "The browser extension was copied to Downloads.\n\nOpen the folder now?", _settings.UiLanguage);
            if (open) Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{destination}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppDialogWindow.ShowError(this, es ? "Error al descargar extensión" : "Extension download error", ex.Message, _settings.UiLanguage);
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
        _floatingBar.BookmarkRequested += () => Dispatcher.Invoke(async () => await AddBookmarkAsync());
        _floatingBar.ChecklistRequested += () => Dispatcher.Invoke(OpenChecklist);
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
        else if (e.Key == Key.M) { await AddBookmarkAsync(); e.Handled = true; }
        else if (e.Key == Key.K) { OpenChecklist(); e.Handled = true; }
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
        RegisterHotKey(hwnd, HotkeyBookmark, ModControl | ModShift, (uint)KeyInterop.VirtualKeyFromKey(Key.M));
        RegisterHotKey(hwnd, HotkeyChecklist, ModControl | ModShift, (uint)KeyInterop.VirtualKeyFromKey(Key.K));
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
            case HotkeyBookmark: await AddBookmarkAsync(); break;
            case HotkeyChecklist: OpenChecklist(); break;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey) { handled = true; HandleHotkey(wParam.ToInt32()); }
        return IntPtr.Zero;
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        if (_session is { State: SessionState.Recording or SessionState.Paused })
        {
            try
            {
                StopVideoRecordingAsync().GetAwaiter().GetResult();
                _session.RecoveryCheckpointAt = DateTimeOffset.Now;
                _store.SaveAsync(_session).GetAwaiter().GetResult();
            }
            catch (Exception ex) { TechnicalLogService.Warn(nameof(MainWindow), "CloseRecoveryCheckpoint", ex.Message, ex); }
        }
        CloseFloatingBar();
        _desktopRecorder.Stop();
        if (_webBridge is not null)
        {
            _webBridge.ObservationReceived -= WebBridge_ObservationReceived;
            _webBridge.Dispose();
            _webBridge = null;
        }
        var hwnd = new WindowInteropHelper(this).Handle;
        foreach (var id in new[] { HotkeyToggle, HotkeyAnnotate, HotkeyPause, HotkeyHistory, HotkeyRestore, HotkeyBug, HotkeyBookmark, HotkeyChecklist }) UnregisterHotKey(hwnd, id);
        _hwndSource?.RemoveHook(WndProc);
    }

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
