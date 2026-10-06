using System.Windows;
using QAFlowRecorder.Models;
using QAFlowRecorder.Services;
namespace QAFlowRecorder.Views;
public partial class FlowLibraryWindow:Window
{
    private readonly ReusableFlowLibraryService _service=new(); private readonly bool _es; private List<ReusableFlow> _flows=[];
    public ReusableFlow? SelectedFlow { get; private set; }
    public FlowLibraryWindow(string language="English") { InitializeComponent(); WindowThemeHelper.ApplyDarkTitleBar(this); _es=language.StartsWith("Esp",StringComparison.OrdinalIgnoreCase); Loaded+=async(_,_)=>await ReloadAsync(); ApplyLanguage(); }
    private async Task ReloadAsync(){_flows=await _service.LoadAsync();FlowGrid.ItemsSource=null;FlowGrid.ItemsSource=_flows;}
    private void ApplyLanguage(){if(!_es)return;Title="Flujos reutilizables — QAFlow";TitleText.Text="Flujos reutilizables";SubtitleText.Text="Usa flujos manuales validados como referencia de regresión.";NameColumn.Header="Flujo";TypeColumn.Header="Tipo";StepsColumn.Header="Pasos";UpdatedColumn.Header="Actualizado";DeleteButton.Content="Eliminar";CloseButton.Content="Cerrar";UseButton.Content="Usar como referencia";}
    private async void Delete_Click(object sender,RoutedEventArgs e){if(FlowGrid.SelectedItem is not ReusableFlow f)return;if(!AppDialogWindow.ShowQuestion(this,"QAFlow",_es?"¿Eliminar este flujo reutilizable?":"Delete this reusable flow?",_es?"Español":"English"))return;await _service.DeleteAsync(f.Id);await ReloadAsync();}
    private void Use_Click(object sender,RoutedEventArgs e){if(FlowGrid.SelectedItem is not ReusableFlow f)return;SelectedFlow=f;DialogResult=true;}
}
