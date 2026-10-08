using System.Windows;
using QAFlowRecorder.Models;
using QAFlowRecorder.Services;

namespace QAFlowRecorder.Views;

public partial class AnnotationWindow : Window
{
    public Annotation? Result { get; private set; }
    private readonly string _language;

    public AnnotationWindow(string language = "English")
    {
        InitializeComponent();
        WindowThemeHelper.ApplyDarkTitleBar(this);
        _language = language;
        ApplyLanguage();
        Loaded += (_, _) => NoteBox.Focus();
    }

    private void ApplyLanguage()
    {
        var es = _language.StartsWith("Esp", StringComparison.OrdinalIgnoreCase);
        if (!es) return;
        Title = "Agregar anotación";
        TitleText.Text = "Anotación del flujo";
        SubtitleText.Text = "Agrega una nota breve al punto actual del flujo QA.";
        NoteLabel.Text = "Anotación *";
        NoteHelpText.Text = "Usa anotaciones para contexto, observaciones o recordatorios. Usa Bug cuando un defecto necesite seguimiento formal.";
        CaptureBox.Content = "Capturar screenshot al guardar esta anotación";
        CaptureHelpText.Text = "QAFlow oculta sus controles y captura la ventana objetivo activa.";
        CancelButton.Content = "Cancelar";
        SaveButton.Content = "Guardar anotación";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var note = NoteBox.Text?.Trim() ?? string.Empty;
        if (note.Length < 2)
        {
            AppDialogWindow.ShowInfo(this, "QAFlow Recorder",
                _language.StartsWith("Esp", StringComparison.OrdinalIgnoreCase)
                    ? "Escribe una anotación antes de guardar."
                    : "Enter an annotation before saving.", _language);
            return;
        }

        Result = new Annotation
        {
            Note = note,
            Result = AnnotationResult.Ok,
            Severity = Severity.None,
            Expected = string.Empty,
            Actual = string.Empty
        };
        DialogResult = true;
    }

    public bool CaptureRequested => CaptureBox.IsChecked == true;
}
