using System.Windows;
using System.Windows.Controls;
using QAFlowRecorder.Models;
using QAFlowRecorder.Services;

namespace QAFlowRecorder.Views;

public partial class SessionReviewWindow : Window
{
    private readonly TestSession _session;
    private readonly bool _es;
    private readonly List<ReviewRow> _rows;
    private readonly RiskAssessment _risk;
    private EvidenceQualityResult _quality = new();

    public SessionReviewWindow(TestSession session, string language = "English")
    {
        InitializeComponent();
        WindowThemeHelper.ApplyDarkTitleBar(this);
        _session = session;
        _es = language.StartsWith("Esp", StringComparison.OrdinalIgnoreCase);
        _rows = session.Events
            .Where(ReportLanguage.IsAction)
            .OrderBy(e => e.Timestamp)
            .Select(e => new ReviewRow
            {
                Event = e,
                Include = !e.ExcludeFromReport,
                Time = e.Timestamp.LocalDateTime.ToString("HH:mm:ss"),
                Target = ReportLanguage.Target(e),
                Description = string.IsNullOrWhiteSpace(e.CustomDescription) ? SmartNarrativeService.Describe(e, _es) : e.CustomDescription,
                Important = e.IsImportant,
                MaskSensitive = e.MaskSensitiveEvidence,
                Checkpoint = e.IsCheckpoint,
                CheckpointTitle = e.CheckpointTitle
            }).ToList();
        _risk = RiskAssessmentService.Analyze(session);
        _quality = EvidenceQualityService.Analyze(session);
        ReviewGrid.ItemsSource = _rows;
        ApplyLanguage();
        UpdateSummary();
        if (_rows.Count > 0) ReviewGrid.SelectedIndex = 0;
    }

    private void ApplyLanguage()
    {
        if (!_es) return;
        Title = "Revisar sesión — QAFlow";
        TitleText.Text = "Revisar sesión";
        SubtitleText.Text = "Revisa los pasos capturados antes de generar la documentación QA final.";
        CapturedLabel.Text = "CAPTURADOS";
        IncludedLabel.Text = "EN REPORTE";
        CheckpointLabel.Text = "HITOS";
        QualityLabel.Text = "LISTO PARA REPORTE";
        IncludeColumn.Header = "Incluir";
        TimeColumn.Header = "Hora";
        TargetColumn.Header = "Aplicación / ventana";
        DescriptionColumn.Header = "Descripción del paso";
        AdvancedExpander.Header = "Opciones avanzadas del paso seleccionado";
        ImportantCheck.Content = "Paso importante";
        MaskCheck.Content = "Ocultar evidencia";
        CheckpointCheck.Content = "Hito";
        StageTitleLabel.Text = "Título del hito";
        HintText.Text = "Conserva solo los pasos útiles y mejora descripciones poco claras. La evidencia original nunca se elimina.";
        MoreActionsExpander.Header = "Más acciones de revisión";
        SaveBaselineButton.Content = "Actualizar baseline visual";
        SaveFlowButton.Content = "Guardar flujo reutilizable";
        CancelButton.Content = "Volver";
        ContinueButton.Content = "Continuar a exportación";
    }

    private bool _syncingAdvanced;

    private ReviewRow? SelectedRow => ReviewGrid.SelectedItem as ReviewRow;

    private void ReviewGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _syncingAdvanced = true;
        var row = SelectedRow;
        ImportantCheck.IsEnabled = row is not null;
        MaskCheck.IsEnabled = row is not null;
        CheckpointCheck.IsEnabled = row is not null;
        StageTitleBox.IsEnabled = row is not null;
        ImportantCheck.IsChecked = row?.Important ?? false;
        MaskCheck.IsChecked = row?.MaskSensitive ?? false;
        CheckpointCheck.IsChecked = row?.Checkpoint ?? false;
        StageTitleBox.Text = row?.CheckpointTitle ?? string.Empty;
        _syncingAdvanced = false;
    }

    private void AdvancedOption_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncingAdvanced || SelectedRow is not { } row) return;
        row.Important = ImportantCheck.IsChecked == true;
        row.MaskSensitive = MaskCheck.IsChecked == true;
        row.Checkpoint = CheckpointCheck.IsChecked == true;
        UpdateSummary();
    }

    private void StageTitleBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingAdvanced || SelectedRow is not { } row) return;
        row.CheckpointTitle = StageTitleBox.Text;
    }

    private void ReviewGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(UpdateSummary));
    }

    private void UpdateSummary()
    {
        CapturedValue.Text = _rows.Count.ToString();
        CheckpointValue.Text = _rows.Count(r => r.Checkpoint).ToString();
        IncludedValue.Text = _rows.Count(r => r.Include).ToString();
        _quality = EvidenceQualityService.Analyze(_session);
        QualityValue.Text = $"{_quality.ReadinessPercent}%";
        QualityValue.ToolTip = string.Join("\n", _quality.Issues.Take(6).Select(x => _es ? x.MessageSpanish : x.MessageEnglish));
    }


    private async void SaveBaseline_Click(object sender, RoutedEventArgs e)
    {
        var count = await VisualBaselineService.SaveFromSessionAsync(_session);
        AppDialogWindow.ShowInfo(this, "QAFlow", _es ? $"Baseline visual actualizado para {count} objetivo(s)." : $"Visual baseline updated for {count} target(s).", _es ? "Español" : "English");
    }

    private async void SaveFlow_Click(object sender, RoutedEventArgs e)
    {
        var flow = await new ReusableFlowLibraryService().SaveFromSessionAsync(_session);
        AppDialogWindow.ShowInfo(this, "QAFlow", _es ? $"Flujo reutilizable guardado: {flow.Name}" : $"Reusable flow saved: {flow.Name}", _es ? "Español" : "English");
    }

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        ReviewGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        ReviewGrid.CommitEdit(DataGridEditingUnit.Row, true);
        foreach (var row in _rows)
        {
            row.Event.ExcludeFromReport = !row.Include;
            row.Event.CustomDescription = row.Description?.Trim() ?? "";
            row.Event.IsImportant = row.Important;
            row.Event.MaskSensitiveEvidence = row.MaskSensitive;
            if (row.MaskSensitive)
            {
                var shot = _session.Screenshots.FirstOrDefault(s => s.EventId == row.Event.Id && File.Exists(s.FilePath));
                if (shot is not null && !shot.SensitiveMaskApplied)
                {
                    if (PrivacyMaskService.ApplyToFile(shot.FilePath, row.Event, IntPtr.Zero, PrivacyMaskMode.Manual)) shot.SensitiveMaskApplied = true;
                }
            }
            row.Event.IsCheckpoint = row.Checkpoint;
            row.Event.CheckpointTitle = row.CheckpointTitle?.Trim() ?? "";
            if (row.Event.IsCheckpoint && string.IsNullOrWhiteSpace(row.Event.CheckpointKind)) row.Event.CheckpointKind = "Manual";
        }
        DialogResult = true;
    }

    private sealed class ReviewRow
    {
        public required QaEvent Event { get; init; }
        public bool Include { get; set; }
        public string Time { get; set; } = "";
        public string Target { get; set; } = "";
        public string Description { get; set; } = "";
        public bool Important { get; set; }
        public bool MaskSensitive { get; set; }
        public bool Checkpoint { get; set; }
        public string CheckpointTitle { get; set; } = "";
    }
}
