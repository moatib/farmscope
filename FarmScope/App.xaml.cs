using System.Windows;
using RASLogAggregator.Services;

namespace RASLogAggregator;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        UserSettings.Load();
        Loc.Lang = UserSettings.Current.EffectiveLanguage;
        ThemeManager.Apply(UserSettings.Current.Theme);
    }
}

/// <summary>Bascule le dictionnaire de thème (Dark/Light) à chaud.</summary>
public static class ThemeManager
{
    public static string Current { get; private set; } = "Dark";

    public static void Apply(string theme)
    {
        Current = theme == "Light" ? "Light" : "Dark";
        var dict = new ResourceDictionary
        {
            Source = new Uri($"Themes/{Current}.xaml", UriKind.Relative)
        };

        var merged = Application.Current.Resources.MergedDictionaries;
        // Remplace le dictionnaire de thème existant (celui dont la source pointe vers Themes/)
        for (int i = 0; i < merged.Count; i++)
        {
            if (merged[i].Source != null &&
                merged[i].Source.OriginalString.Contains("Themes/", StringComparison.OrdinalIgnoreCase))
            {
                merged[i] = dict;
                return;
            }
        }
        merged.Add(dict);
    }

    public static string Toggle()
    {
        Apply(Current == "Dark" ? "Light" : "Dark");
        return Current;
    }
}
