using System.Windows;

namespace QAFlowRecorder.Services;

public static class ThemeManager
{
    public const string Dark = "Dark";
    public const string Aero = "Aero";

    public static string CurrentTheme { get; private set; } = Dark;

    public static void ApplyTheme(string? requestedTheme)
    {
        var theme = string.Equals(requestedTheme, Aero, StringComparison.OrdinalIgnoreCase) ? Aero : Dark;
        var resources = Application.Current?.Resources;
        if (resources is null) return;

        var source = new Uri($"Themes/{theme}Theme.xaml", UriKind.Relative);
        var merged = resources.MergedDictionaries;
        var existing = merged.FirstOrDefault(d => d.Source is not null &&
            (d.Source.OriginalString.EndsWith("DarkTheme.xaml", StringComparison.OrdinalIgnoreCase) ||
             d.Source.OriginalString.EndsWith("AeroTheme.xaml", StringComparison.OrdinalIgnoreCase)));

        var replacement = new ResourceDictionary { Source = source };
        if (existing is null) merged.Insert(0, replacement);
        else
        {
            var index = merged.IndexOf(existing);
            merged[index] = replacement;
        }

        CurrentTheme = theme;
        foreach (Window window in Application.Current.Windows)
            WindowThemeHelper.ApplyTitleBar(window);
    }
}
