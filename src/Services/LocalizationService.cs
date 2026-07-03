using System.Windows;
using Wpf.Ui.Appearance;

namespace ExWSLC.Services;

public static class LocalizationService
{
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
        if (theme.Equals("Dark", StringComparison.OrdinalIgnoreCase))
            ApplicationThemeManager.Apply(ApplicationTheme.Dark);
        else if (theme.Equals("Light", StringComparison.OrdinalIgnoreCase))
            ApplicationThemeManager.Apply(ApplicationTheme.Light);
        else
            ApplicationThemeManager.Apply(ApplicationThemeManager.GetSystemTheme() == SystemTheme.Dark
                ? ApplicationTheme.Dark
                : ApplicationTheme.Light);
    }
}
