using System.Windows;
using System.Windows.Controls;
using QAFlowRecorder.Models;
using QAFlowRecorder.Services;

namespace QAFlowRecorder.Views;

public partial class ManualChecklistWindow : Window
{
    private readonly List<ManualChecklistItem> _items;
    public ManualChecklistWindow(List<ManualChecklistItem> items, string language)
    {
        InitializeComponent(); WindowThemeHelper.ApplyDarkTitleBar(this); _items=items; ChecklistGrid.ItemsSource=_items;
        if(language.StartsWith("Esp",StringComparison.OrdinalIgnoreCase)) { Title="Checklist manual";TitleText.Text="Checklist de validación manual";SubtitleText.Text="Actualiza cada validación durante la sesión.";ValidationColumn.Header="Validación";StatusColumn.Header="Estado";NoteColumn.Header="Nota";HintText.Text="Haz clic en el estado para cambiar: No probado → Aprobado → Fallido → Bloqueado.";DoneButton.Content="Listo"; }
    }
    private void CycleStatus_Click(object sender, RoutedEventArgs e)
    {
        if(sender is not Button { Tag: ManualChecklistItem item }) return;
        item.Status=item.Status switch { ChecklistStatus.NotTested=>ChecklistStatus.Passed, ChecklistStatus.Passed=>ChecklistStatus.Failed, ChecklistStatus.Failed=>ChecklistStatus.Blocked, _=>ChecklistStatus.NotTested };
        ChecklistGrid.Items.Refresh();
    }
    private void Done_Click(object sender,RoutedEventArgs e){ ChecklistGrid.CommitEdit(); DialogResult=true; }
}
