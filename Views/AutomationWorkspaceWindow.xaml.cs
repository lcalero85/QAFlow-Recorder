using System.Diagnostics;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using QAFlowRecorder.Models;
using QAFlowRecorder.Services;

namespace QAFlowRecorder.Views;

public partial class AutomationWorkspaceWindow : Window
{
    private readonly AutomationWorkspaceStore _store = new();
    private readonly SessionStore _sessionStore = new();
    private readonly AutomationRunStore _runStore = new();
    private readonly AutomationSuiteRunStore _suiteRunStore = new();
    private readonly AppSettings _settings;
    private readonly ObservableCollection<AutomationProject> _projects = [];
    private readonly ObservableCollection<TestSession> _recordedSessions = [];
    private readonly ObservableCollection<AutomationTestRun> _runs = [];
    private readonly ObservableCollection<AutomationSuiteRun> _suiteRuns = [];
    private readonly ObservableCollection<AutomationQueueItem> _queueItems = [];
    private readonly AutomationRunQueueStore _queueStore = new();
    private AutomationQueueSettings _queueSettings = new();
    private CancellationTokenSource? _queueCts;
    private readonly AutomationQualityAnalyticsService _qualityAnalyticsService = new();
    private readonly AutomationGovernanceService _governanceService = new();
    private AutomationGovernanceDocument? _governanceDocument;
    private AutomationQualitySnapshot? _qualitySnapshot;
    private ConversionPreview? _conversionPreview;
    private bool _loading;
    private Guid? _selectedVariableId;
    private readonly AutomationSchedulerService _scheduler = new();

    private AutomationProject? CurrentProject => ProjectsList.SelectedItem as AutomationProject;
    private AutomationEnvironmentProfile? CurrentEnvironment => EnvironmentsGrid.SelectedItem as AutomationEnvironmentProfile;
    private AutomationSchedule? CurrentSchedule => SchedulesGrid.SelectedItem as AutomationSchedule;
    private AutomationSuite? CurrentSuite => SuitesGrid.SelectedItem as AutomationSuite;
    private AutomationTestCase? CurrentCase => CasesGrid.SelectedItem as AutomationTestCase;
    private AutomationDataSet? CurrentDataSet => DataSetsGrid.SelectedItem as AutomationDataSet;
    private AutomationTestCase? AssertionCase => AssertionCaseBox.SelectedItem as AutomationTestCase;
    private AutomationTestStep? AssertionStep => AssertionStepBox.SelectedItem as AutomationTestStep;
    private ReusableAutomationComponent? CurrentComponent => ComponentsGrid.SelectedItem as ReusableAutomationComponent;
    private AutomationTestCase? RecoveryCase => RecoveryCaseBox.SelectedItem as AutomationTestCase;
    private AutomationTestStep? RecoveryStep => RecoveryStepBox.SelectedItem as AutomationTestStep;

    public AutomationWorkspaceWindow(AppSettings settings)
    {
        InitializeComponent();
        WindowThemeHelper.ApplyDarkTitleBar(this);
        _settings = settings;
        ModeColumn.ItemsSource = Enum.GetValues<AutomationCaseMode>();
        StatusColumn.ItemsSource = Enum.GetValues<AutomationCaseStatus>();
        FailureActionBox.ItemsSource = Enum.GetValues<AutomationFailureAction>();
        ScheduleFrequencyBox.ItemsSource = Enum.GetValues<AutomationScheduleFrequency>();
        ScheduleFrequencyBox.SelectedIndex = 0;
        Loaded += async (_, _) => await LoadAsync();
        ProjectHintText.Text = "Phase 12 adds test case versioning, approval workflow and a local audit trail without changing Recorder behavior.";
        ConversionHintText.Text = "DOM selectors remain the preferred source. Desktop and out-of-scope steps stay manual/evidence checkpoints.";
        StatusText.Text = "Local workspace · Phase 12 governance enabled · web automation only · Recorder remains independent";
        ApplyLanguage();
    }

    private bool IsSpanish => _settings.UiLanguage.StartsWith("Esp", StringComparison.OrdinalIgnoreCase);

    private void ApplyLanguage()
    {
        if (!IsSpanish) return;
        Title = "QAFlow · Espacio de automatización";
        TitleText.Text = "Espacio de automatización";
        SubtitleText.Text = "Organiza activos de automatización web sin cambiar el comportamiento del Recorder.";
        ModeBadgeText.Text = "SOLO WEB · FASE DE DISEÑO";
        ProjectsLabel.Text = "Proyectos";
        NewProjectButton.Content = "＋ Nuevo";
        DeleteProjectButton.Content = "Eliminar proyecto";
        ProjectNameLabel.Text = "Nombre del proyecto";
        BaseUrlLabel.Text = "URL base";
        BrowserLabel.Text = "Navegador";
        ProjectHintText.Text = "La Fase 12 mantiene el Recorder independiente y agrega versionado, aprobación y trazabilidad local de casos de prueba.";
        ModeBadgeText.Text = "SOLO WEB · QUEUE + PARALLEL + MATRIX";
        SuitesTab.Header = "Suites";
        SuitesHelpText.Text = "Agrupa casos relacionados dentro de suites.";
        NewSuiteButton.Content = "＋ Suite";
        DeleteSuiteButton.Content = "Eliminar";
        CasesTab.Header = "Casos de prueba";
        CasesContextText.Text = "Selecciona una suite para administrar sus casos.";
        NewCaseButton.Content = "＋ Caso de prueba";
        DeleteCaseButton.Content = "Eliminar";
        CaseDescriptionLabel.Text = "Descripción";
        CaseExpectedLabel.Text = "Resultado esperado";
        SaveButton.Content = "Guardar espacio";
        CloseButton.Content = "Cerrar";
        QueueMatrixTab.Header = "Cola y matriz de navegadores";
        QueueTitleText.Text = "Cola de ejecución y matriz de navegadores";
        QueueHelpText.Text = "Encola la suite seleccionada para Chrome, Edge o ambos. QAFlow ejecuta trabajos web independientes en paralelo con un límite de concurrencia controlado.";
        QueueSuiteLabel.Text = "Suite seleccionada";
        QueueEnvironmentLabel.Text = "Ambiente";
        ConcurrencyLabel.Text = "Paralelo";
        QueueChromeCheck.Content = "Chrome";
        QueueEdgeCheck.Content = "Edge";
        StopBatchOnFailureCheck.Content = "Detener los trabajos restantes de la matriz si falla un navegador";
        QueueStateText.Text = "Cola inactiva";
        QueueMatrixButton.Content = "＋ Encolar matriz";
        StartQueueButton.Content = "▶ Iniciar cola";
        CancelQueueButton.Content = "Detener";
        OpenQueuedReportButton.Content = "Abrir reporte";
        RecordedSessionsTab.Header = "Sesiones grabadas";
        RecordedSessionsHelpText.Text = "Convierte sesiones finalizadas del Recorder en borradores de casos web.";
        RecordedSessionsScopeText.Text = "Solo la actividad dentro de la URL base del proyecto se convierte en candidata de automatización.";
        TargetSuiteLabel.Text = "Suite destino";
        ConversionNameLabel.Text = "Nombre del caso de prueba";
        PreviewWebLabel.Text = "WEB EN ALCANCE";
        PreviewManualLabel.Text = "MANUAL / EVIDENCIA";
        PreviewConfidenceLabel.Text = "PREPARACIÓN";
        ConversionHintText.Text = "Los selectores DOM siguen siendo la fuente preferida. Los pasos de escritorio o fuera de alcance permanecen manuales/evidencia.";
        ConvertSessionButton.Content = "Convertir a caso de prueba";
        InstrumentationTab.Header = "Instrumentación web";
        InstrumentationTitleText.Text = "Instrumentación DOM e inteligencia de selectores";
        InstrumentationHelpText.Text = "QAFlow enriquece las acciones web grabadas con metadata DOM y prioriza selectores estables. Nunca captura los valores escritos.";
        InstrumentationStepsTitle.Text = "Conectar Chrome o Edge";
        InstrumentationStepsText.Text = "1. Abre la página de extensiones del navegador.\n2. Activa el modo desarrollador.\n3. Selecciona Cargar descomprimida y elige la carpeta WebInstrumentationExtension de QAFlow.\n4. Mantén QAFlow en Hybrid Testing con una URL base válida.\n\nLa extensión solo se comunica con 127.0.0.1 en este equipo y solo envía metadata DOM dentro del alcance configurado.";
        SelectorPolicyText.Text = "Prioridad de selectores: data-testid → id estable → name → aria-label → role + nombre accesible → CSS → XPath. UI Automation queda solo como respaldo.";
        InstrumentationPrivacyText.Text = "No se capturan valores de campos. Recorder Only desactiva la aceptación de eventos DOM.";
        OpenExtensionFolderButton.Content = "Abrir carpeta de extensión";
        RefreshInstrumentationButton.Content = "Actualizar estado";
        RunsTab.Header = "Ejecuciones";
        RunsTitleText.Text = "Ejecuciones de automatización web";
        RunsHelpText.Text = "El historial de ejecución es inmutable y se guarda localmente con evidencia por paso y un reporte HTML offline.";
        RunsScopeText.Text = "La automatización continúa siendo solo web y respeta la URL base y dominios permitidos del proyecto.";
        OpenRunFolderButton.Content = "Abrir carpeta del run";
        OpenRunReportButton.Content = "Abrir reporte HTML";
        RunCaseButton.Content = "▶ Ejecutar seleccionado";
        RunSuiteButton.Content = "▶ Ejecutar suite";
        SuiteRunsTab.Header = "Ejecuciones de suite";
        SuiteRunsTitleText.Text = "Historial de ejecuciones de suite";
        SuiteRunsHelpText.Text = "Ejecuta suites completas en secuencia y consolida los resultados en un reporte HTML offline.";
        SuiteRunsScopeText.Text = "La orquestación de suites continúa siendo solo web y respeta la URL base, dominios permitidos y políticas de recuperación.";
        OpenSuiteRunFolderButton.Content = "Abrir carpeta";
        OpenSuiteRunReportButton.Content = "Abrir reporte HTML";
        SendSuiteRunButton.Content = "✉ Enviar reporte";
        DeliveryTab.Header = "Entrega de reportes";
        DeliveryTitleText.Text = "Envío de reportes por correo";
        DeliveryHelpText.Text = "Envía el reporte HTML de suite y PDFs QA/Bug seleccionados mediante el servidor SMTP de tu organización. La contraseña se guarda en el Administrador de credenciales de Windows.";
        DeliveryEnabledCheck.Content = "Habilitar envío por correo para este proyecto";
        AutoSendCheck.Content = "Enviar automáticamente al finalizar la suite";
        AttachSuiteHtmlCheck.Content = "Adjuntar HTML de suite";
        AttachCaseHtmlCheck.Content = "Adjuntar HTML individuales de casos";
        PdfAttachmentsLabel.Text = "PDF QA/Bug adjuntos";
        AddPdfButton.Content = "＋ Agregar PDF";
        RemovePdfButton.Content = "Quitar";
        TestEmailButton.Content = "Enviar correo de prueba";
        SaveDeliveryButton.Content = "Guardar configuración de envío";
        DeliveryStatusText.Text = "La configuración de envío pertenece únicamente a este proyecto.";
        EnvironmentsTab.Header = "Ambientes y programación";
        EnvironmentsTitleText.Text = "Ambientes web";
        EnvironmentsHelpText.Text = "Reutiliza las mismas suites contra diferentes ambientes web sin duplicar casos de prueba.";
        SchedulingTitleText.Text = "Ejecuciones programadas";
        SchedulingHelpText.Text = "Registra regresiones web recurrentes en el Programador de tareas de Windows usando el modo de ejecución sin interfaz de QAFlow.";
        NewEnvironmentButton.Content = "＋ Nuevo";
        SaveEnvironmentButton.Content = "Guardar ambiente";
        DeleteEnvironmentButton.Content = "Eliminar";
        SaveScheduleButton.Content = "Guardar";
        RegisterScheduleButton.Content = "Registrar en Windows";
        DeleteScheduleButton.Content = "Eliminar";
        DataAssertionsTab.Header = "Datos y validaciones";
        TestDataTitleText.Text = "Conjuntos de datos";
        TestDataHelpText.Text = "Las variables resuelven expresiones como ${username}. Los secretos permanecen en el Administrador de credenciales de Windows.";
        NewDataSetButton.Content = "＋ Datos";
        DeleteDataSetButton.Content = "Eliminar";
        VariableNameLabel.Text = "Variable";
        VariableValueLabel.Text = "Valor";
        VariableSecretCheck.Content = "Secreto";
        SaveVariableButton.Content = "Agregar / actualizar";
        DeleteVariableButton.Content = "Eliminar variable seleccionada";
        AssertionsTitleText.Text = "Datos por paso y validaciones";
        AssertionsHelpText.Text = "Vincula inputs/selects a datos de prueba y valida el estado esperado del navegador después de un paso.";
        AssertionCaseLabel.Text = "Caso de prueba";
        AssertionStepLabel.Text = "Paso";
        DefaultDataSetLabel.Text = "Conjunto de datos predeterminado";
        ValueExpressionLabel.Text = "Expresión de valor del paso";
        AddAssertionButton.Content = "＋ Validación";
        DeleteAssertionButton.Content = "Eliminar";
        ComponentsRecoveryTab.Header = "Componentes y recuperación";
        ComponentsTitleText.Text = "Componentes web reutilizables";
        ComponentsHelpText.Text = "Convierte flujos estables en bloques reutilizables e insértalos en otros casos de prueba.";
        CreateComponentButton.Content = "Guardar caso seleccionado como componente";
        InsertComponentButton.Content = "Insertar en caso seleccionado";
        DeleteComponentButton.Content = "Eliminar";
        RecoveryTitleText.Text = "Recuperación y self-healing";
        RecoveryHelpText.Text = "Configura reintentos y comportamiento ante fallos. Si un selector estable cambia, QAFlow intenta una recuperación semántica conservadora antes de solicitar intervención o fallar.";
        SaveRecoveryButton.Content = "Guardar política de recuperación";
        HealingStatusText.Text = "El self-healing solo acepta coincidencias semánticas únicas y seguras. Los selectores recuperados se incorporan al perfil del paso.";
        QualityAnalyticsTab.Header = "Inteligencia de calidad";
        QualityTitleText.Text = "Inteligencia de calidad";
        QualityHelpText.Text = "Analiza el historial de ejecuciones para detectar casos flaky, patrones recurrentes de fallo y tendencias sin modificar la lógica de prueba.";
        QualityPassLabel.Text = "APROBACIÓN HISTÓRICA";
        QualityFlakyLabel.Text = "CASOS FLAKY";
        QualityFailuresLabel.Text = "FALLOS RECURRENTES";
        QualityRunsLabel.Text = "SUITE RUNS";
        StabilityTitleText.Text = "Estabilidad de casos";
        StabilityHintText.Text = "La clasificación flaky requiere resultados históricos exitosos y no exitosos; QAFlow nunca modifica el caso automáticamente.";
        FailureClustersTitleText.Text = "Agrupación de fallos";
        TrendTitleText.Text = "Tendencia reciente de suites";
        QualityStateText.Text = "Selecciona un proyecto para analizar su historial local de automatización.";
        GovernanceTab.Header = "Gobernanza y auditoría";
        GovernanceTitleText.Text = "Versionado, aprobación y auditoría";
        GovernanceHelpText.Text = "Controla versiones inmutables de casos, revisiones y aprobación antes de ejecuciones automatizadas sin alterar el Recorder.";
        RequireApprovalCheck.Content = "Requerir versión aprobada para ejecutar casos Assisted/Automated";
        VersionNoteLabel.Text = "Nota de versión / revisión";
        CreateVersionButton.Content = "Crear versión";
        SubmitReviewButton.Content = "Enviar a revisión";
        ApproveButton.Content = "Aprobar";
        RejectButton.Content = "Rechazar";
        RestoreVersionButton.Content = "Restaurar como nueva versión";
        RefreshAuditButton.Content = "Actualizar";
        RefreshQualityButton.Content = "Actualizar";
        ExportQualityButton.Content = "Exportar HTML";
        RerunFailedButton.Content = "↻ Reejecutar casos fallidos";
        StatusText.Text = "Espacio local · Fase 11 inteligencia de calidad habilitada · automatización solo web · automatización de escritorio deshabilitada";
    }

    private async Task LoadAsync()
    {
        _loading = true;
        AssertionTypeBox.ItemsSource = Enum.GetValues<AutomationAssertionType>();
        AssertionTypeBox.SelectedIndex = 0;
        FailureActionBox.SelectedIndex = 0;
        var loaded = await _store.LoadProjectsAsync();
        foreach (var project in loaded) _projects.Add(project);
        ProjectsList.ItemsSource = _projects;

        var sessions = await _sessionStore.LoadAllAsync();
        foreach (var session in sessions.Where(s => s.State == SessionState.Finished).OrderByDescending(s => s.StartedAt))
            _recordedSessions.Add(session);
        RecordedSessionsGrid.ItemsSource = _recordedSessions;

        var runs = await _runStore.LoadAllAsync();
        foreach (var run in runs) _runs.Add(run);
        RunsGrid.ItemsSource = _runs;

        var suiteRuns = await _suiteRunStore.LoadAllAsync();
        foreach (var run in suiteRuns) _suiteRuns.Add(run);
        SuiteRunsGrid.ItemsSource = _suiteRuns;

        var queue = await _queueStore.LoadAsync();
        foreach (var item in queue.OrderByDescending(x => x.CreatedAt)) _queueItems.Add(item);
        RunQueueGrid.ItemsSource = _queueItems;
        _queueSettings = await _queueStore.LoadSettingsAsync();
        ConcurrencyBox.SelectedIndex = Math.Clamp(_queueSettings.MaxConcurrency, 1, 4) - 1;
        StopBatchOnFailureCheck.IsChecked = _queueSettings.StopBatchOnFailure;
        _loading = false;
        if (_projects.Count > 0) ProjectsList.SelectedIndex = 0;
        RefreshEditorState();
        RefreshInstrumentationStatus();
        await RefreshQualityAnalyticsAsync();
    }

    private async Task PersistAsync(string? message = null)
    {
        if (_loading) return;
        await _store.SaveProjectsAsync(_projects);
        StatusText.Text = message ?? (IsSpanish ? "Cambios guardados localmente." : "Changes saved locally.");
        ProjectsList.Items.Refresh();
        SuitesGrid.Items.Refresh();
        CasesGrid.Items.Refresh();
    }

    private void RefreshEditorState()
    {
        _loading = true;
        var p = CurrentProject;
        ProjectNameBox.IsEnabled = p is not null;
        BaseUrlBox.IsEnabled = p is not null;
        BrowserBox.IsEnabled = p is not null;
        NewSuiteButton.IsEnabled = p is not null;
        DeleteProjectButton.IsEnabled = p is not null;
        RunSuiteButton.IsEnabled = CurrentSuite is not null;
        ProjectNameBox.Text = p?.Name ?? "";
        BaseUrlBox.Text = p?.BaseUrl ?? "";
        BrowserBox.SelectedIndex = string.Equals(p?.DefaultBrowser, "Edge", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        SuitesGrid.ItemsSource = p?.Suites;
        ConversionSuiteBox.ItemsSource = p?.Suites;
        DataSetsGrid.ItemsSource = p?.DataSets;
        if (DataSetsGrid.SelectedItem is null && (p?.DataSets.Count ?? 0) > 0) DataSetsGrid.SelectedIndex = 0;
        AssertionCaseBox.ItemsSource = p?.Suites.SelectMany(s => s.TestCases).ToList();
        DefaultDataSetBox.ItemsSource = p?.DataSets;
        ComponentsGrid.ItemsSource = p?.Components;
        RecoveryCaseBox.ItemsSource = p?.Suites.SelectMany(s => s.TestCases).ToList();
        if (RecoveryCaseBox.SelectedItem is null && (p?.TestCaseCount ?? 0) > 0) RecoveryCaseBox.SelectedIndex = 0;
        if (p?.Suites.Count > 0)
        {
            SuitesGrid.SelectedIndex = Math.Max(0, SuitesGrid.SelectedIndex);
            if (ConversionSuiteBox.SelectedItem is null) ConversionSuiteBox.SelectedIndex = 0;
        }
        else
        {
            CasesGrid.ItemsSource = null;
            CasesContextText.Text = IsSpanish ? "Selecciona una suite para administrar sus casos." : "Select a suite to manage its test cases.";
        }
        EnvironmentsGrid.ItemsSource = p?.Environments;
        SchedulesGrid.ItemsSource = p?.Schedules;
        ScheduleSuiteBox.ItemsSource = p?.Suites;
        ScheduleEnvironmentBox.ItemsSource = p?.Environments;
        EnvironmentDataSetBox.ItemsSource = p?.DataSets;
        QueueEnvironmentBox.ItemsSource = p?.Environments;
        if (QueueEnvironmentBox.SelectedItem is null && (p?.Environments.Count ?? 0) > 0) QueueEnvironmentBox.SelectedItem = p!.Environments.FirstOrDefault(e => e.IsDefault) ?? p.Environments.FirstOrDefault();
        QueueSuiteText.Text = CurrentSuite?.DisplayName ?? "—";
        _loading = false;
        _ = RefreshGovernanceAsync();
        RefreshEnvironmentEditor();
        RefreshScheduleEditor();
        RefreshCaseEditor();
        RefreshDataAssertionsState();
        RefreshComponentsRecoveryState();
        RefreshDeliveryState();
    }

    private void RefreshCaseEditor()
    {
        var suite = CurrentSuite;
        CasesGrid.ItemsSource = suite?.TestCases;
        NewCaseButton.IsEnabled = suite is not null;
        DeleteSuiteButton.IsEnabled = suite is not null;
        RunSuiteButton.IsEnabled = suite is not null && CurrentProject is not null && suite.TestCases.Any(c => c.Status != AutomationCaseStatus.Archived);
        CasesContextText.Text = suite is null
            ? (IsSpanish ? "Selecciona una suite para administrar sus casos." : "Select a suite to manage its test cases.")
            : (IsSpanish ? $"Suite: {suite.Name}" : $"Suite: {suite.Name}");

        _loading = true;
        var tc = CurrentCase;
        CaseDescriptionBox.IsEnabled = tc is not null;
        CaseExpectedBox.IsEnabled = tc is not null;
        DeleteCaseButton.IsEnabled = tc is not null;
        RunCaseButton.IsEnabled = tc is not null && CurrentProject is not null && CurrentSuite is not null && tc.Steps.Count > 0;
        CaseDescriptionBox.Text = tc?.Description ?? "";
        CaseExpectedBox.Text = tc?.ExpectedOutcome ?? "";
        CaseStepsGrid.ItemsSource = tc?.Steps;
        if (tc is not null && AssertionCaseBox.SelectedItem is null) AssertionCaseBox.SelectedItem = tc;
        if (tc is not null && RecoveryCaseBox.SelectedItem is null) RecoveryCaseBox.SelectedItem = tc;
        _loading = false;
        RefreshComponentsRecoveryState();
    }


    private async Task RefreshGovernanceAsync()
    {
        var project = CurrentProject;
        if (project is null)
        {
            _governanceDocument = null;
            GovernanceCaseText.Text = "—";
            GovernanceStateText.Text = IsSpanish ? "Selecciona un proyecto y un caso de prueba." : "Select a project and a test case.";
            VersionsGrid.ItemsSource = null;
            AuditGrid.ItemsSource = null;
            RequireApprovalCheck.IsChecked = false;
            return;
        }
        _governanceDocument = await _governanceService.LoadAsync(project.Id);
        RequireApprovalCheck.IsChecked = _governanceDocument.Settings.RequireApprovalForAutomatedRuns;
        AuditGrid.ItemsSource = _governanceDocument.AuditTrail.OrderByDescending(x => x.Timestamp).ToList();
        var tc = CurrentCase;
        GovernanceCaseText.Text = tc?.DisplayName ?? "—";
        if (tc is null)
        {
            GovernanceStateText.Text = IsSpanish ? "Selecciona un caso de prueba para administrar su ciclo de aprobación." : "Select a test case to manage its approval lifecycle.";
            VersionsGrid.ItemsSource = null;
            return;
        }
        var rec = _governanceDocument.TestCases.FirstOrDefault(x => x.TestCaseId == tc.Id);
        GovernanceStateText.Text = rec is null
            ? (IsSpanish ? $"v{tc.Version} · Borrador · sin snapshot" : $"v{tc.Version} · Draft · no snapshot yet")
            : $"v{tc.Version} · {rec.ApprovalState}" + (rec.ApprovedVersion > 0 ? $" · approved v{rec.ApprovedVersion}" : "");
        VersionsGrid.ItemsSource = rec?.Versions.OrderByDescending(v => v.Version).ToList();
    }

    private async void RequireApprovalCheck_Click(object sender, RoutedEventArgs e)
    {
        if (_loading || CurrentProject is not AutomationProject project) return;
        await _governanceService.SetRequireApprovalAsync(project.Id, RequireApprovalCheck.IsChecked == true, _settings.QaName);
        await RefreshGovernanceAsync();
    }

    private async void CreateVersion_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentProject is not AutomationProject project || CurrentCase is not AutomationTestCase tc) return;
        var created = await _governanceService.CreateVersionAsync(project.Id, tc, _settings.QaName, VersionNoteBox.Text.Trim());
        if (created is null)
        {
            AppDialogWindow.ShowInfo(this, "QAFlow Recorder", IsSpanish ? "No hay cambios respecto a la última versión guardada." : "There are no changes compared with the latest saved version.", _settings.UiLanguage);
            return;
        }
        tc.UpdatedAt = DateTimeOffset.Now;
        await PersistAsync(IsSpanish ? $"Versión v{created.Version} creada." : $"Version v{created.Version} created.");
        VersionNoteBox.Text = "";
        await RefreshGovernanceAsync();
    }

    private async Task<bool> EnsureCurrentVersionSnapshotAsync()
    {
        if (CurrentProject is not AutomationProject project || CurrentCase is not AutomationTestCase tc) return false;
        var doc = await _governanceService.LoadAsync(project.Id);
        var rec = doc.TestCases.FirstOrDefault(x => x.TestCaseId == tc.Id);
        var current = rec?.Versions.FirstOrDefault(x => x.Version == tc.Version);
        if (current is not null)
        {
            var hash = AutomationGovernanceService.ComputeHash(JsonSerializer.Serialize(tc));
            if (current.Sha256.Equals(hash, StringComparison.OrdinalIgnoreCase)) return true;
        }
        var created = await _governanceService.CreateVersionAsync(project.Id, tc, _settings.QaName, VersionNoteBox.Text.Trim());
        if (created is not null) await PersistAsync();
        return true;
    }

    private async void SubmitReview_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentProject is not AutomationProject project || CurrentCase is not AutomationTestCase tc) return;
        await EnsureCurrentVersionSnapshotAsync();
        await _governanceService.SetApprovalAsync(project.Id, tc, AutomationApprovalState.InReview, _settings.QaName, VersionNoteBox.Text.Trim());
        await RefreshGovernanceAsync();
    }

    private async void Approve_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentProject is not AutomationProject project || CurrentCase is not AutomationTestCase tc) return;
        await EnsureCurrentVersionSnapshotAsync();
        await _governanceService.SetApprovalAsync(project.Id, tc, AutomationApprovalState.Approved, _settings.QaName, VersionNoteBox.Text.Trim());
        await RefreshGovernanceAsync();
    }

    private async void Reject_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentProject is not AutomationProject project || CurrentCase is not AutomationTestCase tc) return;
        await EnsureCurrentVersionSnapshotAsync();
        await _governanceService.SetApprovalAsync(project.Id, tc, AutomationApprovalState.Rejected, _settings.QaName, VersionNoteBox.Text.Trim());
        await RefreshGovernanceAsync();
    }

    private async void RestoreVersion_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentProject is not AutomationProject project || CurrentCase is not AutomationTestCase tc || VersionsGrid.SelectedItem is not AutomationTestCaseVersionRecord version) return;
        var restored = await _governanceService.BuildRestoredCopyAsync(version);
        if (restored is null) return;
        tc.Name = restored.Name; tc.Description = restored.Description; tc.Preconditions = restored.Preconditions; tc.ExpectedOutcome = restored.ExpectedOutcome;
        tc.Mode = restored.Mode; tc.Status = AutomationCaseStatus.NeedsReview; tc.BaseUrlSnapshot = restored.BaseUrlSnapshot;
        tc.AutomationReadiness = restored.AutomationReadiness; tc.DefaultDataSetId = restored.DefaultDataSetId; tc.ConversionSummary = restored.ConversionSummary;
        tc.Steps = CloneSteps(restored.Steps); tc.UpdatedAt = DateTimeOffset.Now;
        await _governanceService.LogAsync(project.Id, _settings.QaName, "VersionRestored", "TestCase", tc.Id, tc.DisplayName, $"Restored content from v{version.Version}; a new version will be created.");
        var created = await _governanceService.CreateVersionAsync(project.Id, tc, _settings.QaName, $"Restored from v{version.Version}. {VersionNoteBox.Text}".Trim());
        if (created is not null) await PersistAsync(IsSpanish ? $"Restaurado como nueva versión v{created.Version}." : $"Restored as new version v{created.Version}.");
        RefreshCaseEditor();
        await RefreshGovernanceAsync();
    }

    private async void RefreshAudit_Click(object sender, RoutedEventArgs e) => await RefreshGovernanceAsync();

    private async Task<bool> CheckGovernanceBeforeRunAsync(IEnumerable<AutomationTestCase> cases)
    {
        if (CurrentProject is not AutomationProject project) return false;
        var allowed = await _governanceService.IsExecutionAllowedAsync(project.Id, cases);
        if (!allowed)
        {
            AppDialogWindow.ShowInfo(this, "QAFlow Recorder",
                IsSpanish ? "Este proyecto requiere aprobación. Todos los casos Assisted/Automated deben tener su versión actual aprobada antes de ejecutar." : "This project requires approval. Every Assisted/Automated case must have its current version approved before execution.",
                _settings.UiLanguage);
        }
        return allowed;
    }

    private async void NewProject_Click(object sender, RoutedEventArgs e)
    {
        var p = new AutomationProject
        {
            Name = IsSpanish ? $"Proyecto {_projects.Count + 1}" : $"Project {_projects.Count + 1}",
            BaseUrl = _settings.WebAutomationBaseUrl,
            AllowedDomains = _settings.WebAutomationAllowedDomains,
            DefaultBrowser = _settings.WebAutomationDefaultBrowser
        };
        p.Environments.Add(new AutomationEnvironmentProfile { Name = IsSpanish ? "Predeterminado" : "Default", BaseUrl = p.BaseUrl, AllowedDomains = p.AllowedDomains, Browser = p.DefaultBrowser, IsDefault = true });
        _projects.Add(p);
        ProjectsList.SelectedItem = p;
        await PersistAsync(IsSpanish ? "Proyecto creado." : "Project created.");
        await _governanceService.LogAsync(p.Id, _settings.QaName, "ProjectCreated", "Project", p.Id, p.Name);
    }

    private async void DeleteProject_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentProject is not AutomationProject p) return;
        var ok = AppDialogWindow.ShowQuestion(this, "QAFlow Recorder",
            IsSpanish ? $"¿Eliminar el proyecto '{p.Name}' y sus suites/casos?" : $"Delete project '{p.Name}' and its suites/test cases?",
            _settings.UiLanguage);
        if (!ok) return;
        foreach (var schedule in p.Schedules.ToList()) { try { await _scheduler.RemoveAsync(schedule); } catch { } }
        foreach (var variable in p.DataSets.SelectMany(d => d.Variables).Where(v => v.IsSecret)) CredentialManagerService.DeleteSecret(variable.SecretKey);
        if (!string.IsNullOrWhiteSpace(p.EmailDelivery?.PasswordSecretKey)) CredentialManagerService.DeleteSecret(p.EmailDelivery.PasswordSecretKey);
        _projects.Remove(p);
        await PersistAsync(IsSpanish ? "Proyecto eliminado." : "Project deleted.");
        RefreshEditorState();
    }

    private async void NewSuite_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentProject is not AutomationProject p) return;
        var suite = new AutomationSuite { Name = IsSpanish ? $"Suite {p.Suites.Count + 1}" : $"Suite {p.Suites.Count + 1}", Order = p.Suites.Count + 1 };
        p.Suites.Add(suite);
        await _governanceService.LogAsync(p.Id, _settings.QaName, "SuiteCreated", "Suite", suite.Id, suite.Name);
        SuitesGrid.Items.Refresh();
        SuitesGrid.SelectedItem = suite;
        await PersistAsync(IsSpanish ? "Suite creada." : "Suite created.");
    }

    private async void DeleteSuite_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentProject is not AutomationProject p || CurrentSuite is not AutomationSuite suite) return;
        if (p.Schedules.Any(s => s.SuiteId == suite.Id))
        {
            AppDialogWindow.ShowInfo(this, "QAFlow Recorder", IsSpanish ? "La suite está vinculada a una programación. Elimina primero la programación." : "This suite is linked to a schedule. Delete the schedule first.", _settings.UiLanguage);
            return;
        }
        var ok = AppDialogWindow.ShowQuestion(this, "QAFlow Recorder",
            IsSpanish ? $"¿Eliminar la suite '{suite.Name}' y sus casos?" : $"Delete suite '{suite.Name}' and its test cases?",
            _settings.UiLanguage);
        if (!ok) return;
        p.Suites.Remove(suite);
        await PersistAsync(IsSpanish ? "Suite eliminada." : "Suite deleted.");
        RefreshEditorState();
    }

    private async void NewCase_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentSuite is not AutomationSuite suite) return;
        var next = suite.TestCases.Count + 1;
        var tc = new AutomationTestCase
        {
            Key = $"TC-{next:000}",
            Name = IsSpanish ? $"Caso de prueba {next}" : $"Test case {next}",
            Mode = AutomationCaseMode.Manual,
            Status = AutomationCaseStatus.Draft
        };
        suite.TestCases.Add(tc);
        if (CurrentProject is AutomationProject projectForAudit) await _governanceService.LogAsync(projectForAudit.Id, _settings.QaName, "TestCaseCreated", "TestCase", tc.Id, tc.DisplayName);
        CasesGrid.Items.Refresh();
        CasesGrid.SelectedItem = tc;
        await PersistAsync(IsSpanish ? "Caso de prueba creado." : "Test case created.");
    }

    private async void DeleteCase_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentSuite is not AutomationSuite suite || CurrentCase is not AutomationTestCase tc) return;
        var ok = AppDialogWindow.ShowQuestion(this, "QAFlow Recorder",
            IsSpanish ? $"¿Eliminar '{tc.DisplayName}'?" : $"Delete '{tc.DisplayName}'?",
            _settings.UiLanguage);
        if (!ok) return;
        suite.TestCases.Remove(tc);
        await PersistAsync(IsSpanish ? "Caso eliminado." : "Test case deleted.");
        RefreshCaseEditor();
    }

    private async void ProjectsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshEditorState();
        await RefreshQualityAnalyticsAsync();
    }

    private void SuitesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        RefreshCaseEditor();
        RunSuiteButton.IsEnabled = CurrentSuite is AutomationSuite s && s.TestCases.Any(c => c.Status != AutomationCaseStatus.Archived);
        QueueSuiteText.Text = CurrentSuite?.DisplayName ?? "—";
    }

    private void CasesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        RefreshCaseEditor();
    }

    private async void ProjectEditor_Changed(object sender, EventArgs e)
    {
        if (_loading || CurrentProject is not AutomationProject p) return;
        p.Name = string.IsNullOrWhiteSpace(ProjectNameBox.Text) ? p.Name : ProjectNameBox.Text.Trim();
        p.BaseUrl = BaseUrlBox.Text.Trim();
        p.DefaultBrowser = BrowserBox.SelectedIndex == 1 ? "Edge" : "Chrome";
        await PersistAsync();
    }

    private async void CaseEditor_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loading || CurrentCase is not AutomationTestCase tc) return;
        tc.Description = CaseDescriptionBox.Text.Trim();
        tc.ExpectedOutcome = CaseExpectedBox.Text.Trim();
        tc.UpdatedAt = DateTimeOffset.Now;
        await PersistAsync();
    }

    private async void SuitesGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        // Let the DataGrid commit the edited value before persisting the workspace.
        await Dispatcher.InvokeAsync(static () => { }, System.Windows.Threading.DispatcherPriority.Background);
        await PersistAsync();
    }

    private async void CasesGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        // Let the DataGrid commit the edited value before persisting the workspace.
        await Dispatcher.InvokeAsync(static () => { }, System.Windows.Threading.DispatcherPriority.Background);
        await PersistAsync();
    }

    private void RefreshDataAssertionsState()
    {
        var project = CurrentProject;
        DataSetsGrid.ItemsSource = project?.DataSets;
        DeleteDataSetButton.IsEnabled = CurrentDataSet is not null;
        VariablesGrid.ItemsSource = CurrentDataSet?.Variables;
        SaveVariableButton.IsEnabled = CurrentDataSet is not null;
        DeleteVariableButton.IsEnabled = VariablesGrid.SelectedItem is AutomationDataVariable;

        var cases = project?.Suites.SelectMany(s => s.TestCases).ToList() ?? [];
        var selectedCaseId = AssertionCase?.Id;
        AssertionCaseBox.ItemsSource = cases;
        if (selectedCaseId.HasValue) AssertionCaseBox.SelectedItem = cases.FirstOrDefault(c => c.Id == selectedCaseId.Value);
        if (AssertionCaseBox.SelectedItem is null && cases.Count > 0) AssertionCaseBox.SelectedIndex = 0;
        RefreshAssertionStepState();
    }

    private void RefreshAssertionStepState()
    {
        _loading = true;
        var tc = AssertionCase;
        AssertionStepBox.ItemsSource = tc?.Steps;
        DefaultDataSetBox.ItemsSource = CurrentProject?.DataSets;
        if (tc?.DefaultDataSetId is Guid dataId)
            DefaultDataSetBox.SelectedItem = CurrentProject?.DataSets.FirstOrDefault(d => d.Id == dataId);
        else
            DefaultDataSetBox.SelectedItem = null;
        if (AssertionStepBox.SelectedItem is null && (tc?.Steps.Count ?? 0) > 0) AssertionStepBox.SelectedIndex = 0;
        _loading = false;
        RefreshAssertionEditor();
    }

    private void RefreshAssertionEditor()
    {
        _loading = true;
        var step = AssertionStep;
        ValueExpressionBox.IsEnabled = step is not null;
        ValueExpressionBox.Text = step?.ValueExpression ?? "";
        AssertionsGrid.ItemsSource = step?.Assertions;
        AddAssertionButton.IsEnabled = step is not null;
        DeleteAssertionButton.IsEnabled = AssertionsGrid.SelectedItem is AutomationAssertion;
        _loading = false;
    }

    private async void NewDataSet_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentProject is not AutomationProject project) return;
        var set = new AutomationDataSet { Name = IsSpanish ? $"Datos {project.DataSets.Count + 1}" : $"Data set {project.DataSets.Count + 1}" };
        project.DataSets.Add(set);
        DataSetsGrid.Items.Refresh();
        DataSetsGrid.SelectedItem = set;
        await PersistAsync(IsSpanish ? "Conjunto de datos creado." : "Data set created.");
        RefreshDataAssertionsState();
    }

    private async void DeleteDataSet_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentProject is not AutomationProject project || CurrentDataSet is not AutomationDataSet set) return;
        if (!AppDialogWindow.ShowQuestion(this, "QAFlow Recorder", IsSpanish ? $"¿Eliminar '{set.Name}' y sus variables?" : $"Delete '{set.Name}' and its variables?", _settings.UiLanguage)) return;
        foreach (var variable in set.Variables.Where(v => v.IsSecret)) CredentialManagerService.DeleteSecret(variable.SecretKey);
        project.DataSets.Remove(set);
        foreach (var tc in project.Suites.SelectMany(s => s.TestCases).Where(c => c.DefaultDataSetId == set.Id)) tc.DefaultDataSetId = null;
        await PersistAsync(IsSpanish ? "Conjunto de datos eliminado." : "Data set deleted.");
        RefreshDataAssertionsState();
    }

    private void DataSetsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        _selectedVariableId = null;
        VariablesGrid.ItemsSource = CurrentDataSet?.Variables;
        VariableNameBox.Text = VariableValueBox.Text = VariableSecretBox.Password = "";
        VariableSecretCheck.IsChecked = false;
        DeleteDataSetButton.IsEnabled = CurrentDataSet is not null;
        SaveVariableButton.IsEnabled = CurrentDataSet is not null;
    }

    private async void DataSetsGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        await Dispatcher.InvokeAsync(static () => { }, System.Windows.Threading.DispatcherPriority.Background);
        await PersistAsync();
        RefreshDataAssertionsState();
    }

    private void VariableSecretCheck_Changed(object sender, RoutedEventArgs e)
    {
        var secret = VariableSecretCheck.IsChecked == true;
        VariableSecretBox.Visibility = secret ? Visibility.Visible : Visibility.Collapsed;
        VariableValueBox.Visibility = secret ? Visibility.Collapsed : Visibility.Visible;
    }

    private void VariablesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (VariablesGrid.SelectedItem is not AutomationDataVariable variable)
        {
            _selectedVariableId = null;
            DeleteVariableButton.IsEnabled = false;
            return;
        }
        _selectedVariableId = variable.Id;
        VariableNameBox.Text = variable.Name;
        VariableSecretCheck.IsChecked = variable.IsSecret;
        VariableValueBox.Text = variable.IsSecret ? "" : variable.Value;
        VariableSecretBox.Password = "";
        DeleteVariableButton.IsEnabled = true;
    }

    private async void SaveVariable_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentProject is not AutomationProject project || CurrentDataSet is not AutomationDataSet set) return;
        var name = VariableNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            AppDialogWindow.ShowInfo(this, "QAFlow Recorder", IsSpanish ? "Escribe un nombre de variable." : "Enter a variable name.", _settings.UiLanguage);
            return;
        }
        var variable = set.Variables.FirstOrDefault(v => v.Id == _selectedVariableId) ?? set.Variables.FirstOrDefault(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (variable is null) { variable = new AutomationDataVariable(); set.Variables.Add(variable); }
        var wasSecret = variable.IsSecret;
        var oldSecretKey = variable.SecretKey;
        variable.Name = name;
        variable.IsSecret = VariableSecretCheck.IsChecked == true;
        if (variable.IsSecret)
        {
            variable.Value = "";
            variable.SecretKey = string.IsNullOrWhiteSpace(oldSecretKey) ? CredentialManagerService.BuildTarget(project.Id, set.Id, variable.Id) : oldSecretKey;
            if (!string.IsNullOrWhiteSpace(VariableSecretBox.Password)) CredentialManagerService.WriteSecret(variable.SecretKey, VariableSecretBox.Password);
            else if (!wasSecret || string.IsNullOrWhiteSpace(CredentialManagerService.ReadSecret(variable.SecretKey)))
            {
                AppDialogWindow.ShowInfo(this, "QAFlow Recorder", IsSpanish ? "Escribe el valor secreto antes de guardar." : "Enter the secret value before saving.", _settings.UiLanguage);
                return;
            }
        }
        else
        {
            if (wasSecret) CredentialManagerService.DeleteSecret(oldSecretKey);
            variable.SecretKey = "";
            variable.Value = VariableValueBox.Text;
        }
        _selectedVariableId = variable.Id;
        await PersistAsync(IsSpanish ? "Variable guardada." : "Variable saved.");
        VariablesGrid.Items.Refresh();
        VariableSecretBox.Password = "";
    }

    private async void DeleteVariable_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentDataSet is not AutomationDataSet set || VariablesGrid.SelectedItem is not AutomationDataVariable variable) return;
        if (variable.IsSecret) CredentialManagerService.DeleteSecret(variable.SecretKey);
        set.Variables.Remove(variable);
        _selectedVariableId = null;
        await PersistAsync(IsSpanish ? "Variable eliminada." : "Variable deleted.");
        VariablesGrid.Items.Refresh();
    }

    private void AssertionCaseBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        RefreshAssertionStepState();
    }

    private void AssertionStepBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        RefreshAssertionEditor();
    }

    private async void DefaultDataSetBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || AssertionCase is not AutomationTestCase tc) return;
        tc.DefaultDataSetId = (DefaultDataSetBox.SelectedItem as AutomationDataSet)?.Id;
        await PersistAsync();
    }

    private async void ValueExpressionBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || AssertionStep is not AutomationTestStep step) return;
        step.ValueExpression = ValueExpressionBox.Text.Trim();
        await PersistAsync();
    }

    private async void AddAssertion_Click(object sender, RoutedEventArgs e)
    {
        if (AssertionStep is not AutomationTestStep step || AssertionTypeBox.SelectedItem is not AutomationAssertionType type) return;
        var assertion = new AutomationAssertion { Type = type, Expected = AssertionExpectedBox.Text.Trim(), UseStepTarget = true };
        if ((type is AutomationAssertionType.UrlContains or AutomationAssertionType.TitleContains or AutomationAssertionType.TextContains) && string.IsNullOrWhiteSpace(assertion.Expected))
        {
            AppDialogWindow.ShowInfo(this, "QAFlow Recorder", IsSpanish ? "Esta validación requiere un valor esperado." : "This assertion requires an expected value.", _settings.UiLanguage);
            return;
        }
        step.Assertions.Add(assertion);
        await PersistAsync(IsSpanish ? "Validación agregada." : "Assertion added.");
        AssertionsGrid.Items.Refresh();
        AssertionExpectedBox.Text = "";
    }

    private async void DeleteAssertion_Click(object sender, RoutedEventArgs e)
    {
        if (AssertionStep is not AutomationTestStep step || AssertionsGrid.SelectedItem is not AutomationAssertion assertion) return;
        step.Assertions.Remove(assertion);
        await PersistAsync(IsSpanish ? "Validación eliminada." : "Assertion deleted.");
        AssertionsGrid.Items.Refresh();
    }

    private void RefreshEnvironmentEditor()
    {
        _loading = true;
        var env = CurrentEnvironment;
        EnvironmentNameBox.Text = env?.Name ?? "";
        EnvironmentBaseUrlBox.Text = env?.BaseUrl ?? "";
        EnvironmentDomainsBox.Text = env?.AllowedDomains ?? "";
        EnvironmentBrowserBox.SelectedIndex = string.Equals(env?.Browser, "Edge", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        EnvironmentDataSetBox.ItemsSource = CurrentProject?.DataSets;
        EnvironmentDataSetBox.SelectedItem = env?.DefaultDataSetId is Guid id ? CurrentProject?.DataSets.FirstOrDefault(d => d.Id == id) : null;
        EnvironmentDefaultCheck.IsChecked = env?.IsDefault == true;
        SaveEnvironmentButton.IsEnabled = env is not null;
        DeleteEnvironmentButton.IsEnabled = env is not null && (CurrentProject?.Environments.Count ?? 0) > 1;
        _loading = false;
    }

    private void RefreshScheduleEditor()
    {
        _loading = true;
        ScheduleSuiteBox.ItemsSource = CurrentProject?.Suites;
        ScheduleEnvironmentBox.ItemsSource = CurrentProject?.Environments;
        var item = CurrentSchedule;
        ScheduleNameBox.Text = item?.Name ?? "";
        ScheduleSuiteBox.SelectedItem = item is null ? null : CurrentProject?.Suites.FirstOrDefault(s => s.Id == item.SuiteId);
        ScheduleEnvironmentBox.SelectedItem = item?.EnvironmentId is Guid envId ? CurrentProject?.Environments.FirstOrDefault(e => e.Id == envId) : CurrentProject?.Environments.FirstOrDefault(e => e.IsDefault);
        ScheduleFrequencyBox.SelectedItem = item?.Frequency ?? AutomationScheduleFrequency.Daily;
        ScheduleTimeBox.Text = item?.TimeOfDay ?? "22:00";
        ScheduleDaysBox.Text = item?.DaysOfWeek ?? "MON";
        ScheduleEnabledCheck.IsChecked = item?.Enabled ?? true;
        ScheduleSendCheck.IsChecked = item?.SendReport ?? true;
        SaveScheduleButton.IsEnabled = CurrentProject is not null;
        RegisterScheduleButton.IsEnabled = item is not null;
        DeleteScheduleButton.IsEnabled = item is not null;
        _loading = false;
    }

    private void EnvironmentsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        RefreshEnvironmentEditor();
        RefreshScheduleEditor();
    }

    private async void NewEnvironment_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentProject is not AutomationProject project) return;
        var env = new AutomationEnvironmentProfile { Name = IsSpanish ? $"Ambiente {project.Environments.Count + 1}" : $"Environment {project.Environments.Count + 1}", BaseUrl = project.BaseUrl, AllowedDomains = project.AllowedDomains, Browser = project.DefaultBrowser, IsDefault = project.Environments.Count == 0 };
        project.Environments.Add(env);
        EnvironmentsGrid.Items.Refresh(); EnvironmentsGrid.SelectedItem = env;
        await PersistAsync(IsSpanish ? "Ambiente creado." : "Environment created.");
    }

    private async void SaveEnvironment_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentProject is not AutomationProject project || CurrentEnvironment is not AutomationEnvironmentProfile env) return;
        var url = EnvironmentBaseUrlBox.Text.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            AppDialogWindow.ShowInfo(this, "QAFlow Recorder", IsSpanish ? "El ambiente requiere una URL base http/https válida." : "The environment requires a valid http/https Base URL.", _settings.UiLanguage); return;
        }
        env.Name = string.IsNullOrWhiteSpace(EnvironmentNameBox.Text) ? env.Name : EnvironmentNameBox.Text.Trim();
        env.BaseUrl = url; env.AllowedDomains = EnvironmentDomainsBox.Text.Trim(); env.Browser = EnvironmentBrowserBox.SelectedIndex == 1 ? "Edge" : "Chrome";
        env.DefaultDataSetId = (EnvironmentDataSetBox.SelectedItem as AutomationDataSet)?.Id;
        if (EnvironmentDefaultCheck.IsChecked == true)
        {
            foreach (var other in project.Environments) other.IsDefault = other.Id == env.Id;
            project.BaseUrl = env.BaseUrl; project.AllowedDomains = env.AllowedDomains; project.DefaultBrowser = env.Browser;
        }
        else if (!project.Environments.Any(x => x.IsDefault)) env.IsDefault = true;
        await PersistAsync(IsSpanish ? "Ambiente guardado." : "Environment saved.");
        EnvironmentsGrid.Items.Refresh(); RefreshScheduleEditor();
    }

    private async void DeleteEnvironment_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentProject is not AutomationProject project || CurrentEnvironment is not AutomationEnvironmentProfile env || project.Environments.Count <= 1) return;
        if (project.Schedules.Any(s => s.EnvironmentId == env.Id))
        {
            AppDialogWindow.ShowInfo(this, "QAFlow Recorder", IsSpanish ? "Este ambiente está siendo usado por una programación. Cambia la programación antes de eliminarlo." : "This environment is used by a schedule. Change the schedule before deleting it.", _settings.UiLanguage); return;
        }
        project.Environments.Remove(env);
        if (!project.Environments.Any(e => e.IsDefault)) project.Environments[0].IsDefault = true;
        await PersistAsync(IsSpanish ? "Ambiente eliminado." : "Environment deleted."); RefreshEditorState();
    }

    private void SchedulesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return; RefreshScheduleEditor();
    }

    private async void SaveSchedule_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentProject is not AutomationProject project) return;
        var suite = ScheduleSuiteBox.SelectedItem as AutomationSuite;
        if (suite is null) { AppDialogWindow.ShowInfo(this, "QAFlow Recorder", IsSpanish ? "Selecciona una suite." : "Select a suite.", _settings.UiLanguage); return; }
        if (!TimeSpan.TryParse(ScheduleTimeBox.Text.Trim(), out _)) { AppDialogWindow.ShowInfo(this, "QAFlow Recorder", IsSpanish ? "La hora debe usar formato HH:mm." : "Time must use HH:mm format.", _settings.UiLanguage); return; }
        var schedule = CurrentSchedule;
        if (schedule is null)
        {
            schedule = new AutomationSchedule(); project.Schedules.Add(schedule); SchedulesGrid.Items.Refresh(); SchedulesGrid.SelectedItem = schedule;
        }
        schedule.Name = string.IsNullOrWhiteSpace(ScheduleNameBox.Text) ? suite.Name : ScheduleNameBox.Text.Trim();
        schedule.SuiteId = suite.Id; schedule.EnvironmentId = (ScheduleEnvironmentBox.SelectedItem as AutomationEnvironmentProfile)?.Id;
        schedule.Frequency = ScheduleFrequencyBox.SelectedItem is AutomationScheduleFrequency f ? f : AutomationScheduleFrequency.Daily;
        schedule.TimeOfDay = ScheduleTimeBox.Text.Trim(); schedule.DaysOfWeek = ScheduleDaysBox.Text.Trim();
        schedule.Enabled = ScheduleEnabledCheck.IsChecked == true; schedule.SendReport = ScheduleSendCheck.IsChecked == true;
        await PersistAsync(IsSpanish ? "Programación guardada. Usa 'Registrar en Windows' para activarla." : "Schedule saved. Use 'Register in Windows' to activate it.");
        SchedulesGrid.Items.Refresh(); RegisterScheduleButton.IsEnabled = true;
    }

    private async void DeleteSchedule_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentProject is not AutomationProject project || CurrentSchedule is not AutomationSchedule schedule) return;
        try { await _scheduler.RemoveAsync(schedule); } catch { }
        project.Schedules.Remove(schedule);
        await PersistAsync(IsSpanish ? "Programación eliminada." : "Schedule deleted.");
        SchedulesGrid.Items.Refresh(); RefreshScheduleEditor();
    }

    private async void RegisterSchedule_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentProject is not AutomationProject project || CurrentSchedule is not AutomationSchedule schedule) return;
        try
        {
            await _scheduler.RegisterAsync(project, schedule);
            await PersistAsync(IsSpanish ? "Programación registrada en el Programador de tareas de Windows." : "Schedule registered in Windows Task Scheduler.");
            SchedulesGrid.Items.Refresh();
        }
        catch (Exception ex)
        {
            AppDialogWindow.ShowInfo(this, IsSpanish ? "Error de programación" : "Scheduling error", ex.Message, _settings.UiLanguage);
        }
    }

    private void RecordedSessionsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        RefreshConversionPreview();
    }

    private void ConversionSuiteBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        RefreshConversionPreview();
    }

    private void RefreshConversionPreview()
    {
        _conversionPreview = null;
        ConversionPreviewGrid.ItemsSource = null;
        PreviewTotalText.Text = PreviewWebText.Text = PreviewManualText.Text = "0";
        PreviewConfidenceText.Text = "0%";
        ConvertSessionButton.IsEnabled = false;

        if (CurrentProject is not AutomationProject project || RecordedSessionsGrid.SelectedItem is not TestSession session)
            return;

        if (!Uri.TryCreate(project.BaseUrl, UriKind.Absolute, out var projectUri) ||
            (projectUri.Scheme != Uri.UriSchemeHttp && projectUri.Scheme != Uri.UriSchemeHttps))
        {
            ConversionHintText.Text = IsSpanish
                ? "Configura una URL base http/https válida en el proyecto antes de convertir sesiones."
                : "Configure a valid http/https Base URL in the project before converting sessions.";
            return;
        }

        var service = new WebAutomationConversionService(_settings);
        _conversionPreview = service.Analyze(session, project);
        ConversionPreviewGrid.ItemsSource = _conversionPreview.Items;
        PreviewTotalText.Text = _conversionPreview.TotalActions.ToString();
        PreviewWebText.Text = _conversionPreview.InScopeSteps.ToString();
        PreviewManualText.Text = _conversionPreview.ManualCheckpoints.ToString();
        PreviewConfidenceText.Text = $"{_conversionPreview.AverageConfidence}%";
        ConversionNameBox.Text = session.DisplayName;
        ConvertSessionButton.IsEnabled = ConversionSuiteBox.SelectedItem is AutomationSuite && _conversionPreview.TotalActions > 0;

        ConversionHintText.Text = _conversionPreview.InScopeSteps > 0
            ? (IsSpanish
                ? $"{_conversionPreview.InScopeSteps} paso(s) web dentro del alcance. Los pasos externos quedan como checkpoints manuales/evidencia."
                : $"{_conversionPreview.InScopeSteps} web step(s) are inside scope. External activity remains manual/evidence checkpoints.")
            : (IsSpanish
                ? "No se detectaron pasos web dentro de la URL del proyecto. La sesión puede conservarse como referencia manual."
                : "No web steps were detected inside the project URL. The session can remain a manual reference.");
    }

    private async void ConvertSession_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentProject is not AutomationProject project ||
            ConversionSuiteBox.SelectedItem is not AutomationSuite suite ||
            RecordedSessionsGrid.SelectedItem is not TestSession session)
            return;

        var service = new WebAutomationConversionService(_settings);
        var next = suite.TestCases.Count + 1;
        var key = $"TC-{next:000}";
        var tc = service.Convert(session, project, ConversionNameBox.Text, key);
        suite.TestCases.Add(tc);
        project.UpdatedAt = DateTimeOffset.Now;
        await PersistAsync(IsSpanish
            ? $"Sesión convertida a {tc.DisplayName}. Revisa sus pasos antes de automatizar."
            : $"Session converted to {tc.DisplayName}. Review its steps before automation.");

        SuitesGrid.SelectedItem = suite;
        CasesGrid.ItemsSource = suite.TestCases;
        CasesGrid.Items.Refresh();
        CasesGrid.SelectedItem = tc;
        WorkspaceTabs.SelectedItem = CasesTab;
        RefreshCaseEditor();
    }

    private void RefreshInstrumentationStatus()
    {
        BridgeStatusText.Text = WebInstrumentationBridge.IsRunning
            ? (IsSpanish ? "Activo · localhost" : "Active · localhost")
            : (IsSpanish ? "No disponible" : "Unavailable");
        BridgeStatusText.Foreground = WebInstrumentationBridge.IsRunning
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(61,220,151))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255,101,119));

        var connected = DateTimeOffset.Now - WebInstrumentationBridge.LastClientSeen < TimeSpan.FromSeconds(15);
        ExtensionStatusText.Text = connected
            ? (IsSpanish ? "Conectada" : "Connected")
            : (IsSpanish ? "Esperando conexión" : "Waiting for connection");
        ExtensionStatusText.Foreground = connected
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(61,220,151))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(244,184,96));
    }

    private void RefreshInstrumentation_Click(object sender, RoutedEventArgs e) => RefreshInstrumentationStatus();

    private void OpenExtensionFolder_Click(object sender, RoutedEventArgs e)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "WebInstrumentationExtension");
        if (!Directory.Exists(path))
        {
            AppDialogWindow.ShowInfo(this, "QAFlow Recorder",
                IsSpanish ? "No se encontró la carpeta de instrumentación web en esta publicación." : "The web instrumentation folder was not found in this build.",
                _settings.UiLanguage);
            return;
        }
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }


    private void RefreshComponentsRecoveryState()
    {
        var project = CurrentProject;
        ComponentsGrid.ItemsSource = project?.Components;
        DeleteComponentButton.IsEnabled = CurrentComponent is not null;
        InsertComponentButton.IsEnabled = CurrentComponent is not null && CurrentCase is not null;
        CreateComponentButton.IsEnabled = CurrentCase is not null && CurrentCase.Steps.Count > 0;

        var cases = project?.Suites.SelectMany(s => s.TestCases).ToList() ?? [];
        var currentRecoveryId = RecoveryCase?.Id;
        RecoveryCaseBox.ItemsSource = cases;
        if (currentRecoveryId.HasValue) RecoveryCaseBox.SelectedItem = cases.FirstOrDefault(c => c.Id == currentRecoveryId.Value);
        if (RecoveryCaseBox.SelectedItem is null && CurrentCase is not null) RecoveryCaseBox.SelectedItem = CurrentCase;
        if (RecoveryCaseBox.SelectedItem is null && cases.Count > 0) RecoveryCaseBox.SelectedIndex = 0;
        RefreshRecoveryStepState();
    }

    private void RefreshRecoveryStepState()
    {
        _loading = true;
        var tc = RecoveryCase;
        var selectedStepId = RecoveryStep?.Id;
        RecoveryStepBox.ItemsSource = tc?.Steps;
        RecoveryStepsGrid.ItemsSource = tc?.Steps;
        if (selectedStepId.HasValue) RecoveryStepBox.SelectedItem = tc?.Steps.FirstOrDefault(s => s.Id == selectedStepId.Value);
        if (RecoveryStepBox.SelectedItem is null && (tc?.Steps.Count ?? 0) > 0) RecoveryStepBox.SelectedIndex = 0;
        var step = RecoveryStep;
        RecoveryRetriesBox.Text = (step?.MaxRetries ?? 1).ToString();
        RecoveryDelayBox.Text = (step?.RetryDelayMs ?? 600).ToString();
        FailureActionBox.SelectedItem = step?.OnFailure ?? AutomationFailureAction.StopTest;
        SaveRecoveryButton.IsEnabled = step is not null;
        _loading = false;
    }

    private void ComponentsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var component = CurrentComponent;
        ComponentNameBox.Text = component?.Name ?? "";
        ComponentDescriptionBox.Text = component?.Description ?? "";
        DeleteComponentButton.IsEnabled = component is not null;
        InsertComponentButton.IsEnabled = component is not null && CurrentCase is not null;
    }

    private async void CreateComponent_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentProject is not AutomationProject project || CurrentCase is not AutomationTestCase tc || tc.Steps.Count == 0) return;
        var name = string.IsNullOrWhiteSpace(ComponentNameBox.Text) ? tc.Name : ComponentNameBox.Text.Trim();
        var component = new ReusableAutomationComponent
        {
            Name = name,
            Description = string.IsNullOrWhiteSpace(ComponentDescriptionBox.Text) ? tc.Description : ComponentDescriptionBox.Text.Trim(),
            Steps = CloneSteps(tc.Steps)
        };
        project.Components.Add(component);
        ComponentsGrid.Items.Refresh();
        ComponentsGrid.SelectedItem = component;
        await PersistAsync(IsSpanish ? "Componente reutilizable creado." : "Reusable component created.");
    }

    private async void InsertComponent_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentCase is not AutomationTestCase tc || CurrentComponent is not ReusableAutomationComponent component) return;
        var clones = CloneSteps(component.Steps);
        var next = tc.Steps.Count + 1;
        foreach (var step in clones) step.Order = next++;
        tc.Steps.AddRange(clones);
        tc.UpdatedAt = DateTimeOffset.Now;
        CaseStepsGrid.Items.Refresh();
        await PersistAsync(IsSpanish ? "Componente insertado en el caso de prueba." : "Component inserted into the test case.");
        RefreshComponentsRecoveryState();
    }

    private async void DeleteComponent_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentProject is not AutomationProject project || CurrentComponent is not ReusableAutomationComponent component) return;
        if (!AppDialogWindow.ShowQuestion(this, "QAFlow Recorder", IsSpanish ? $"¿Eliminar el componente '{component.Name}'?" : $"Delete component '{component.Name}'?", _settings.UiLanguage)) return;
        project.Components.Remove(component);
        ComponentsGrid.Items.Refresh();
        ComponentNameBox.Text = ComponentDescriptionBox.Text = "";
        await PersistAsync(IsSpanish ? "Componente eliminado." : "Component deleted.");
        RefreshComponentsRecoveryState();
    }

    private void RecoveryCaseBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        RefreshRecoveryStepState();
    }

    private void RecoveryStepBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        RefreshRecoveryStepState();
    }

    private async void SaveRecovery_Click(object sender, RoutedEventArgs e)
    {
        if (RecoveryStep is not AutomationTestStep step) return;
        if (!int.TryParse(RecoveryRetriesBox.Text.Trim(), out var retries)) retries = 1;
        if (!int.TryParse(RecoveryDelayBox.Text.Trim(), out var delay)) delay = 600;
        step.MaxRetries = Math.Clamp(retries, 0, 5);
        step.RetryDelayMs = Math.Clamp(delay, 100, 10000);
        step.OnFailure = FailureActionBox.SelectedItem is AutomationFailureAction action ? action : AutomationFailureAction.StopTest;
        RecoveryStepsGrid.Items.Refresh();
        await PersistAsync(IsSpanish ? "Política de recuperación guardada." : "Recovery policy saved.");
    }

    private static List<AutomationTestStep> CloneSteps(IEnumerable<AutomationTestStep> steps)
    {
        var json = JsonSerializer.Serialize(steps);
        var result = JsonSerializer.Deserialize<List<AutomationTestStep>>(json) ?? [];
        foreach (var step in result)
        {
            step.Id = Guid.NewGuid();
            step.SourceEventId = null;
            foreach (var assertion in step.Assertions) assertion.Id = Guid.NewGuid();
        }
        return result;
    }

    private async void RunCase_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentProject is not AutomationProject project || CurrentSuite is not AutomationSuite suite || CurrentCase is not AutomationTestCase tc) return;
        if (!await CheckGovernanceBeforeRunAsync([tc])) return;
        if (!Uri.TryCreate(project.BaseUrl, UriKind.Absolute, out var u) || (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps))
        {
            AppDialogWindow.ShowInfo(this, "QAFlow Recorder", IsSpanish ? "Configura una URL base http/https válida antes de ejecutar automatización." : "Configure a valid http/https Base URL before running automation.", _settings.UiLanguage);
            return;
        }
        var win = new AutomationRunWindow(project, suite, tc, _settings) { Owner = this };
        win.ShowDialog();
        await PersistAsync(IsSpanish ? "Resultados y selectores aprendidos guardados." : "Run results and learned selectors saved.");
        await ReloadRunsAsync();
        RecoveryStepsGrid.Items.Refresh();
    }

    private async Task ReloadRunsAsync()
    {
        var runs = await _runStore.LoadAllAsync();
        _runs.Clear();
        foreach (var run in runs) _runs.Add(run);
        RunsGrid.Items.Refresh();
    }

    private void OpenRunFolder_Click(object sender, RoutedEventArgs e)
    {
        if (RunsGrid.SelectedItem is not AutomationTestRun run || string.IsNullOrWhiteSpace(run.RunFolder) || !Directory.Exists(run.RunFolder)) return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{run.RunFolder}\"") { UseShellExecute = true });
    }

    private void OpenRunReport_Click(object sender, RoutedEventArgs e)
    {
        if (RunsGrid.SelectedItem is not AutomationTestRun run || string.IsNullOrWhiteSpace(run.HtmlReportPath) || !File.Exists(run.HtmlReportPath)) return;
        Process.Start(new ProcessStartInfo(run.HtmlReportPath) { UseShellExecute = true });
    }

    private void RefreshDeliveryState()
    {
        _loading = true;
        var p = CurrentProject;
        var cfg = p?.EmailDelivery ?? new AutomationEmailDeliverySettings();
        DeliveryEnabledCheck.IsEnabled = p is not null;
        AutoSendCheck.IsEnabled = p is not null;
        DeliveryEnabledCheck.IsChecked = cfg.Enabled;
        AutoSendCheck.IsChecked = cfg.AutoSendAfterSuiteRun;
        SmtpHostBox.Text = cfg.SmtpHost;
        SmtpPortBox.Text = cfg.SmtpPort.ToString();
        SmtpSslCheck.IsChecked = cfg.UseSsl;
        SmtpUserBox.Text = cfg.Username;
        FromAddressBox.Text = cfg.FromAddress;
        RecipientsBox.Text = cfg.Recipients;
        AttachSuiteHtmlCheck.IsChecked = cfg.AttachSuiteHtml;
        AttachCaseHtmlCheck.IsChecked = cfg.AttachCaseHtml;
        SmtpPasswordBox.Password = "";
        PdfAttachmentsList.ItemsSource = cfg.PdfAttachments;
        SaveDeliveryButton.IsEnabled = p is not null;
        TestEmailButton.IsEnabled = p is not null;
        AddPdfButton.IsEnabled = p is not null;
        RemovePdfButton.IsEnabled = p is not null;
        _loading = false;
    }

    private void DeliveryEditor_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || CurrentProject is not AutomationProject project) return;
        var cfg = project.EmailDelivery ??= new AutomationEmailDeliverySettings();
        cfg.Enabled = DeliveryEnabledCheck.IsChecked == true;
        cfg.AutoSendAfterSuiteRun = AutoSendCheck.IsChecked == true;
        cfg.SmtpHost = SmtpHostBox.Text.Trim();
        cfg.UseSsl = SmtpSslCheck.IsChecked == true;
        cfg.Username = SmtpUserBox.Text.Trim();
        cfg.FromAddress = FromAddressBox.Text.Trim();
        cfg.Recipients = RecipientsBox.Text.Trim();
        cfg.AttachSuiteHtml = AttachSuiteHtmlCheck.IsChecked == true;
        cfg.AttachCaseHtml = AttachCaseHtmlCheck.IsChecked == true;
    }

    private void DeliveryText_Changed(object sender, TextChangedEventArgs e) => DeliveryEditor_Changed(sender, new RoutedEventArgs());

    private void SmtpPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || CurrentProject is null) return;
        DeliveryStatusText.Text = IsSpanish ? "Contraseña pendiente de guardar de forma segura." : "Password will be stored securely when settings are saved.";
    }

    private async Task<bool> SaveDeliverySettingsAsync()
    {
        if (CurrentProject is not AutomationProject project) return false;
        DeliveryEditor_Changed(this, new RoutedEventArgs());
        var cfg = project.EmailDelivery ??= new AutomationEmailDeliverySettings();
        if (!int.TryParse(SmtpPortBox.Text.Trim(), out var port) || port is < 1 or > 65535)
        {
            AppDialogWindow.ShowInfo(this, "QAFlow Recorder", IsSpanish ? "El puerto SMTP no es válido." : "SMTP port is invalid.", _settings.UiLanguage); return false;
        }
        cfg.SmtpPort = port;
        cfg.PasswordSecretKey = string.IsNullOrWhiteSpace(cfg.PasswordSecretKey) ? CredentialManagerService.BuildEmailTarget(project.Id) : cfg.PasswordSecretKey;
        if (!string.IsNullOrWhiteSpace(SmtpPasswordBox.Password)) CredentialManagerService.WriteSecret(cfg.PasswordSecretKey, SmtpPasswordBox.Password);
        SmtpPasswordBox.Password = "";
        await PersistAsync(IsSpanish ? "Configuración de envío guardada." : "Report delivery settings saved.");
        DeliveryStatusText.Text = IsSpanish ? "Configuración guardada. La contraseña permanece en Windows Credential Manager." : "Settings saved. Password remains in Windows Credential Manager.";
        return true;
    }

    private async void SaveDelivery_Click(object sender, RoutedEventArgs e) => await SaveDeliverySettingsAsync();

    private async void TestEmail_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentProject is not AutomationProject project || !await SaveDeliverySettingsAsync()) return;
        try
        {
            DeliveryStatusText.Text = IsSpanish ? "Enviando correo de prueba..." : "Sending test email...";
            await new AutomationReportDeliveryService().SendTestAsync(project);
            DeliveryStatusText.Text = IsSpanish ? "Correo de prueba enviado correctamente." : "Test email sent successfully.";
        }
        catch (Exception ex)
        {
            DeliveryStatusText.Text = IsSpanish ? "No se pudo enviar el correo de prueba." : "Test email could not be sent.";
            AppDialogWindow.ShowInfo(this, IsSpanish ? "Error de correo" : "Email error", ex.Message, _settings.UiLanguage);
        }
    }

    private async void AddPdf_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentProject is not AutomationProject project) return;
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "PDF reports|*.pdf", Multiselect = true, Title = IsSpanish ? "Seleccionar reportes PDF" : "Select PDF reports" };
        if (dlg.ShowDialog(this) != true) return;
        var cfg = project.EmailDelivery ??= new AutomationEmailDeliverySettings();
        foreach (var file in dlg.FileNames.Where(File.Exists)) if (!cfg.PdfAttachments.Contains(file, StringComparer.OrdinalIgnoreCase)) cfg.PdfAttachments.Add(file);
        PdfAttachmentsList.Items.Refresh();
        await PersistAsync(IsSpanish ? "PDFs agregados a la entrega." : "PDFs added to report delivery.");
    }

    private async void RemovePdf_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentProject is not AutomationProject project || PdfAttachmentsList.SelectedItem is not string path) return;
        project.EmailDelivery.PdfAttachments.Remove(path);
        PdfAttachmentsList.Items.Refresh();
        await PersistAsync(IsSpanish ? "PDF quitado de la entrega." : "PDF removed from report delivery.");
    }

    private async void RunSuite_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentProject is not AutomationProject project || CurrentSuite is not AutomationSuite suite) return;
        if (!await CheckGovernanceBeforeRunAsync(suite.TestCases)) return;
        if (!Uri.TryCreate(project.BaseUrl, UriKind.Absolute, out var u) || (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps))
        {
            AppDialogWindow.ShowInfo(this, "QAFlow Recorder", IsSpanish ? "Configura una URL base http/https válida antes de ejecutar la suite." : "Configure a valid http/https Base URL before running the suite.", _settings.UiLanguage); return;
        }
        var win = new AutomationSuiteRunWindow(project, suite, _settings) { Owner = this };
        win.ShowDialog();
        await PersistAsync(IsSpanish ? "Resultados de suite y selectores aprendidos guardados." : "Suite results and learned selectors saved.");
        await ReloadRunsAsync();
        await ReloadSuiteRunsAsync();
    }

    private async Task ReloadSuiteRunsAsync()
    {
        var runs = await _suiteRunStore.LoadAllAsync();
        _suiteRuns.Clear(); foreach (var run in runs) _suiteRuns.Add(run);
        SuiteRunsGrid.Items.Refresh();
    }

    private void OpenSuiteRunFolder_Click(object sender, RoutedEventArgs e)
    {
        if (SuiteRunsGrid.SelectedItem is not AutomationSuiteRun run || string.IsNullOrWhiteSpace(run.RunFolder) || !Directory.Exists(run.RunFolder)) return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{run.RunFolder}\"") { UseShellExecute = true });
    }

    private void OpenSuiteRunReport_Click(object sender, RoutedEventArgs e)
    {
        if (SuiteRunsGrid.SelectedItem is not AutomationSuiteRun run || string.IsNullOrWhiteSpace(run.HtmlReportPath) || !File.Exists(run.HtmlReportPath)) return;
        Process.Start(new ProcessStartInfo(run.HtmlReportPath) { UseShellExecute = true });
    }

    private async void SendSuiteRun_Click(object sender, RoutedEventArgs e)
    {
        if (SuiteRunsGrid.SelectedItem is not AutomationSuiteRun run) return;
        var project = _projects.FirstOrDefault(p => p.Id == run.ProjectId);
        if (project is null) return;
        try
        {
            StatusText.Text = IsSpanish ? "Enviando reporte de suite..." : "Sending suite report...";
            await new AutomationReportDeliveryService().SendSuiteRunAsync(project, run);
            StatusText.Text = IsSpanish ? "Reporte enviado correctamente." : "Suite report sent successfully.";
        }
        catch (Exception ex) { AppDialogWindow.ShowInfo(this, IsSpanish ? "Error de correo" : "Email error", ex.Message, _settings.UiLanguage); }
    }

    private async void QueueMatrix_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentProject is not AutomationProject project || CurrentSuite is not AutomationSuite suite)
        {
            AppDialogWindow.ShowInfo(this, "QAFlow Recorder", IsSpanish ? "Selecciona un proyecto y una suite." : "Select a project and suite.", _settings.UiLanguage); return;
        }
        var browsers = new List<string>();
        if (QueueChromeCheck.IsChecked == true) browsers.Add("Chrome");
        if (QueueEdgeCheck.IsChecked == true) browsers.Add("Edge");
        if (browsers.Count == 0)
        {
            AppDialogWindow.ShowInfo(this, "QAFlow Recorder", IsSpanish ? "Selecciona al menos un navegador." : "Select at least one browser.", _settings.UiLanguage); return;
        }
        var env = QueueEnvironmentBox.SelectedItem as AutomationEnvironmentProfile ?? project.Environments.FirstOrDefault(e => e.IsDefault) ?? project.Environments.FirstOrDefault();
        var effective = AutomationEnvironmentService.Apply(project, env);
        if (!Uri.TryCreate(effective.BaseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            AppDialogWindow.ShowInfo(this, "QAFlow Recorder", IsSpanish ? "El ambiente necesita una URL base http/https válida." : "The environment requires a valid http/https Base URL.", _settings.UiLanguage); return;
        }
        var batch = Guid.NewGuid();
        foreach (var browser in browsers)
            _queueItems.Insert(0, new AutomationQueueItem { BatchId = batch, ProjectId = project.Id, SuiteId = suite.Id, EnvironmentId = env?.Id, ProjectName = project.Name, SuiteName = suite.Name, EnvironmentName = env?.Name ?? "Default", Browser = browser, Status = AutomationQueueStatus.Queued, Message = IsSpanish ? "Esperando ejecución." : "Waiting for execution." });
        await _queueStore.SaveAsync(_queueItems);
        RunQueueGrid.Items.Refresh();
        QueueStateText.Text = IsSpanish ? $"Matriz {batch.ToString("N")[..8].ToUpperInvariant()} encolada · {browsers.Count} navegador(es)." : $"Matrix {batch.ToString("N")[..8].ToUpperInvariant()} queued · {browsers.Count} browser(s).";
    }

    private async void StartQueue_Click(object sender, RoutedEventArgs e)
    {
        if (_queueCts is not null) return;
        var pending = _queueItems.Count(x => x.Status == AutomationQueueStatus.Queued);
        if (pending == 0)
        {
            AppDialogWindow.ShowInfo(this, "QAFlow Recorder", IsSpanish ? "No hay trabajos pendientes en la cola." : "There are no pending queue jobs.", _settings.UiLanguage); return;
        }
        _queueSettings.MaxConcurrency = Math.Clamp(ConcurrencyBox.SelectedIndex + 1, 1, 4);
        _queueSettings.StopBatchOnFailure = StopBatchOnFailureCheck.IsChecked == true;
        await _queueStore.SaveSettingsAsync(_queueSettings);
        _queueCts = new CancellationTokenSource();
        StartQueueButton.IsEnabled = false; CancelQueueButton.IsEnabled = true; QueueMatrixButton.IsEnabled = false;
        QueueStateText.Text = IsSpanish ? $"Ejecutando {pending} trabajo(s) · concurrencia {_queueSettings.MaxConcurrency}." : $"Running {pending} job(s) · concurrency {_queueSettings.MaxConcurrency}.";
        var service = new AutomationRunQueueService();
        service.ItemChanged += item => Dispatcher.Invoke(() => { RunQueueGrid.Items.Refresh(); QueueStateText.Text = IsSpanish ? $"{item.Browser}: {item.Status}" : $"{item.Browser}: {item.Status}"; });
        try
        {
            await service.RunPendingAsync(_queueItems, _queueSettings, _queueCts.Token);
            QueueStateText.Text = IsSpanish ? "Cola finalizada." : "Queue finished.";
            await ReloadRunsAsync(); await ReloadSuiteRunsAsync();
        }
        catch (OperationCanceledException) { QueueStateText.Text = IsSpanish ? "Cola detenida." : "Queue stopped."; }
        finally
        {
            _queueCts.Dispose(); _queueCts = null;
            StartQueueButton.IsEnabled = true; CancelQueueButton.IsEnabled = false; QueueMatrixButton.IsEnabled = true;
            RunQueueGrid.Items.Refresh();
        }
    }

    private void CancelQueue_Click(object sender, RoutedEventArgs e) => _queueCts?.Cancel();

    private void OpenQueuedReport_Click(object sender, RoutedEventArgs e)
    {
        if (RunQueueGrid.SelectedItem is not AutomationQueueItem item) return;
        var path = File.Exists(item.MatrixReportPath) ? item.MatrixReportPath : item.HtmlReportPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private async Task RefreshQualityAnalyticsAsync()
    {
        try
        {
            QualityStateText.Text = IsSpanish ? "Analizando historial local..." : "Analyzing local history...";
            _qualitySnapshot = await _qualityAnalyticsService.AnalyzeAsync(CurrentProject);
            QualityPassText.Text = $"{_qualitySnapshot.OverallPassRate}%";
            QualityFlakyText.Text = _qualitySnapshot.FlakyTests.ToString();
            QualityFailuresText.Text = _qualitySnapshot.RecurringFailureClusters.ToString();
            QualityRunsText.Text = _qualitySnapshot.SuiteRunsAnalyzed.ToString();
            StabilityGrid.ItemsSource = _qualitySnapshot.TestStability;
            FailureClustersGrid.ItemsSource = _qualitySnapshot.FailureClusters;
            AnalyticsTrendGrid.ItemsSource = _qualitySnapshot.Trends;
            QualityStateText.Text = IsSpanish
                ? $"{_qualitySnapshot.TestRunsAnalyzed} ejecuciones de caso analizadas · {_qualitySnapshot.StableTests} casos estables · {_qualitySnapshot.FlakyTests} flaky."
                : $"{_qualitySnapshot.TestRunsAnalyzed} test runs analyzed · {_qualitySnapshot.StableTests} stable tests · {_qualitySnapshot.FlakyTests} flaky.";
            RerunFailedButton.IsEnabled = AnalyticsTrendGrid.SelectedItem is AutomationTrendPoint t && (t.FailedCases > 0 || t.ReviewCases > 0);
        }
        catch (Exception ex)
        {
            QualityStateText.Text = IsSpanish ? $"No se pudo analizar el historial: {ex.Message}" : $"History analysis failed: {ex.Message}";
        }
    }

    private async void RefreshQuality_Click(object sender, RoutedEventArgs e) => await RefreshQualityAnalyticsAsync();

    private async void ExportQuality_Click(object sender, RoutedEventArgs e)
    {
        if (_qualitySnapshot is null) await RefreshQualityAnalyticsAsync();
        if (_qualitySnapshot is null) return;
        try
        {
            var path = await AutomationQualityHtmlReportService.GenerateAsync(_qualitySnapshot, _settings.UiLanguage);
            QualityStateText.Text = IsSpanish ? $"Reporte generado: {path}" : $"Report generated: {path}";
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppDialogWindow.ShowInfo(this, IsSpanish ? "Error de reporte" : "Report error", ex.Message, _settings.UiLanguage);
        }
    }

    private void AnalyticsTrendGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RerunFailedButton.IsEnabled = AnalyticsTrendGrid.SelectedItem is AutomationTrendPoint t && (t.FailedCases > 0 || t.ReviewCases > 0);
    }

    private async void RerunFailed_Click(object sender, RoutedEventArgs e)
    {
        if (AnalyticsTrendGrid.SelectedItem is not AutomationTrendPoint trend) return;
        var originalRun = _suiteRuns.FirstOrDefault(r => r.Id == trend.SuiteRunId);
        if (originalRun is null)
        {
            var all = await _suiteRunStore.LoadAllAsync();
            originalRun = all.FirstOrDefault(r => r.Id == trend.SuiteRunId);
        }
        var project = _projects.FirstOrDefault(p => p.Id == trend.ProjectId);
        var originalSuite = project?.Suites.FirstOrDefault(s => s.Id == trend.SuiteId);
        if (originalRun is null || project is null || originalSuite is null) return;

        var failedIds = originalRun.Cases
            .Where(c => c.Status is AutomationRunStatus.Failed or AutomationRunStatus.NeedsReview or AutomationRunStatus.Blocked)
            .Select(c => c.TestCaseId).ToHashSet();
        var retryCases = originalSuite.TestCases.Where(c => failedIds.Contains(c.Id)).ToList();
        if (retryCases.Count == 0)
        {
            AppDialogWindow.ShowInfo(this, "QAFlow Recorder", IsSpanish ? "La ejecución seleccionada no contiene casos fallidos para reejecutar." : "The selected run has no failed cases to rerun.", _settings.UiLanguage);
            return;
        }

        var retrySuite = new AutomationSuite
        {
            Id = originalSuite.Id,
            Name = IsSpanish ? $"{originalSuite.Name} · Reintento de fallos" : $"{originalSuite.Name} · Failed retry",
            Description = originalSuite.Description,
            Order = originalSuite.Order,
            TestCases = retryCases
        };
        var win = new AutomationSuiteRunWindow(project, retrySuite, _settings) { Owner = this };
        win.ShowDialog();
        await ReloadRunsAsync();
        await ReloadSuiteRunsAsync();
        await RefreshQualityAnalyticsAsync();
    }

    private async void Save_Click(object sender, RoutedEventArgs e) => await PersistAsync(IsSpanish ? "Espacio de automatización guardado." : "Automation workspace saved.");
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
