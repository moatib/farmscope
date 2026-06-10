using System.IO;
using System.Text;
using System.Text.Json;

namespace RASLogAggregator.Services;

/// <summary>Configuration de l'application, chargée depuis config.json (sinon valeurs par défaut).</summary>
public class AppConfig
{
    public string AdminShare { get; set; } = "C$";
    public string LogSubPath { get; set; } = @"ProgramData\Parallels\RASLogs";
    public int MaxLinesPerFile { get; set; } = 15000;
    public string Encoding { get; set; } = "utf-8";

    /// <summary>Si vrai, tous les composants sont lus en local (utile pour un test mono-serveur).</summary>
    public bool ForceLocal { get; set; } = false;

    /// <summary>Noms de serveurs à considérer comme « locaux » (FQDN, alias…).</summary>
    public string[] ExtraLocalNames { get; set; } = Array.Empty<string>();

    public string TimestampRegex { get; set; } =
        @"(?<ts>\d{2}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2}(?:[.,]\d{1,3})?)";
    public string SeverityRegex { get; set; } =
        @"\b(?<sev>FATAL|CRITICAL|CRIT|ERROR|ERR|WARNING|WARN|NOTICE|INFO|DEBUG|TRACE|VERBOSE)\b";

    /// <summary>Identifie le début d'une entrée de log. Défaut = crochet RAS « [I … », « [E … ».
    /// Le groupe "sev" (lettre I/E/W/D…) donne la sévérité.</summary>
    public string LineStartRegex { get; set; } = @"^\[(?<sev>[A-Za-z])[\s/]";

    public string[] TimestampFormats { get; set; } =
    {
        "dd-MM-yy HH:mm:ss", "dd-MM-yy HH:mm:ss.fff",
        "dd.MM.yyyy HH:mm:ss", "dd.MM.yyyy HH:mm:ss.fff",
        "dd/MM/yyyy HH:mm:ss", "dd/MM/yyyy HH:mm:ss.fff",
        "MM/dd/yyyy HH:mm:ss", "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH:mm:ss.fff", "yyyy-MM-ddTHH:mm:ss"
    };

    public Dictionary<string, string[]> RoleLogFiles { get; set; } = new()
    {
        ["Broker"] = new[]
        {
            "controller.log", "redundancy.log", "notifdispatch.log", "devscheduler.log",
            "licensing.log", "cpuloadbalancer.log", "powershell.log", "console.log",
            "rasgroupmanager.log", "permission.log"
        },
        ["Gateway"] = new[] { "gateway.log", "HTML5GW.log" },
        ["EnrollmentServer"] = new[] { "EnrollServer.log" }
    };

    public Encoding GetEncoding()
    {
        try { return System.Text.Encoding.GetEncoding(Encoding); }
        catch { return System.Text.Encoding.UTF8; }
    }

    public static AppConfig Load()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "config.json");
            if (File.Exists(path))
            {
                var opts = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                };
                var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), opts);
                if (cfg != null) return cfg;
            }
        }
        catch { /* repli sur les valeurs par défaut */ }
        return new AppConfig();
    }
}
