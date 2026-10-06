using QAFlowRecorder.Models;

namespace QAFlowRecorder.Services;

public sealed class CapabilityManager
{
    private readonly AppSettings _settings;
    public CapabilityManager(AppSettings settings) => _settings = settings;

    public bool AutomationEnabled => _settings.ProductMode == ProductMode.HybridTesting;
    public bool RecorderEnabled => true;
    public bool WebAutomationEnabled => AutomationEnabled && _settings.AutomationTechnology == AutomationTechnology.Web;

    public bool TryValidateAutomationConfiguration(out string error)
    {
        error = string.Empty;
        if (!AutomationEnabled) return true;
        if (!Uri.TryCreate(_settings.WebAutomationBaseUrl?.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            error = "A valid http/https Base URL is required for Web Automation.";
            return false;
        }
        return true;
    }

    public bool IsUrlInAutomationScope(string? candidate)
    {
        if (!WebAutomationEnabled || !Uri.TryCreate(candidate, UriKind.Absolute, out var url)) return false;
        if (!Uri.TryCreate(_settings.WebAutomationBaseUrl?.Trim(), UriKind.Absolute, out var baseUrl)) return false;
        if (string.Equals(url.Host, baseUrl.Host, StringComparison.OrdinalIgnoreCase)) return true;

        var allowed = (_settings.WebAutomationAllowedDomains ?? string.Empty)
            .Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return allowed.Any(domain => string.Equals(url.Host, domain, StringComparison.OrdinalIgnoreCase) ||
                                     url.Host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase));
    }
}
