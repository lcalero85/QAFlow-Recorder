using System.Windows;
using QAFlowRecorder.Models;
using QAFlowRecorder.Services;
namespace QAFlowRecorder.Views;

public partial class ExportWindow : Window
{
    public bool ExportTc => Tc.IsChecked == true;
    public bool ExportPdf => Pdf.IsChecked == true;
    public bool ExportBug => Bug.IsChecked == true;
    public bool ExportZip => Zip.IsChecked == true;

    public ExportWindow(TestSession s, string language="English")
    {
        InitializeComponent();
        WindowThemeHelper.ApplyDarkTitleBar(this);
        var es = language.StartsWith("Esp", StringComparison.OrdinalIgnoreCase);
        Title = Localizer.T("ExportTitle", language);
        TitleText.Text = Localizer.T("ExportTitle", language);
        Summary.Text = $"{s.DisplayName} · {s.DurationText} · {s.ActionCount} {(es ? "interacciones" : "interactions")} · {s.Bugs.Count} bug(s)";
        Tc.Content = es ? "Registro estructurado de sesión" : "Structured session record";
        Pdf.Content = es ? "Reporte de ejecución QA (PDF)" : "QA execution report (PDF)";
        Bug.Content = es ? "Reporte de bugs (PDF)" : "Bug report (PDF)";
        Zip.Content = es ? "Empaquetar como ZIP" : "Package as ZIP";
        CancelButton.Content = Localizer.T("Cancel", language);
        ExportButton.Content = es ? "Exportar" : "Export";
        Bug.IsChecked = s.Bugs.Count > 0;
        Bug.IsEnabled = s.Bugs.Count > 0;
        if (es)
        {
            TcDesc.Text = "Markdown + JSON con el registro ordenado de interacciones.";
            PdfDesc.Text = "Reporte profesional de ejecución con pasos ordenados y evidencia visual.";
            BugDesc.Text = "Reporte de defectos separado con evidencia marcada y pasos para reproducir.";
            ZipDesc.Text = "Empaqueta reportes y evidencias. Las sesiones multipartes mantienen cada parte claramente separada.";
        }
    }

    private void Export_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
