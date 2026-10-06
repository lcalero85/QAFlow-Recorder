using System.Text.RegularExpressions;
using QAFlowRecorder.Models;

namespace QAFlowRecorder.Services;

public static class AutomationDataResolver
{
    private static readonly Regex Token = new(@"\$\{(?<name>[A-Za-z0-9_.-]+)\}", RegexOptions.Compiled);

    public static bool UsesSecret(string? expression, AutomationDataSet? dataSet)
    {
        if (dataSet is null || string.IsNullOrWhiteSpace(expression)) return false;
        return Token.Matches(expression).Cast<Match>().Any(match =>
        {
            var name = match.Groups["name"].Value;
            return dataSet.Variables.Any(v => v.IsSecret && v.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        });
    }

    public static string Resolve(string? expression, AutomationDataSet? dataSet)
    {
        var input = expression ?? string.Empty;
        if (dataSet is null || string.IsNullOrEmpty(input)) return input;
        return Token.Replace(input, match =>
        {
            var name = match.Groups["name"].Value;
            var variable = dataSet.Variables.FirstOrDefault(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (variable is null) throw new InvalidOperationException($"Test data variable '${{{name}}}' was not found in data set '{dataSet.Name}'.");
            if (!variable.IsSecret) return variable.Value ?? string.Empty;
            var secret = CredentialManagerService.ReadSecret(variable.SecretKey);
            if (string.IsNullOrEmpty(secret)) throw new InvalidOperationException($"Secret variable '${{{name}}}' is not available in Windows Credential Manager.");
            return secret;
        });
    }
}
