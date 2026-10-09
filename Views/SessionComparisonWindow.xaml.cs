using System.Windows;
using QAFlowRecorder.Services;
namespace QAFlowRecorder.Views;
public partial class SessionComparisonWindow:Window
{
 public SessionComparisonWindow(SessionComparisonSummary x,string lang){InitializeComponent();WindowThemeHelper.ApplyDarkTitleBar(this);var es=lang.StartsWith("Esp",StringComparison.OrdinalIgnoreCase);SubtitleText.Text=$"{x.Current.DisplayName}  ↔  {x.Previous.DisplayName}";DurationValue.Text=$"{FormatDelta(x.DurationDelta)} ({SignedPercent(x.DurationDeltaPercent)})";ActionsValue.Text=Signed(x.InteractionDelta);BugValue.Text=Signed(x.BugDelta);StepValue.Text=$"+{x.NewStepCount} / -{x.MissingStepCount}";VisualValue.Text=x.VisualChangeCount.ToString();NewList.ItemsSource=x.NewSteps;MissingList.ItemsSource=x.MissingSteps;if(es){Title="Comparación de sesiones";TitleText.Text="Resumen de comparación";DurationLabel.Text="Duración";ActionsLabel.Text="Interacciones";BugLabel.Text="Bugs";StepLabel.Text="Cambios de flujo";VisualLabel.Text="Cambios visuales";NewTitle.Text="Nuevo en esta ejecución";MissingTitle.Text="Falta vs ejecución anterior";CloseButton.Content="Cerrar";}}
 private static string Signed(int v)=>v>0?$"+{v}":v.ToString();
 private static string SignedPercent(double v)=>(v>0?"+":"")+$"{v:0.#}%";
 private static string FormatDelta(TimeSpan v)=>(v<TimeSpan.Zero?"-":"+")+v.Duration().ToString(@"hh\:mm\:ss");
 private void Close_Click(object sender,RoutedEventArgs e)=>Close();
}
