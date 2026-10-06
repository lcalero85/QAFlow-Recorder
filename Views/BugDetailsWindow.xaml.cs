using System.Windows;
using System.Windows.Media.Imaging;
using QAFlowRecorder.Models;
using QAFlowRecorder.Services;

namespace QAFlowRecorder.Views;

public partial class BugDetailsWindow : Window
{
    private readonly string _language;
    private readonly string _bugFolder;
    private string _screenshotPath;
    private readonly List<string> _attachments = [];
    public BugRecord? Bug { get; private set; }

    public BugDetailsWindow(string screenshotPath, string bugFolder, string language, string url, string screenHint="")
    {
        InitializeComponent();
        WindowThemeHelper.ApplyDarkTitleBar(this);
        _language = language;
        _bugFolder = bugFolder;
        _screenshotPath = screenshotPath;
        ScreenBox.Text = !string.IsNullOrWhiteSpace(screenHint) ? screenHint : (Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host + uri.AbsolutePath : url);
        LoadPreview();
        ApplyLanguage();
    }

    private void ApplyLanguage()
    {
        Title = Localizer.T("BugTitle", _language);
        TitleText.Text = "🐞 " + Localizer.T("BugTitle", _language);
        PauseInfoText.Text = Localizer.T("BugPauseInfo", _language);
        ScreenLabel.Text = Localizer.T("BugScreen", _language);
        FrequencyLabel.Text = Localizer.T("Frequency", _language);
        ResultLabel.Text = Localizer.T("ResultType", _language);
        SeverityLabel.Text = Localizer.T("Severity", _language);
        ExpectedLabel.Text = Localizer.T("Expected", _language);
        ActualLabel.Text = Localizer.T("Actual", _language);
        AttachmentsLabel.Text = Localizer.T("Attachments", _language);
        AddImageButton.Content = Localizer.T("AddImage", _language);
        NoteLabel.Text = Localizer.T("FreeNote", _language);
        SaveButton.Content = Localizer.T("SaveResume", _language);
        RemarkButton.Content = _language.StartsWith("Esp", StringComparison.OrdinalIgnoreCase) ? "✎ Volver a marcar captura" : "✎ Mark screenshot again";
        AttachmentCountText.Text = _language.StartsWith("Esp", StringComparison.OrdinalIgnoreCase) ? "0 imagen(es) adjunta(s)" : "0 attachment(s)";
        if (_language.StartsWith("Esp", StringComparison.OrdinalIgnoreCase))
        {
            ((System.Windows.Controls.ComboBoxItem)FrequencyBox.Items[0]).Content="Primera vez";
            ((System.Windows.Controls.ComboBoxItem)FrequencyBox.Items[1]).Content="Siempre";
            ((System.Windows.Controls.ComboBoxItem)FrequencyBox.Items[2]).Content="A veces";
            ((System.Windows.Controls.ComboBoxItem)FrequencyBox.Items[3]).Content="Intermitente";
            DoubtRadio.Content="Duda";
            ((System.Windows.Controls.ComboBoxItem)SeverityBox.Items[0]).Content="Crítico";
            ((System.Windows.Controls.ComboBoxItem)SeverityBox.Items[1]).Content="Alto";
            ((System.Windows.Controls.ComboBoxItem)SeverityBox.Items[2]).Content="Medio";
            ((System.Windows.Controls.ComboBoxItem)SeverityBox.Items[3]).Content="Bajo";
        }
    }

    private void LoadPreview()
    {
        if (!File.Exists(_screenshotPath)) return;
        using var stream = File.OpenRead(_screenshotPath);
        var bmp = new BitmapImage(); bmp.BeginInit(); bmp.CacheOption=BitmapCacheOption.OnLoad; bmp.StreamSource=stream; bmp.EndInit(); bmp.Freeze();
        BugPreview.Source = bmp;
    }

    private void Remark_Click(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(_screenshotPath)) return;
        using var stream=File.OpenRead(_screenshotPath);
        var bmp=new BitmapImage(); bmp.BeginInit(); bmp.CacheOption=BitmapCacheOption.OnLoad; bmp.StreamSource=stream; bmp.EndInit(); bmp.Freeze();
        var mark = new ScreenshotMarkupWindow(bmp, _bugFolder, _language) { Owner = this };
        if (mark.ShowDialog()==true && !string.IsNullOrWhiteSpace(mark.SavedPath)) { _screenshotPath=mark.SavedPath; LoadPreview(); }
    }

    private void AddImage_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter="Images|*.png;*.jpg;*.jpeg;*.bmp", Multiselect=true };
        if (dlg.ShowDialog(this)!=true) return;
        _attachments.AddRange(dlg.FileNames.Where(File.Exists));
        AttachmentCountText.Text = _language.StartsWith("Esp", StringComparison.OrdinalIgnoreCase) ? $"{_attachments.Count} imagen(es) adjunta(s)" : $"{_attachments.Count} attachment(s)";
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (ExpectedBox.Text.Trim().Length < 2)
        {
            AppDialogWindow.ShowInfo(this, "QAFlow", _language.StartsWith("Esp", StringComparison.OrdinalIgnoreCase) ? "Escribe el resultado esperado." : "Enter the expected result.", _language);
            return;
        }
        var result = DoubtRadio.IsChecked==true ? AnnotationResult.Doubt : AnnotationResult.Error;
        var severityText = (SeverityBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "Medium";
        var severity = severityText.ToLowerInvariant() switch { "critical" or "crítico" => Severity.Critical, "high" or "alto" => Severity.High, "low" or "bajo" => Severity.Low, _ => Severity.Medium };
        var screen = ScreenBox.Text.Trim();
        var candidate = new BugRecord
        {
            Title = string.IsNullOrWhiteSpace(screen) ? ActualBox.Text.Trim() : screen + " — " + (string.IsNullOrWhiteSpace(ActualBox.Text)?ExpectedBox.Text.Trim():ActualBox.Text.Trim()),
            Screen = screen,
            Frequency = FrequencyBox.SelectedIndex switch { 1=>"Always", 2=>"Sometimes", 3=>"Intermittent", _=>"First time" },
            Result = result, Severity = severity, Expected=ExpectedBox.Text.Trim(), Actual=ActualBox.Text.Trim(), Note=NoteBox.Text.Trim(),
            MarkedScreenshotPath=_screenshotPath, OriginalScreenshotPath=_screenshotPath, Attachments=[.._attachments]
        };
        var duplicate = await DuplicateBugService.FindBestAsync(candidate);
        if (duplicate is not null)
        {
            var pct = Math.Round(duplicate.Similarity * 100);
            var es = _language.StartsWith("Esp", StringComparison.OrdinalIgnoreCase);
            var message = es
                ? $"Posible duplicado detectado ({pct}%).\n\nBug similar: {duplicate.Bug.Title}\nSesión: {duplicate.Session.DisplayName}\n\n¿Deseas guardar este bug de todas formas?"
                : $"Possible duplicate detected ({pct}%).\n\nSimilar bug: {duplicate.Bug.Title}\nSession: {duplicate.Session.DisplayName}\n\nSave this bug anyway?";
            if (!AppDialogWindow.ShowQuestion(this, es ? "Posible bug duplicado" : "Possible duplicate bug", message, _language)) return;
            candidate.PossibleDuplicateBugId = duplicate.Bug.Id;
            candidate.DuplicateSimilarity = duplicate.Similarity;
            candidate.PossibleDuplicateTitle = duplicate.Bug.Title;
        }
        Bug = candidate;
        DialogResult=true;
    }
}
