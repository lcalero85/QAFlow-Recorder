using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using QAFlowRecorder.Services;

namespace QAFlowRecorder.Views;

public partial class FloatingRecorderBar : Window
{
    public event Action? PauseRequested;
    public event Action? AnnotateRequested;
    public event Action? CaptureRequested;
    public event Action? BugRequested;
    public event Action? RestoreRequested;
    public event Func<Task>? FinishRequested;
    private readonly Func<string> _timeProvider;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly string _language;

    public FloatingRecorderBar(string sessionName, Func<string> timeProvider, string language="English")
    {
        InitializeComponent();
        WindowThemeHelper.ApplyDarkTitleBar(this);
        SessionText.Text = sessionName;
        _timeProvider = timeProvider;
        _language = language;
        ApplyLanguage(false);
        Loaded += (_,_) =>
        {
            var work = SystemParameters.WorkArea;
            Left = work.Left + Math.Max(10,(work.Width-Width)/2);
            Top = work.Top + 8;
            _timer.Tick += (_,_) => TimeText.Text = _timeProvider();
            _timer.Start();
        };
        Closed += (_,_) => _timer.Stop();
    }

    private void ApplyLanguage(bool paused)
    {
        StatusText.Text = paused ? Localizer.T("Paused",_language) : Localizer.T("Recording",_language);
        PauseButton.Content = (paused ? "▶ " : "⏸ ") + (paused ? Localizer.T("Resume",_language) : Localizer.T("Pause",_language));
        AnnotateButton.Content="✎ "+Localizer.T("Annotate",_language);CaptureButton.Content="📷 "+Localizer.T("Capture",_language);BugButton.Content="🐞 "+Localizer.T("Bug",_language);RestoreButton.Content="▣ "+Localizer.T("Open",_language);FinishButton.Content="■ "+Localizer.T("Finish",_language);
        var es=_language.StartsWith("Esp",StringComparison.OrdinalIgnoreCase);PauseButton.ToolTip=es?"Pausar o reanudar grabación":"Pause or resume recording";AnnotateButton.ToolTip=es?"Agregar anotación QA":"Add QA annotation";CaptureButton.ToolTip=es?"Capturar el objetivo actual":"Capture current target";BugButton.ToolTip=es?"Pausar y documentar un bug":"Pause and document a bug";RestoreButton.ToolTip=es?"Mostrar QAFlow Recorder":"Show QAFlow Recorder";FinishButton.ToolTip=es?"Finalizar sesión y exportar":"Finish session and export";
    }

    public void SetPaused(bool paused)
    {
        ApplyLanguage(paused);
        StatusText.Foreground = paused ? System.Windows.Media.Brushes.Orange : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255,120,136));
    }

    private void Bar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        DependencyObject? current = e.OriginalSource as DependencyObject;
        while (current is not null)
        {
            if (current is System.Windows.Controls.Button) return;
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }
        DragMove();
    }
    private void Pause_Click(object sender,RoutedEventArgs e)=>PauseRequested?.Invoke();
    private void Annotate_Click(object sender,RoutedEventArgs e)=>AnnotateRequested?.Invoke();
    private void Capture_Click(object sender,RoutedEventArgs e)=>CaptureRequested?.Invoke();
    private void Bug_Click(object sender,RoutedEventArgs e)=>BugRequested?.Invoke();
    private void Restore_Click(object sender,RoutedEventArgs e)=>RestoreRequested?.Invoke();
    private async void Finish_Click(object sender, RoutedEventArgs e)
    {
        if (FinishRequested is null || !FinishButton.IsEnabled) return;
        FinishButton.IsEnabled = false;
        try
        {
            foreach (Func<Task> handler in FinishRequested.GetInvocationList())
                await handler();
        }
        finally
        {
            if (IsVisible) FinishButton.IsEnabled = true;
        }
    }
}
