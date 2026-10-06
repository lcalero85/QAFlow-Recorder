using System.Windows;
using QAFlowRecorder.Models;
using QAFlowRecorder.Services;

namespace QAFlowRecorder.Views;

public partial class AnnotationWindow : Window
{
    public Annotation? Result { get; private set; }
    private readonly string _language;
    public AnnotationWindow(string language="English")
    {
        InitializeComponent();
        WindowThemeHelper.ApplyDarkTitleBar(this);
        _language=language;
        ApplyLanguage();
    }
    private void ApplyLanguage()
    {
        var es=_language.StartsWith("Esp",StringComparison.OrdinalIgnoreCase);
        if(!es)return;
        Title="Anotar resultado";TitleText.Text="Punto de anotación";SubtitleText.Text="Documenta el resultado sin interrumpir el flujo.";ExpectedLabel.Text="Resultado esperado *";ActualLabel.Text="Resultado real";DoubtRadio.Content="Duda";SeverityLabel.Text="Severidad";CaptureLabel.Text="Captura automática";CaptureBox.Content="Adjuntar screenshot";NoteLabel.Text="Nota libre";CancelButton.Content="Cancelar";SaveButton.Content="Guardar anotación";
        ((System.Windows.Controls.ComboBoxItem)SeverityBox.Items[0]).Content="Crítico";((System.Windows.Controls.ComboBoxItem)SeverityBox.Items[1]).Content="Alto";((System.Windows.Controls.ComboBoxItem)SeverityBox.Items[2]).Content="Medio";((System.Windows.Controls.ComboBoxItem)SeverityBox.Items[3]).Content="Bajo";
    }
    private void ResultChanged(object sender, RoutedEventArgs e){ if(SeverityBox is not null) SeverityBox.IsEnabled = ErrorRadio.IsChecked == true; }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if((ExpectedBox.Text??"").Trim().Length<5){AppDialogWindow.ShowInfo(this,"QAFlow Recorder",_language.StartsWith("Esp",StringComparison.OrdinalIgnoreCase)?"El resultado esperado debe tener al menos 5 caracteres.":"The expected result must contain at least 5 characters.",_language);return;}
        var result=ErrorRadio.IsChecked==true?AnnotationResult.Error:DoubtRadio.IsChecked==true?AnnotationResult.Doubt:AnnotationResult.Ok;
        var sev=result==AnnotationResult.Error?SeverityBox.SelectedIndex switch{0=>Severity.Critical,1=>Severity.High,2=>Severity.Medium,3=>Severity.Low,_=>Severity.Medium}:Severity.None;
        var expected = ExpectedBox.Text?.Trim() ?? string.Empty;
        var actual = ActualBox.Text?.Trim() ?? string.Empty;
        var note = NoteBox.Text?.Trim() ?? string.Empty;
        Result=new Annotation{Expected=expected,Actual=actual,Result=result,Severity=sev,Note=note};
        DialogResult=true;
    }
    public bool CaptureRequested => CaptureBox.IsChecked==true;
}
