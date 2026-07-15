using System.Windows;
using Wpf.Ui.Appearance;

namespace ExWSLC.Services;

public static class LocalizationService
{
    public static string GetString(string key, string fallback) =>
        Application.Current?.TryFindResource(key) as string ?? fallback;

    public static void ApplyLanguage(string language)
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var current = dictionaries.FirstOrDefault(dictionary => dictionary.Source?.OriginalString.Contains("Strings.") == true);
        if (current is not null) dictionaries.Remove(current);
        var culture = language.Equals("en-US", StringComparison.OrdinalIgnoreCase) ? "en-US" : "zh-CN";
        dictionaries.Add(new ResourceDictionary
        {
            Source = new Uri($"Resources/Strings.{culture}.xaml", UriKind.Relative)
        });
    }

    public static void ApplyTheme(string theme)
    {
        var applicationTheme = theme.ToLowerInvariant() switch
        {
            "dark" => ApplicationTheme.Dark,
            "light" => ApplicationTheme.Light,
            _ => SystemThemeManager.GetCachedSystemTheme() == SystemTheme.Dark
                ? ApplicationTheme.Dark
                : ApplicationTheme.Light
        };
        ApplicationThemeManager.Apply(applicationTheme);
    }
}
