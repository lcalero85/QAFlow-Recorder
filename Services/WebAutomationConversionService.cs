using QAFlowRecorder.Models;

namespace QAFlowRecorder.Services;

/// <summary>
/// Phase 3 conversion service. It converts a finished QAFlow session into a draft web test case.
/// It does not execute Selenium. Recorder data remains the source of truth and out-of-scope
/// desktop activity is never silently promoted to web automation.
/// </summary>
public sealed class WebAutomationConversionService
{
    private readonly AppSettings _settings;
    public WebAutomationConversionService(AppSettings settings) => _settings = settings;

    public ConversionPreview Analyze(TestSession session, AutomationProject project)
    {
        var actions = session.Events
            .Where(IsConvertibleAction)
            .OrderBy(e => e.Timestamp)
            .ToList();

        var items = new List<ConversionPreviewItem>();
        foreach (var e in actions)
        {
            var browser = IsBrowserApplication(e.ApplicationName);
            var inScope = browser && IsInProjectScope(e.Url, project);
            var hasUrl = !string.IsNullOrWhiteSpace(e.Url);
            var isManual = !inScope;
            var reason = inScope
                ? "Web step in project scope"
                : !browser
                    ? "Desktop activity · evidence/manual checkpoint"
                    : !hasUrl
                        ? "Browser activity without a readable URL · review required"
                        : "Browser activity outside project URL scope · evidence/manual checkpoint";

            items.Add(new ConversionPreviewItem
            {
                EventId = e.Id,
                Time = e.Timestamp.LocalDateTime.ToString("HH:mm:ss"),
                Action = MapAction(e.Kind),
                Target = HumanTarget(e),
                Url = e.Url,
                Application = e.ApplicationName,
                InScope = inScope,
                ManualCheckpoint = isManual,
                Confidence = CalculateConfidence(e, inScope),
                Selector = inScope ? SelectorIntelligenceService.Build(e).PrimaryDisplay : "",
                DomInstrumented = e.WebDomEnriched,
                Reason = reason
            });
        }

        return new ConversionPreview
        {
            SessionId = session.Id,
            SessionName = session.DisplayName,
            TotalActions = items.Count,
            InScopeSteps = items.Count(x => x.InScope),
            ManualCheckpoints = items.Count(x => x.ManualCheckpoint),
            AverageConfidence = items.Where(x => x.InScope).Any()
                ? (int)Math.Round(items.Where(x => x.InScope).Average(x => x.Confidence))
                : 0,
            Items = items
        };
    }

    public AutomationTestCase Convert(TestSession session, AutomationProject project, string name, string key)
    {
        var preview = Analyze(session, project);
        var tc = new AutomationTestCase
        {
            Key = key,
            Name = string.IsNullOrWhiteSpace(name) ? session.DisplayName : name.Trim(),
            Description = $"Created from recorded QAFlow session '{session.DisplayName}'.",
            Mode = preview.InScopeSteps > 0 ? AutomationCaseMode.Assisted : AutomationCaseMode.Manual,
            Status = preview.InScopeSteps > 0 ? AutomationCaseStatus.NeedsReview : AutomationCaseStatus.Draft,
            SourceSessionId = session.Id,
            SourceSessionName = session.DisplayName,
            BaseUrlSnapshot = project.BaseUrl,
            AutomationReadiness = preview.AverageConfidence,
            ConversionSummary = $"{preview.InScopeSteps} web step(s) in scope · {preview.ManualCheckpoints} manual/evidence checkpoint(s)."
        };

        var sourceEvents = session.Events.ToDictionary(x => x.Id);
        var order = 1;
        foreach (var item in preview.Items)
        {
            if (!sourceEvents.TryGetValue(item.EventId, out var e)) continue;
            if (!item.InScope && !_settings.CaptureOutOfScopeAsEvidence) continue;

            tc.Steps.Add(new AutomationTestStep
            {
                Order = order++,
                Action = item.InScope ? MapAction(e.Kind) : "Manual checkpoint",
                Target = HumanTarget(e),
                ExpectedResult = item.InScope ? SuggestedExpectedResult(e) : "QA verifies the external desktop step before continuing.",
                SelectorHint = item.InScope ? BuildSelectorHint(e) : string.Empty,
                SelectorProfile = item.InScope ? SelectorIntelligenceService.Build(e) : new WebSelectorProfile(),
                IsManualCheckpoint = !item.InScope,
                AutomationConfidence = item.Confidence,
                SourceEventId = e.Id,
                SourceUrl = e.Url,
                SourceApplication = e.ApplicationName,
                SourceWindow = e.WindowTitle,
                IsInAutomationScope = item.InScope,
                CaptureNote = item.Reason
            });
        }

        return tc;
    }

    private bool IsInProjectScope(string? candidate, AutomationProject project)
    {
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var url)) return false;
        if (!Uri.TryCreate(project.BaseUrl, UriKind.Absolute, out var baseUrl)) return false;
        if (string.Equals(url.Host, baseUrl.Host, StringComparison.OrdinalIgnoreCase)) return true;

        var allowed = (project.AllowedDomains ?? string.Empty)
            .Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return allowed.Any(d => string.Equals(url.Host, NormalizeDomain(d), StringComparison.OrdinalIgnoreCase) ||
                                url.Host.EndsWith("." + NormalizeDomain(d), StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeDomain(string value)
    {
        value = value.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var u)) return u.Host;
        return value.TrimStart('*', '.').TrimEnd('/');
    }

    private static bool IsConvertibleAction(QaEvent e) =>
        !e.ExcludeFromReport && e.Kind is EventKind.Click or EventKind.Input or EventKind.Select or EventKind.Submit or EventKind.Scroll or EventKind.Navigation;

    private static bool IsBrowserApplication(string? app) =>
        app?.Equals("chrome", StringComparison.OrdinalIgnoreCase) == true ||
        app?.Equals("msedge", StringComparison.OrdinalIgnoreCase) == true ||
        app?.Equals("firefox", StringComparison.OrdinalIgnoreCase) == true;

    private static string MapAction(EventKind kind) => kind switch
    {
        EventKind.Click => "Click",
        EventKind.Input => "Input",
        EventKind.Select => "Select",
        EventKind.Submit => "Submit",
        EventKind.Scroll => "Scroll",
        EventKind.Navigation => "Navigate",
        _ => kind.ToString()
    };

    private static string HumanTarget(QaEvent e)
    {
        if (!string.IsNullOrWhiteSpace(e.ControlName))
            return !string.IsNullOrWhiteSpace(e.ControlType) ? $"{e.ControlName} ({e.ControlType})" : e.ControlName;
        if (!string.IsNullOrWhiteSpace(e.CustomDescription)) return e.CustomDescription;
        if (!string.IsNullOrWhiteSpace(e.TargetTitle)) return e.TargetTitle;
        return !string.IsNullOrWhiteSpace(e.ApplicationName) ? e.ApplicationName : "Recorded target";
    }

    private static string BuildSelectorHint(QaEvent e)
    {
        var profile = SelectorIntelligenceService.Build(e);
        if (!string.IsNullOrWhiteSpace(profile.PrimaryValue)) return profile.PrimaryDisplay;
        if (!string.IsNullOrWhiteSpace(e.Selector) && !e.Selector.StartsWith("screen(", StringComparison.OrdinalIgnoreCase)) return e.Selector;
        return "DOM selector required during web instrumentation review";
    }

    private static int CalculateConfidence(QaEvent e, bool inScope)
    {
        if (!inScope) return 0;
        var profile = SelectorIntelligenceService.Build(e);
        if (profile.DomInstrumented && profile.Confidence > 0) return profile.Confidence;

        var score = 35;
        if (!string.IsNullOrWhiteSpace(e.ControlName)) score += 20;
        if (!string.IsNullOrWhiteSpace(e.AutomationId)) score += 20;
        if (!string.IsNullOrWhiteSpace(e.ControlType)) score += 10;
        if (!string.IsNullOrWhiteSpace(e.Selector) && !e.Selector.StartsWith("screen(", StringComparison.OrdinalIgnoreCase)) score += 10;
        if (e.Kind is EventKind.Navigation or EventKind.Scroll) score -= 5;
        return Math.Clamp(score, 0, 90);
    }

    private static string SuggestedExpectedResult(QaEvent e) => e.Kind switch
    {
        EventKind.Navigation => "The expected page is displayed.",
        EventKind.Submit => "The form is processed and the expected next state is displayed.",
        EventKind.Input => "The field accepts the provided test data.",
        EventKind.Select => "The selected option is applied.",
        EventKind.Click => "The selected control performs its expected action.",
        EventKind.Scroll => "The expected content remains available after scrolling.",
        _ => "The expected application state is reached."
    };
}

public sealed class ConversionPreview
{
    public Guid SessionId { get; set; }
    public string SessionName { get; set; } = "";
    public int TotalActions { get; set; }
    public int InScopeSteps { get; set; }
    public int ManualCheckpoints { get; set; }
    public int AverageConfidence { get; set; }
    public List<ConversionPreviewItem> Items { get; set; } = [];
}

public sealed class ConversionPreviewItem
{
    public Guid EventId { get; set; }
    public string Time { get; set; } = "";
    public string Action { get; set; } = "";
    public string Target { get; set; } = "";
    public string Url { get; set; } = "";
    public string Application { get; set; } = "";
    public bool InScope { get; set; }
    public bool ManualCheckpoint { get; set; }
    public int Confidence { get; set; }
    public string Selector { get; set; } = "";
    public bool DomInstrumented { get; set; }
    public string Reason { get; set; } = "";
    public string ScopeDisplay => InScope ? "WEB" : "MANUAL";
}
