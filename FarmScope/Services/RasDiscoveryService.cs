using System.Diagnostics;
using System.IO;
using System.Security;
using System.Text;
using System.Text.Json;
using RASLogAggregator.Models;

namespace RASLogAggregator.Services;

/// <summary>
/// Interroge la ferme RAS via le module PowerShell RASAdmin (ex-PSAdmin) pour
/// énumérer Brokers, Gateways et Enrollment Servers.
///
/// On passe par un shell-out vers powershell.exe (Windows PowerShell 5.1) plutôt
/// que par le SDK .NET : c'est là que le module RASAdmin est enregistré, et ça
/// évite d'embarquer Microsoft.PowerShell.SDK (~50 Mo) dans l'exe.
/// </summary>
public class RasDiscoveryService
{
    // Le mot de passe et l'utilisateur transitent par variables d'environnement
    // (pas par la ligne de commande, donc invisibles dans la liste des process).
    private const string Script = @"
$ErrorActionPreference = 'Stop'
$server = $env:RAS_SERVER
$user   = $env:RAS_USER
$pass   = $env:RAS_PASS

# RAS 20+ : module RASAdmin. Antérieur : PSAdmin.
if (Get-Module -ListAvailable -Name RASAdmin) { Import-Module RASAdmin -ErrorAction Stop }
elseif (Get-Module -ListAvailable -Name PSAdmin) { Import-Module PSAdmin -ErrorAction Stop }
else { throw 'Module RAS PowerShell introuvable (RASAdmin / PSAdmin).' }

$sec = ConvertTo-SecureString $pass -AsPlainText -Force
New-RASSession -Server $server -Username $user -Password $sec | Out-Null

$result = New-Object System.Collections.ArrayList

function Add-Comp([string]$role, $objs) {
    foreach ($o in $objs) {
        $srv = $o.Server
        if (-not $srv) { $srv = $o.ServerName }
        if (-not $srv) { $srv = $o.FQDN }
        if (-not $srv) { continue }
        [void]$result.Add([pscustomobject]@{
            Role   = $role
            Server = [string]$srv
            Id     = $o.Id
            SiteId = $o.SiteId
        })
    }
}

# Connection Brokers (RAS 20+ : Get-RASBroker ; antérieur : Get-RASPA = Publishing Agents)
try { Add-Comp 'Broker' (Get-RASBroker) }
catch { try { Add-Comp 'Broker' (Get-RASPA) } catch { } }

# Secure Gateways
try { Add-Comp 'Gateway' (Get-RASGateway) } catch { }

# Enrollment Servers
try { Add-Comp 'EnrollmentServer' (Get-RASEnrollmentServer) }
catch {
    try {
        $st = Get-RASEnrollmentServerStatus
        Add-Comp 'EnrollmentServer' $st
    } catch { }
}

try { Remove-RASSession | Out-Null } catch { }

$result | ConvertTo-Json -Depth 4 -Compress
";

    public async Task<List<RasComponent>> DiscoverAsync(string server, string user, SecureString password)
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"ras_discover_{Guid.NewGuid():N}.ps1");
        await File.WriteAllTextAsync(scriptPath, Script, new UTF8Encoding(false));

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            psi.EnvironmentVariables["RAS_SERVER"] = server;
            psi.EnvironmentVariables["RAS_USER"] = user;
            psi.EnvironmentVariables["RAS_PASS"] = ToPlain(password);

            using var proc = new Process { StartInfo = psi };
            proc.Start();

            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (proc.ExitCode != 0)
                throw new InvalidOperationException(
                    $"Découverte RAS échouée (exit {proc.ExitCode}).\n{stderr.Trim()}");

            return ParseJson(stdout);
        }
        finally
        {
            try { File.Delete(scriptPath); } catch { }
        }
    }

    private static List<RasComponent> ParseJson(string stdout)
    {
        var list = new List<RasComponent>();
        stdout = stdout.Trim();
        if (string.IsNullOrEmpty(stdout)) return list;

        // ConvertTo-Json renvoie un objet seul si un seul élément : on normalise en tableau.
        if (!stdout.StartsWith("[")) stdout = "[" + stdout + "]";

        using var doc = JsonDocument.Parse(stdout);
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            var c = new RasComponent
            {
                Role = GetStr(el, "Role"),
                Server = GetStr(el, "Server")
            };
            if (el.TryGetProperty("Id", out var id) && id.ValueKind == JsonValueKind.Number)
                c.Id = id.GetInt32();
            if (el.TryGetProperty("SiteId", out var sid) && sid.ValueKind == JsonValueKind.Number)
                c.SiteId = sid.GetInt32();

            if (!string.IsNullOrWhiteSpace(c.Server))
                list.Add(c);
        }
        return list;
    }

    private static string GetStr(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";

    private static string ToPlain(SecureString s)
    {
        var ptr = System.Runtime.InteropServices.Marshal.SecureStringToGlobalAllocUnicode(s);
        try { return System.Runtime.InteropServices.Marshal.PtrToStringUni(ptr) ?? ""; }
        finally { System.Runtime.InteropServices.Marshal.ZeroFreeGlobalAllocUnicode(ptr); }
    }
}
