namespace RASLogAggregator.Models;

/// <summary>Un composant RAS découvert via l'API PowerShell.</summary>
public class RasComponent
{
    public string Role { get; set; } = "";    // Broker / Gateway / EnrollmentServer
    public string Server { get; set; } = "";   // FQDN ou IP
    public int? Id { get; set; }
    public int? SiteId { get; set; }

    public override string ToString() => $"{Role} — {Server}";
}
