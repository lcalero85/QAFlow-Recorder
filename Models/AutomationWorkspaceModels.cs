using System.Text.Json.Serialization;

namespace QAFlowRecorder.Models;

public enum AutomationCaseMode { Manual, Guided, Assisted, Automated }
public enum AutomationCaseStatus { Draft, Ready, NeedsReview, Archived }
public enum AutomationAssertionType { ElementVisible, ElementNotVisible, ElementEnabled, ElementTextContains, ElementTextEquals, ElementAttributeEquals, TextContains, UrlContains, TitleContains, VisualBaselineMatch, NoConsoleErrors, NoNetworkErrors, MaxPageLoadMs, MaxDomContentLoadedMs, MaxFirstContentfulPaintMs, MaxResourceDurationMs }
public enum AutomationWaitCondition { Auto, None, DocumentReady, ElementPresent, ElementVisible, ElementEnabled, ElementHidden }
public enum AutomationWindowTarget { Current, Newest, First, Last, TitleContains }
public enum AutomationFrameTarget { DefaultContent, CssSelector, NameOrId, Index }
public enum AutomationShadowDomMode { None, OpenShadowPath }
public enum AutomationFailureAction { StopTest, ContinueTest, ManualCheckpoint }
public enum AutomationHealingMode { Off, Conservative, Advanced }
public enum AutomationStepConditionType { Always, IfElementExists, IfElementNotExists, IfUrlContains, IfTitleContains, IfVariableEquals, IfVariableNotEquals }

public sealed class AutomationAssertion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public AutomationAssertionType Type { get; set; } = AutomationAssertionType.ElementVisible;
    public string Expected { get; set; } = "";
    public bool UseStepTarget { get; set; } = true;
    [JsonIgnore] public string Display => string.IsNullOrWhiteSpace(Expected) ? Type.ToString() : $"{Type} · {Expected}";
}

public sealed class AutomationDataVariable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "variable";
    public string Value { get; set; } = "";
    public bool IsSecret { get; set; }
    public string SecretKey { get; set; } = "";
    [JsonIgnore] public string TypeDisplay => IsSecret ? "Secret" : "Value";
    [JsonIgnore] public string DisplayValue => IsSecret ? "••••••••" : Value;
}

public sealed class AutomationDataSet
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Default data";
    public string Description { get; set; } = "";
    public List<AutomationDataVariable> Variables { get; set; } = [];
    [JsonIgnore] public string DisplayName => $"{Name} · {Variables.Count} variable(s)";
    public override string ToString() => DisplayName;
}

public sealed class AutomationTestStep
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Order { get; set; }
    public string Action { get; set; } = "";
    public string Target { get; set; } = "";
    public string ExpectedResult { get; set; } = "";
    public string SelectorHint { get; set; } = "";
    public WebSelectorProfile SelectorProfile { get; set; } = new();
    public bool IsManualCheckpoint { get; set; }
    public int AutomationConfidence { get; set; }

    // Phase 3 provenance. These fields preserve the original recorder context without
    // making the automation engine depend on Recorder internals.
    public Guid? SourceEventId { get; set; }
    public string SourceUrl { get; set; } = "";
    public string SourceApplication { get; set; } = "";
    public string SourceWindow { get; set; } = "";
    public bool IsInAutomationScope { get; set; }
    public string CaptureNote { get; set; } = "";
    public string ValueExpression { get; set; } = "";
    public List<AutomationAssertion> Assertions { get; set; } = [];
    public AutomationWaitCondition WaitCondition { get; set; } = AutomationWaitCondition.Auto;
    public int WaitTimeoutMs { get; set; } = 10000;
    public int WaitPollMs { get; set; } = 250;
    public AutomationWindowTarget WindowTarget { get; set; } = AutomationWindowTarget.Current;
    public string WindowMatch { get; set; } = "";
    public AutomationFrameTarget FrameTarget { get; set; } = AutomationFrameTarget.DefaultContent;
    public string FrameValue { get; set; } = "";
    public AutomationShadowDomMode ShadowDomMode { get; set; } = AutomationShadowDomMode.None;
    public string ShadowCssPath { get; set; } = "";
    public bool CloseCurrentWindowAfterStep { get; set; }
    public bool AllowJavaScriptClickFallback { get; set; } = true;
    // Phase 15 Block 1: optional API helper settings. These remain advanced and web-scope constrained.
    public string ApiMethod { get; set; } = "GET";
    public string ApiUrl { get; set; } = "";
    public string ApiBody { get; set; } = "";
    public string ApiContentType { get; set; } = "application/json";
    public int ApiExpectedStatus { get; set; } = 200;
    public string ApiHeadersExpression { get; set; } = "";
    // Phase 15 Block 2: optional conditional execution. Advanced only; normal Recorder is unaffected.
    public AutomationStepConditionType ConditionType { get; set; } = AutomationStepConditionType.Always;
    public string ConditionExpression { get; set; } = "";
    public string ConditionExpected { get; set; } = "";
    // Phase 15 Block 3: controlled repetition. Advanced only and deliberately bounded.
    public int RepeatCount { get; set; } = 1;
    public int RepeatDelayMs { get; set; } = 0;
    public int MaxRetries { get; set; } = 1;
    public int RetryDelayMs { get; set; } = 600;
    public AutomationFailureAction OnFailure { get; set; } = AutomationFailureAction.StopTest;
    // Phase 15 Block 4: advanced selector self-healing. Conservative remains the compatibility default.
    public AutomationHealingMode HealingMode { get; set; } = AutomationHealingMode.Conservative;
    public int HealingMinimumScore { get; set; } = 78;
    public bool LearnHealedSelector { get; set; } = true;
    public int HealingCount { get; set; }
    public DateTimeOffset? LastHealedAt { get; set; }
    [JsonIgnore] public string DisplayName => $"{Order}. {Action} · {Target}";
    [JsonIgnore] public string WaitDisplay => $"{WaitCondition} · {WaitTimeoutMs / 1000.0:0.#}s";
    [JsonIgnore] public string RecoveryDisplay => $"{MaxRetries} retry · {OnFailure}";
    [JsonIgnore] public string InteractionDisplay => string.IsNullOrWhiteSpace(Action) ? "—" : Action + (AllowJavaScriptClickFallback && Action.Equals("Click", StringComparison.OrdinalIgnoreCase) ? " · JS fallback" : "");
    [JsonIgnore] public string BrowserContextDisplay
    {
        get
        {
            var window = WindowTarget == AutomationWindowTarget.Current ? "Current window" : WindowTarget.ToString();
            var frame = FrameTarget == AutomationFrameTarget.DefaultContent ? "Top document" : $"{FrameTarget}: {FrameValue}";
            var shadow = ShadowDomMode == AutomationShadowDomMode.None ? "" : $" · Shadow DOM: {ShadowCssPath}";
            return $"{window} · {frame}{shadow}" + (CloseCurrentWindowAfterStep ? " · close after" : "");
        }
    }
}


public sealed class WebSelectorCandidate
{
    public string Strategy { get; set; } = "";
    public string Value { get; set; } = "";
    public int Confidence { get; set; }
    public bool Preferred { get; set; }
    [JsonIgnore] public string Display => string.IsNullOrWhiteSpace(Value) ? "—" : $"{Strategy}: {Value}";
}

public sealed class WebSelectorProfile
{
    public string TagName { get; set; } = "";
    public string AccessibleName { get; set; } = "";
    public string Role { get; set; } = "";
    public string PrimaryStrategy { get; set; } = "";
    public string PrimaryValue { get; set; } = "";
    public int Confidence { get; set; }
    public bool DomInstrumented { get; set; }
    public List<WebSelectorCandidate> Candidates { get; set; } = [];
    [JsonIgnore] public string PrimaryDisplay => string.IsNullOrWhiteSpace(PrimaryValue) ? "DOM review required" : $"{PrimaryStrategy}: {PrimaryValue}";
}

public sealed class AutomationTestCase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Key { get; set; } = "";
    public string Name { get; set; } = "New test case";
    public string Description { get; set; } = "";
    public string Preconditions { get; set; } = "";
    public string ExpectedOutcome { get; set; } = "";
    public AutomationCaseMode Mode { get; set; } = AutomationCaseMode.Manual;
    public AutomationCaseStatus Status { get; set; } = AutomationCaseStatus.Draft;
    public int Version { get; set; } = 1;
    public Guid? SourceSessionId { get; set; }
    public string SourceSessionName { get; set; } = "";
    public string BaseUrlSnapshot { get; set; } = "";
    public int AutomationReadiness { get; set; }
    public Guid? DefaultDataSetId { get; set; }
    // Phase 13 Blocks 7-8: advanced lifecycle + data-driven execution.
    public Guid? BeforeTestComponentId { get; set; }
    public Guid? AfterTestComponentId { get; set; }
    public Guid? RequiredTestCaseId { get; set; }
    public bool DataDrivenEnabled { get; set; }
    public List<Guid> DataSetIds { get; set; } = [];
    public string ConversionSummary { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public List<AutomationTestStep> Steps { get; set; } = [];

    [JsonIgnore] public string DisplayName => string.IsNullOrWhiteSpace(Key) ? Name : $"{Key} · {Name}";
    [JsonIgnore] public string ModeDisplay => Mode.ToString();
    public override string ToString() => DisplayName;
}

public sealed class AutomationSuite
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New suite";
    public string Description { get; set; } = "";
    public int Order { get; set; }
    public Guid? BeforeSuiteComponentId { get; set; }
    public Guid? AfterSuiteComponentId { get; set; }
    public List<AutomationTestCase> TestCases { get; set; } = [];
    [JsonIgnore] public string DisplayName => $"{Name} · {TestCases.Count} case(s)";
    public override string ToString() => DisplayName;
}


public sealed class ReusableAutomationComponent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Reusable component";
    public string Description { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public List<AutomationTestStep> Steps { get; set; } = [];
    [JsonIgnore] public string DisplayName => $"{Name} · {Steps.Count} step(s)";
    public override string ToString() => DisplayName;
}



public enum AutomationScheduleFrequency { Daily, Weekly }

public sealed class AutomationEnvironmentProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Default";
    public string BaseUrl { get; set; } = "";
    public string AllowedDomains { get; set; } = "";
    public string Browser { get; set; } = "Chrome";
    public Guid? DefaultDataSetId { get; set; }
    public bool IsDefault { get; set; }
    [JsonIgnore] public string DisplayName => string.IsNullOrWhiteSpace(BaseUrl) ? Name : $"{Name} · {BaseUrl}";
    public override string ToString() => DisplayName;
}

public sealed class AutomationSchedule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Scheduled regression";
    public Guid SuiteId { get; set; }
    public Guid? EnvironmentId { get; set; }
    public AutomationScheduleFrequency Frequency { get; set; } = AutomationScheduleFrequency.Daily;
    public string TimeOfDay { get; set; } = "22:00";
    public string DaysOfWeek { get; set; } = "MON";
    public bool Enabled { get; set; } = true;
    public bool SendReport { get; set; } = true;
    public bool BrowserHeadless { get; set; }
    public DateTimeOffset? LastRegisteredAt { get; set; }
    public string TaskName { get; set; } = "";
    [JsonIgnore] public string DisplayName => $"{Name} · {Frequency} {TimeOfDay}";
    public override string ToString() => DisplayName;
}

public sealed class AutomationEmailDeliverySettings
{
    public bool Enabled { get; set; }
    public string SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string Username { get; set; } = "";
    public string PasswordSecretKey { get; set; } = "";
    public string FromAddress { get; set; } = "";
    public string Recipients { get; set; } = "";
    public bool AutoSendAfterSuiteRun { get; set; }
    public bool AttachSuiteHtml { get; set; } = true;
    public bool AttachCaseHtml { get; set; } = false;
    public List<string> PdfAttachments { get; set; } = [];
}

public sealed class AutomationProject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New project";
    public string Description { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string AllowedDomains { get; set; } = "";
    public string DefaultBrowser { get; set; } = "Chrome";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public List<AutomationSuite> Suites { get; set; } = [];
    public List<AutomationDataSet> DataSets { get; set; } = [];
    public List<ReusableAutomationComponent> Components { get; set; } = [];
    public List<AutomationEnvironmentProfile> Environments { get; set; } = [];
    public List<AutomationSchedule> Schedules { get; set; } = [];
    public AutomationEmailDeliverySettings EmailDelivery { get; set; } = new();

    [JsonIgnore] public int TestCaseCount => Suites.Sum(s => s.TestCases.Count);
    [JsonIgnore] public string DisplayName => $"{Name} · {Suites.Count} suite(s) · {TestCaseCount} case(s)";
    public override string ToString() => DisplayName;
}
