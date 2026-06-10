namespace RASLogAggregator.Models;

/// <summary>Une entrée de log agrégée (une ligne ou un bloc multi-lignes).</summary>
public class LogEntry
{
    public DateTime? Timestamp { get; set; }
    public string RawTimestamp { get; set; } = "";
    public string Server { get; set; } = "";        // FQDN du serveur source
    public string Role { get; set; } = "";           // Broker / Gateway / EnrollmentServer
    public string LogFile { get; set; } = "";        // controller.log, gateway.log, ...
    public string Severity { get; set; } = "INFO";   // ERROR / WARN / INFO / DEBUG / TRACE / OTHER
    public string Message { get; set; } = "";
    public int LineNumber { get; set; }

    /// <summary>Colonne affichée : timestamp parsé ou texte brut en repli.</summary>
    public string TimeDisplay =>
        Timestamp.HasValue ? Timestamp.Value.ToString("yyyy-MM-dd HH:mm:ss.fff") : RawTimestamp;

    /// <summary>Première ligne du message pour l'affichage en grille.</summary>
    public string MessagePreview
    {
        get
        {
            var i = Message.IndexOf('\n');
            return i < 0 ? Message : Message[..i] + "  …";
        }
    }
}
