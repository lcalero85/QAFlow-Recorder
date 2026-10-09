using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using QAFlowRecorder.Models;
using QAFlowRecorder.Services;

namespace QAFlowRecorder.Views;

public partial class EvidenceTimelineWindow : Window
{
    private readonly TestSession _session;
    private readonly string _language;
    private List<EvidenceShot> _all = [];

    public EvidenceTimelineWindow(TestSession session, string language)
    {
        InitializeComponent();
        WindowThemeHelper.ApplyDarkTitleBar(this);
        _session = session;
        _language = language;
        ApplyLanguage();
        _all = session.Screenshots.ToList();
        foreach (var bug in session.Bugs)
        {
            var path = !string.IsNullOrWhiteSpace(bug.MarkedScreenshotPath) ? bug.MarkedScreenshotPath : bug.OriginalScreenshotPath;
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path) && !_all.Any(x => string.Equals(x.FilePath, path, StringComparison.OrdinalIgnoreCase)))
                _all.Add(new EvidenceShot { Timestamp = bug.Timestamp, Kind = ScreenshotKind.BugMarked, TargetTitle = bug.Screen, FilePath = path });
        }
        _all = _all.OrderBy(x => x.Timestamp).ToList();
        ApplyFilter();
    }

    private void ApplyLanguage()
    {
        var es = _language.StartsWith("Esp", StringComparison.OrdinalIgnoreCase);
        Title = es ? "Línea de tiempo de evidencia — QAFlow" : "Evidence timeline — QAFlow";
        TitleText.Text = es ? "Línea de tiempo de evidencia" : "Evidence timeline";
        SubtitleText.Text = es ? "Revisa la evidencia visual capturada durante esta sesión." : "Review the visual evidence captured during this session.";
        OpenFolderButton.Content = es ? "Abrir carpeta de evidencia" : "Open evidence folder";
        ((ComboBoxItem)FilterBox.Items[0]).Content = es ? "Toda la evidencia" : "All evidence";
        ((ComboBoxItem)FilterBox.Items[1]).Content = es ? "Acciones del flujo" : "Flow actions";
        ((ComboBoxItem)FilterBox.Items[2]).Content = es ? "Anotaciones" : "Annotations";
        ((ComboBoxItem)FilterBox.Items[3]).Content = es ? "Capturas manuales" : "Manual captures";
        ((ComboBoxItem)FilterBox.Items[4]).Content = es ? "Bugs" : "Bugs";
        ((ComboBoxItem)FilterBox.Items[5]).Content = es ? "Marcadores" : "Bookmarks";
    }

    private void FilterBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        IEnumerable<EvidenceShot> q = _all;
        q = FilterBox.SelectedIndex switch
        {
            1 => q.Where(x => x.Kind == ScreenshotKind.FlowAction),
            2 => q.Where(x => x.Kind == ScreenshotKind.Annotation),
            3 => q.Where(x => x.Kind is ScreenshotKind.Manual or ScreenshotKind.Periodic or ScreenshotKind.Final),
            4 => q.Where(x => x.Kind is ScreenshotKind.BugOriginal or ScreenshotKind.BugMarked),
            5 => q.Where(x => x.TargetTitle.StartsWith("★", StringComparison.Ordinal)),
            _ => q
        };
        TimelineList.ItemsSource = q.ToList();
        if (TimelineList.Items.Count > 0) TimelineList.SelectedIndex = 0;
    }

    private void TimelineList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TimelineList.SelectedItem is not EvidenceShot shot) { PreviewImage.Source = null; return; }
        PreviewInfo.Text = $"{shot.TimeText} · {shot.Kind} · {shot.TargetTitle}";
        try
        {
            if (!File.Exists(shot.FilePath)) { PreviewImage.Source = null; return; }
            using var stream = File.OpenRead(shot.FilePath);
            var bmp = new BitmapImage();
            bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.StreamSource = stream; bmp.EndInit(); bmp.Freeze();
            PreviewImage.Source = bmp;
        }
        catch { PreviewImage.Source = null; }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = _session.Screenshots.Select(x => Path.GetDirectoryName(x.FilePath)).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x) && Directory.Exists(x));
        if (string.IsNullOrWhiteSpace(folder)) return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    }
}
