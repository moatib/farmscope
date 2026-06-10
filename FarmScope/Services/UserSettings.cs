using System.Globalization;
using System.IO;
using System.Text.Json;

namespace RASLogAggregator.Services;

/// <summary>Préférences utilisateur (thème, langue), persistées dans %AppData%\FarmScope.</summary>
public class UserSettings
{
    public string Theme { get; set; } = "Dark";       // Dark | Light
    public string Language { get; set; } = "";         // fr | en | de ("" = langue de l'OS)

    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "FarmScope", "settings.json");

    public static UserSettings Current { get; private set; } = new();

    /// <summary>Langue effective : préférence enregistrée, sinon celle de Windows (fr/de → natif, sinon en).</summary>
    public string EffectiveLanguage
    {
        get
        {
            if (Language is "fr" or "en" or "de") return Language;
            var os = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            return os is "fr" or "de" ? os : "en";
        }
    }

    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(FilePath));
                if (s != null) { Current = s; return; }
            }
        }
        catch { /* repli sur les défauts */ }
        Current = new UserSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* best effort */ }
    }
}
