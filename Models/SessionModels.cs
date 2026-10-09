using System.IO;
using System.Text.Json.Serialization;

namespace QAFlowRecorder.Models;

public enum SessionState { Idle, Recording, Paused, Finished }
public enum EventKind { Navigation, Click, Input, Select, Submit, ConsoleError, HttpError, ResourceError, Scroll, Dialog, Wait, Note }
public enum AnnotationResult { Ok, Error, Doubt }
public enum Severity { None, Low, Medium, High, Critical }
public enum ScreenshotKind { Navigation, FlowAction, Annotation, CriticalError, BeforeSubmit, Dialog, Periodic, Final, Manual, BugOriginal, BugMarked }
public enum PrivacyMaskMode { Automatic, Manual, Off }
public enum RiskLevel { Low, Medium, High, Critical }
public enum ChecklistStatus { NotTested, Passed, Failed, Blocked }


public sealed class SessionBookmark
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public string Category { get; set; } = "Important";
    public string Note { get; set; } = "";
    public string ApplicationName { get; set; } = "";
    public string WindowTitle { get; set; } = "";
    public string ScreenshotPath { get; set; } = "";
    [JsonIgnore] public string DisplayText => string.IsNullOrWhiteSpace(Note) ? Category : $"{Category} · {Note}";
}

public sealed class ManualChecklistItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Order { get; set; }
    public string Text { get; set; } = "";
    public ChecklistStatus Status { get; set; } = ChecklistStatus.NotTested;
    public string Note { get; set; } = "";
}

public sealed class EnvironmentSnapshot
{
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.Now;
    public string OperatingSystem { get; set; } = Environment.OSVersion.VersionString;
    public string Resolution { get; set; } = "";
    public string TimeZone { get; set; } = TimeZoneInfo.Local.DisplayName;
    public string MachineName { get; set; } = Environment.MachineName;
    public string ActiveApplication { get; set; } = "";
    public string ActiveWindow { get; set; } = "";
    public string BuildVersion { get; set; } = "";
}

public sealed class SessionTemplateDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public bool BuiltIn { get; set; }
    public string TestType { get; set; } = "Functional Normal";
    public string DefaultTags { get; set; } = "";
    public string CharterObjective { get; set; } = "";
    public string CharterScope { get; set; } = "";
    public string CharterRisks { get; set; } = "";
    public string ChecklistText { get; set; } = "";
    public bool PeriodicCapture { get; set; }
    public int PeriodicSeconds { get; set; } = 30;
    public PrivacyMaskMode PrivacyMasking { get; set; } = PrivacyMaskMode.Automatic;
    [JsonIgnore] public string DisplayName => BuiltIn ? $"{Name} · built-in" : Name;
    public override string ToString() => DisplayName;
}

public sealed class QaEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public EventKind Kind { get; set; }
    public string Url { get; set; } = "";
    public string Description { get; set; } = "";
    public string TargetTitle { get; set; } = "";
    public string Selector { get; set; } = "";
    public string Value { get; set; } = "";
    public string Method { get; set; } = "";
    public int? StatusCode { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public string Stack { get; set; } = "";
    public string ApplicationName { get; set; } = "";
    public string WindowTitle { get; set; } = "";
    public string ControlName { get; set; } = "";
    public string ControlType { get; set; } = "";
    public string AutomationId { get; set; } = "";

    // Phase 4 web instrumentation metadata. Empty in Recorder-only and desktop activity.
    public bool WebDomEnriched { get; set; }
    public string WebElementTag { get; set; } = "";
    public string WebElementText { get; set; } = "";
    public string WebElementId { get; set; } = "";
    public string WebElementName { get; set; } = "";
    public string WebElementRole { get; set; } = "";
    public string WebAriaLabel { get; set; } = "";
    public string WebDataTestId { get; set; } = "";
    public string WebCssSelector { get; set; } = "";
    public string WebXPath { get; set; } = "";
    public string WebPrimarySelector { get; set; } = "";
    public string WebPrimarySelectorType { get; set; } = "";
    public int WebSelectorConfidence { get; set; }
    public string WebPageTitle { get; set; } = "";
    public Severity Severity { get; set; } = Severity.None;
    public bool IsAnnotationPoint { get; set; }
    public bool IsExploration { get; set; }
    public bool IsCheckpoint { get; set; }
    public string CheckpointKind { get; set; } = "";
    public string CheckpointTitle { get; set; } = "";
    public bool IsImportant { get; set; }
    public bool ExcludeFromReport { get; set; }
    public string CustomDescription { get; set; } = "";
    public bool MaskSensitiveEvidence { get; set; }
    [JsonIgnore] public string TimeText => Timestamp.LocalDateTime.ToString("HH:mm:ss");
}

public sealed class Annotation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public string Url { get; set; } = "";
    public Guid? EventId { get; set; }
    public string Expected { get; set; } = "";
    public string Actual { get; set; } = "";
    public AnnotationResult Result { get; set; } = AnnotationResult.Ok;
    public Severity Severity { get; set; } = Severity.None;
    public string Note { get; set; } = "";
    public string ScreenshotPath { get; set; } = "";
}

public sealed class EvidenceShot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public ScreenshotKind Kind { get; set; }
    public string Url { get; set; } = "";
    public string TargetTitle { get; set; } = "";
    public string FilePath { get; set; } = "";
    public bool FullPage { get; set; }
    public Guid? EventId { get; set; }
    public bool SensitiveMaskApplied { get; set; }
    [JsonIgnore] public string TimeText => Timestamp.LocalDateTime.ToString("HH:mm:ss");
    [JsonIgnore] public string FileName => Path.GetFileName(FilePath);
}

public sealed class BugRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public string Title { get; set; } = "";
    public string Screen { get; set; } = "";
    public string Frequency { get; set; } = "First time";
    public AnnotationResult Result { get; set; } = AnnotationResult.Error;
    public Severity Severity { get; set; } = Severity.Medium;
    public string Expected { get; set; } = "";
    public string Actual { get; set; } = "";
    public string Note { get; set; } = "";
    public string Url { get; set; } = "";
    public string OriginalScreenshotPath { get; set; } = "";
    public string MarkedScreenshotPath { get; set; } = "";
    public List<string> Attachments { get; set; } = [];
    public Guid? PossibleDuplicateBugId { get; set; }
    public double DuplicateSimilarity { get; set; }
    public string PossibleDuplicateTitle { get; set; } = "";
}

public sealed class TestSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Tester { get; set; } = Environment.UserName;
    public string Template { get; set; } = "Regression";
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? FinishedAt { get; set; }
    public string InitialUrl { get; set; } = "";
    public string FinalUrl { get; set; } = "";
    public string UserAgent { get; set; } = "";
    public Guid SeriesId { get; set; } = Guid.Empty;
    public Guid? PreviousPartId { get; set; }
    public Guid? NextPartId { get; set; }
    public string BaseName { get; set; } = "";
    public int PartNumber { get; set; } = 1;
    public bool IsMultipart { get; set; }
    public Guid? ReferenceFlowId { get; set; }
    public string ReferenceFlowName { get; set; } = "";
    public string Folder { get; set; } = "";
    public List<string> Tags { get; set; } = [];
    public List<ReusableFlowStep> ReferenceFlowSteps { get; set; } = [];
    public string CharterObjective { get; set; } = "";
    public string CharterScope { get; set; } = "";
    public string CharterRisks { get; set; } = "";
    public string CharterTestData { get; set; } = "";
    public string CharterConstraints { get; set; } = "";
    public int CharterPlannedMinutes { get; set; }
    public string Os { get; set; } = Environment.OSVersion.VersionString;
    public string Resolution { get; set; } = "";
    public string BuildVersion { get; set; } = "";
    public EnvironmentSnapshot EnvironmentSnapshot { get; set; } = new();
    public List<SessionBookmark> Bookmarks { get; set; } = [];
    public List<ManualChecklistItem> ManualChecklist { get; set; } = [];
    public List<QaEvent> Events { get; set; } = [];
    public List<Annotation> Annotations { get; set; } = [];
    public List<EvidenceShot> Screenshots { get; set; } = [];
    public List<BugRecord> Bugs { get; set; } = [];
    public SessionState State { get; set; } = SessionState.Idle;
    public TimeSpan PausedTime { get; set; }
    public DateTimeOffset? PauseStartedAt { get; set; }

    [JsonIgnore] public TimeSpan Duration => (FinishedAt ?? DateTimeOffset.Now) - StartedAt - PausedTime;
    [JsonIgnore] public int ActionCount => Events.Count(e => e.Kind is EventKind.Click or EventKind.Input or EventKind.Select or EventKind.Submit or EventKind.Scroll);
    [JsonIgnore] public int PageCount => Events.Select(e => !string.IsNullOrWhiteSpace(e.TargetTitle) ? e.TargetTitle : e.ApplicationName).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
    [JsonIgnore] public int ErrorCount => Events.Count(e => e.Kind is EventKind.ConsoleError or EventKind.HttpError or EventKind.ResourceError) + Annotations.Count(a => a.Result == AnnotationResult.Error);
    [JsonIgnore] public string DurationText => Duration.ToString(@"hh\:mm\:ss");
    [JsonIgnore] public string StatusText => ErrorCount > 0 ? "Con errores" : Annotations.Any(a => a.Result == AnnotationResult.Doubt) ? "Con observaciones" : "Aprobado";
    [JsonIgnore] public string DisplayName => IsMultipart ? $"{(string.IsNullOrWhiteSpace(BaseName) ? Name : BaseName)} — Part {PartNumber}" : Name;
    [JsonIgnore] public string StartedDisplay => StartedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
    [JsonIgnore] public string HistoryStatus => State == SessionState.Finished ? (ErrorCount > 0 ? "Findings" : "Completed") : State.ToString();
    [JsonIgnore] public string PartDisplay => IsMultipart ? $"Part {PartNumber}" : "—";
    [JsonIgnore] public string TagsDisplay => Tags.Count == 0 ? "—" : string.Join(", ", Tags);
    [JsonIgnore] public string FolderDisplay => string.IsNullOrWhiteSpace(Folder) ? "—" : Folder;
}


public enum ProductMode { RecorderOnly, HybridTesting }
public enum AutomationTechnology { Web }

public sealed class AppSettings
{
    public string QaName { get; set; } = Environment.UserName;
    public bool ScreenshotOnNavigation { get; set; } = true;
    public bool ScreenshotBeforeSubmit { get; set; } = true;
    public bool PeriodicScreenshot { get; set; }
    public int PeriodicSeconds { get; set; } = 30;
    public bool IncludeTechnicalAppendix { get; set; } = true;
    public int ImageQuality { get; set; } = 100;
    public string DocumentLanguage { get; set; } = "English";
    public string UiLanguage { get; set; } = "English";
    public string UiTheme { get; set; } = "Dark";
    public string FileNameTemplate { get; set; } = "qa-report-{sesion}-{fecha}.pdf";
    public string CompanyLogoPath { get; set; } = "";
    public string ExportFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "QAFlow Recorder");
    public bool ShowFloatingBar { get; set; } = true;
    public PrivacyMaskMode PrivacyMasking { get; set; } = PrivacyMaskMode.Automatic;
    public string ExpectedCoverageTargets { get; set; } = "";
    public bool IncludeCoverageMap { get; set; } = true;
    public bool IncludeSessionInsights { get; set; } = true;
    public bool IncludeVisualComparison { get; set; } = true;
    public bool IncludeEvidenceQuality { get; set; } = true;

    // Hybrid automation is opt-in. RecorderOnly preserves the native QAFlow experience.
    public ProductMode ProductMode { get; set; } = ProductMode.RecorderOnly;
    public AutomationTechnology AutomationTechnology { get; set; } = AutomationTechnology.Web;
    public string WebAutomationBaseUrl { get; set; } = "";
    public string WebAutomationAllowedDomains { get; set; } = "";
    public string WebAutomationDefaultBrowser { get; set; } = "Chrome";
    public bool CaptureOutOfScopeAsEvidence { get; set; } = true;
    public List<PrivacyZone> PrivacyZones { get; set; } = [];
}

public sealed class PrivacyZone
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Private area";
    public string WindowTitleContains { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool Enabled { get; set; } = true;
    [JsonIgnore] public string ScopeDisplay => string.IsNullOrWhiteSpace(WindowTitleContains) ? "All captured windows" : WindowTitleContains;
}


public sealed class ReusableFlowStep
{
    public int Order { get; set; }
    public EventKind Kind { get; set; }
    public string Target { get; set; } = "";
    public string Description { get; set; } = "";
    public string ControlName { get; set; } = "";
    public string ControlType { get; set; } = "";
    public string AutomationId { get; set; } = "";
    public bool Important { get; set; }
}

public sealed class ReusableFlow
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string TestType { get; set; } = "Functional Normal";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public List<ReusableFlowStep> Steps { get; set; } = [];
    [JsonIgnore] public string DisplayName => $"{Name} · {Steps.Count} steps";
    public override string ToString() => DisplayName;
}

public sealed class RiskArea
{
    public string Target { get; set; } = "";
    public RiskLevel Level { get; set; }
    public int Score { get; set; }
    public int Interactions { get; set; }
    public int RepeatedActions { get; set; }
    public int Returns { get; set; }
    public int LongWaits { get; set; }
    public int Findings { get; set; }
    public string RationaleEnglish { get; set; } = "";
    public string RationaleSpanish { get; set; } = "";
}

public sealed class RiskAssessment
{
    public List<RiskArea> Areas { get; set; } = [];
    [JsonIgnore] public int HighRiskCount => Areas.Count(a => a.Level is RiskLevel.High or RiskLevel.Critical);
}

public sealed class ReferenceFlowComparison
{
    public int ExpectedSteps { get; set; }
    public int MatchedSteps { get; set; }
    public int MissingSteps { get; set; }
    public int ExtraSteps { get; set; }
    public double MatchPercent => ExpectedSteps == 0 ? 0 : Math.Round(MatchedSteps * 100d / ExpectedSteps, 1);
    public List<string> MissingDescriptions { get; set; } = [];
}


public sealed class VisualComparisonItem
{
    public string Target { get; set; } = "";
    public string CurrentEvidencePath { get; set; } = "";
    public string BaselinePath { get; set; } = "";
    public double DifferencePercent { get; set; }
    public string Status { get; set; } = "No baseline";
}

public sealed class VisualComparisonResult
{
    public List<VisualComparisonItem> Items { get; set; } = [];
    [JsonIgnore] public int ComparedCount => Items.Count(x => !string.IsNullOrWhiteSpace(x.BaselinePath));
    [JsonIgnore] public int SignificantChangeCount => Items.Count(x => x.Status == "Changed");
}

public sealed class EvidenceQualityIssue
{
    public string Code { get; set; } = "";
    public string MessageEnglish { get; set; } = "";
    public string MessageSpanish { get; set; } = "";
    public Severity Severity { get; set; } = Severity.Low;
}

public sealed class EvidenceQualityResult
{
    public int ReadinessPercent { get; set; }
    public List<EvidenceQualityIssue> Issues { get; set; } = [];
    [JsonIgnore] public bool IsReady => ReadinessPercent >= 85 && !Issues.Any(x => x.Severity is Severity.High or Severity.Critical);
}
