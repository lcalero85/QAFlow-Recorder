using QAFlowRecorder.Models;

namespace QAFlowRecorder.Services;

public static class SelectorIntelligenceService
{
    public static WebSelectorProfile Build(QaEvent e)
    {
        var candidates = new List<WebSelectorCandidate>();
        void Add(string strategy, string value, int confidence)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            if (candidates.Any(x => x.Strategy.Equals(strategy, StringComparison.OrdinalIgnoreCase) && x.Value == value)) return;
            candidates.Add(new WebSelectorCandidate { Strategy = strategy, Value = value.Trim(), Confidence = confidence });
        }

        Add("data-testid", e.WebDataTestId, 100);
        if (IsStableId(e.WebElementId)) Add("id", e.WebElementId, 96);
        Add("name", e.WebElementName, 91);
        Add("aria-label", e.WebAriaLabel, 89);
        if (!string.IsNullOrWhiteSpace(e.WebElementRole) && !string.IsNullOrWhiteSpace(e.WebElementText))
            Add("role+name", $"{e.WebElementRole}|{Trim(e.WebElementText, 80)}", 86);
        Add("css", e.WebCssSelector, 78);
        Add("xpath", e.WebXPath, 66);

        if (candidates.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(e.AutomationId)) Add("uia-automation-id", e.AutomationId, 52);
            if (!string.IsNullOrWhiteSpace(e.ControlName)) Add("uia-name", e.ControlName, 45);
        }

        var best = candidates.OrderByDescending(x => x.Confidence).FirstOrDefault();
        if (best is not null) best.Preferred = true;

        return new WebSelectorProfile
        {
            TagName = e.WebElementTag,
            AccessibleName = FirstNonEmpty(e.WebAriaLabel, e.WebElementText, e.ControlName),
            Role = e.WebElementRole,
            PrimaryStrategy = best?.Strategy ?? "",
            PrimaryValue = best?.Value ?? "",
            Confidence = best?.Confidence ?? 0,
            DomInstrumented = e.WebDomEnriched,
            Candidates = candidates.OrderByDescending(x => x.Confidence).ToList()
        };
    }

    public static void ApplyPrimaryToEvent(QaEvent e)
    {
        var p = Build(e);
        e.WebPrimarySelectorType = p.PrimaryStrategy;
        e.WebPrimarySelector = p.PrimaryValue;
        e.WebSelectorConfidence = p.Confidence;
    }

    private static bool IsStableId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 100) return false;
        var digits = id.Count(char.IsDigit);
        if (digits > 8 && digits > id.Length / 2) return false;
        return !id.Contains("react-select-", StringComparison.OrdinalIgnoreCase) &&
               !id.Contains("ember", StringComparison.OrdinalIgnoreCase);
    }

    private static string Trim(string value, int max) => value.Length <= max ? value : value[..max];
    private static string FirstNonEmpty(params string[] values) => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "";
}
