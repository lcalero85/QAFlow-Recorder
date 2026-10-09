using System.Windows;
using System.Windows.Controls;
using QAFlowRecorder.Services;

namespace QAFlowRecorder.Views;

public partial class BookmarkWindow : Window
{
    public string Category { get; private set; } = "Important";
    public string Note { get; private set; } = "";
    private readonly bool _es;
    public BookmarkWindow(string language)
    {
        InitializeComponent();
        WindowThemeHelper.ApplyDarkTitleBar(this);
        _es = language.StartsWith("Esp", StringComparison.OrdinalIgnoreCase);
        if (_es)
        {
            Title="Agregar marcador"; TitleText.Text="Marcador de sesión"; SubtitleText.Text="Marca un momento importante sin interrumpir la prueba."; CategoryLabel.Text="Tipo"; NoteLabel.Text="Nota corta (opcional)"; CancelButton.Content="Cancelar"; SaveButton.Content="★ Guardar marcador";
            ((ComboBoxItem)CategoryBox.Items[0]).Content="Importante"; ((ComboBoxItem)CategoryBox.Items[1]).Content="Revisar después"; ((ComboBoxItem)CategoryBox.Items[2]).Content="Comportamiento extraño"; ((ComboBoxItem)CategoryBox.Items[3]).Content="Punto crítico";
        }
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var idx = CategoryBox.SelectedIndex;
        Category = _es ? idx switch {1=>"Revisar después",2=>"Comportamiento extraño",3=>"Punto crítico",_=>"Importante"} : idx switch {1=>"Review later",2=>"Unexpected behavior",3=>"Critical point",_=>"Important"};
        Note=NoteBox.Text.Trim(); DialogResult=true;
    }
    private void Cancel_Click(object sender, RoutedEventArgs e)=>DialogResult=false;
}
