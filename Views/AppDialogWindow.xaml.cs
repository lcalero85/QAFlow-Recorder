using System.Windows;
using QAFlowRecorder.Services;

namespace QAFlowRecorder.Views;

public partial class AppDialogWindow : Window
{
    public bool Accepted { get; private set; }
    public string InputValue => PasswordInput.Password;

    public AppDialogWindow(string title, string message, string language, string kind="info", bool question=false, bool password=false)
    {
        InitializeComponent();
        WindowThemeHelper.ApplyDarkTitleBar(this);
        Title = title; TitleText.Text = title; MessageText.Text = message;
        var es = language.StartsWith("Esp", StringComparison.OrdinalIgnoreCase);
        IconText.Text = kind switch { "error" => "!", "warning" => "!", "question" => "?", _ => "i" };
        IconText.Foreground = kind switch { "error" => System.Windows.Media.Brushes.LightCoral, "warning" => System.Windows.Media.Brushes.Orange, _ => System.Windows.Media.Brushes.LightSkyBlue };
        PrimaryButton.Content = question ? (es ? "Sí" : "Yes") : (es ? "Aceptar" : "OK");
        SecondaryButton.Content = es ? "Cancelar" : "Cancel";
        SecondaryButton.Visibility = question || password ? Visibility.Visible : Visibility.Collapsed;
        PasswordInput.Visibility = password ? Visibility.Visible : Visibility.Collapsed;
        if (password) PrimaryButton.Content = es ? "Continuar" : "Continue";
        Loaded += (_,_) => { if(password) PasswordInput.Focus(); };
    }

    private void Primary_Click(object sender, RoutedEventArgs e) { Accepted=true; DialogResult=true; }
    private void Secondary_Click(object sender, RoutedEventArgs e) { Accepted=false; DialogResult=false; }

    public static bool ShowQuestion(Window owner, string title, string message, string language)
    { var d=new AppDialogWindow(title,message,language,"question",true){Owner=owner}; return d.ShowDialog()==true; }
    public static void ShowInfo(Window owner, string title, string message, string language)
    { var d=new AppDialogWindow(title,message,language,"info"){Owner=owner}; d.ShowDialog(); }
    public static void ShowError(Window owner, string title, string message, string language)
    { var d=new AppDialogWindow(title,message,language,"error"){Owner=owner}; d.ShowDialog(); }
    public static string? PromptPassword(Window owner, string title, string message, string language)
    { var d=new AppDialogWindow(title,message,language,"info",false,true){Owner=owner}; return d.ShowDialog()==true ? d.InputValue : null; }
}
