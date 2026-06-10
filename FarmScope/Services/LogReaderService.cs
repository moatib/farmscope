using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using RASLogAggregator.Models;

namespace RASLogAggregator.Services;

/// <summary>
/// Lit les fichiers de log RAS sur les partages admin (\\serveur\C$\ProgramData\Parallels\RASLogs)
/// et les transforme en entrées agrégées.
/// </summary>
public class LogReaderService
{
    private readonly AppConfig _cfg;
    private readonly Regex _tsRegex;
    private readonly Regex _sevRegex;
    private readonly Regex _lineStartRegex;
    private readonly Encoding _enc;

    // Noms et IP identifiant la machine locale (calculés une fois à la construction).
    private readonly HashSet<string> _localNames;
    private readonly HashSet<string> _localIps;
    private readonly bool _forceLocal;

    public LogReaderService(AppConfig cfg)
    {
        _cfg = cfg;
        _tsRegex = new Regex(cfg.TimestampRegex, RegexOptions.Compiled);
        _sevRegex = new Regex(cfg.SeverityRegex, RegexOptions.Compiled | RegexOptions.IgnoreCase);
        _lineStartRegex = new Regex(cfg.LineStartRegex, RegexOptions.Compiled);
        _enc = cfg.GetEncoding();
        _forceLocal = cfg.ForceLocal;
        (_localNames, _localIps) = BuildLocalIdentity(cfg.ExtraLocalNames);
    }

    public record FileResult(string Server, string Role, string File, string UncPath,
                             bool Ok, string? Error, int EntryCount);

    private static (HashSet<string> names, HashSet<string> ips) BuildLocalIdentity(string[] extra)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "localhost", "127.0.0.1", "::1", "." };
        var ips = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "127.0.0.1", "::1" };
        foreach (var e in extra ?? Array.Empty<string>())
            if (!string.IsNullOrWhiteSpace(e)) names.Add(e.Trim());
        try
        {
            names.Add(Environment.MachineName);
            var host = Dns.GetHostName();
            names.Add(host);
            var entry = Dns.GetHostEntry(host);
            if (!string.IsNullOrEmpty(entry.HostName))
            {
                names.Add(entry.HostName);
                names.Add(entry.HostName.Split('.')[0]);
            }
            foreach (var a in entry.Aliases) names.Add(a);
            foreach (var ip in entry.AddressList) ips.Add(ip.ToString());
        }
        catch { /* best effort */ }
        return (names, ips);
    }

    /// <summary>
    /// Vrai si le serveur désigne la machine locale : par nom (FQDN, hostname court, alias,
    /// noms épinglés) ou par IP (le nom est résolu et comparé aux IP locales — couvre les
    /// CNAME/FQDN qui diffèrent du hostname Windows). Forçable via config (ForceLocal).
    /// </summary>
    public bool IsLocal(string server)
    {
        if (_forceLocal) return true;
        if (string.IsNullOrWhiteSpace(server)) return false;
        if (_localNames.Contains(server)) return true;
        if (_localNames.Contains(server.Split('.')[0])) return true;
        try
        {
            foreach (var a in Dns.GetHostAddresses(server))
            {
                if (IPAddress.IsLoopback(a)) return true;
                if (_localIps.Contains(a.ToString())) return true;
            }
        }
        catch { /* résolution impossible : on considère distant */ }
        return false;
    }

    /// <summary>
    /// Chemin d'accès à un fichier de log : local direct si le composant est sur cette
    /// machine (évite le loopback UNC bloqué par Windows), sinon partage admin UNC.
    /// </summary>
    public string BuildPath(string server, string file)
    {
        if (IsLocal(server))
        {
            var drive = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";
            return Path.Combine(drive + "\\", _cfg.LogSubPath, file);
        }
        return $@"\\{server}\{_cfg.AdminShare}\{_cfg.LogSubPath}\{file}";
    }

    /// <summary>
    /// Lit l'ensemble des logs pour les composants fournis.
    /// <paramref name="report"/> reçoit le statut de chaque fichier (succès / erreur d'accès).
    /// <paramref name="progress"/> reçoit le fichier en cours (affichage live).
    /// Chaque fichier a un timeout dur (8 s) : aucun accès ne peut figer l'UI.
    /// </summary>
    public List<LogEntry> ReadAll(IEnumerable<RasComponent> components, Action<FileResult>? report = null,
                                  IProgress<string>? progress = null, CancellationToken ct = default)
    {
        const int perFileTimeoutMs = 8000;
        var entries = new List<LogEntry>();
        var reachable = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        foreach (var comp in components)
        {
            ct.ThrowIfCancellationRequested();
            if (!_cfg.RoleLogFiles.TryGetValue(comp.Role, out var files)) continue;

            // Machine locale : accès direct, pas de sonde réseau ni d'UNC.
            // Sinon, sonde TCP 445 (2,5 s max) une fois par serveur : un hôte injoignable
            // (Gateway en DMZ, firewall bloquant SMB…) est sauté au lieu de bloquer.
            if (!reachable.TryGetValue(comp.Server, out var ok))
            {
                ok = IsLocal(comp.Server) || IsSmbReachable(comp.Server, 445, 2500);
                reachable[comp.Server] = ok;
            }
            if (!ok)
            {
                foreach (var file in files)
                    report?.Invoke(new FileResult(comp.Server, comp.Role, file,
                        BuildPath(comp.Server, file), false, Loc.T("err_unreachable"), 0));
                continue;
            }

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                var path = BuildPath(comp.Server, file);
                progress?.Report(Loc.F("st_reading_file", comp.Server, file));

                // Lecture dans une tâche bornée par un timeout : un accès bloqué
                // (loopback UNC, fichier verrouillé…) ne fige plus l'application.
                var work = Task.Run<(bool ok, string? err, List<LogEntry> e)>(() =>
                {
                    try
                    {
                        if (!File.Exists(path)) return (true, "absent", new List<LogEntry>());
                        var text = ReadTailText(path, _cfg.MaxLinesPerFile, _enc);
                        return (true, null, Parse(text, comp.Server, comp.Role, file).ToList());
                    }
                    catch (Exception ex) { return (false, ex.Message, new List<LogEntry>()); }
                });

                if (!work.Wait(perFileTimeoutMs))
                {
                    report?.Invoke(new FileResult(comp.Server, comp.Role, file, path, false,
                        Loc.T("err_timeout"), 0));
                    continue;
                }

                var r = work.Result;
                if (r.err == "absent")
                    report?.Invoke(new FileResult(comp.Server, comp.Role, file, path, true, "absent", 0));
                else if (!r.ok)
                    report?.Invoke(new FileResult(comp.Server, comp.Role, file, path, false, r.err, 0));
                else
                {
                    entries.AddRange(r.e);
                    report?.Invoke(new FileResult(comp.Server, comp.Role, file, path, true, null, r.e.Count));
                }
            }
        }

        // Tri global par horodatage (les entrées sans timestamp parsé vont à la fin).
        entries.Sort((a, b) =>
        {
            if (a.Timestamp.HasValue && b.Timestamp.HasValue) return a.Timestamp.Value.CompareTo(b.Timestamp.Value);
            if (a.Timestamp.HasValue) return -1;
            if (b.Timestamp.HasValue) return 1;
            return 0;
        });

        return entries;
    }

    /// <summary>Teste rapidement la joignabilité SMB d'un hôte (connexion TCP avec timeout).</summary>
    private static bool IsSmbReachable(string host, int port, int timeoutMs)
    {
        try
        {
            using var client = new TcpClient();
            var ar = client.BeginConnect(host, port, null, null);
            if (!ar.AsyncWaitHandle.WaitOne(timeoutMs)) return false;
            client.EndConnect(ar);
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Lit la fin d'un fichier (les <paramref name="maxLines"/> dernières lignes) en lisant
    /// par blocs depuis la fin, pour éviter de charger des logs volumineux via SMB.
    /// L'encodage est auto-détecté via le BOM (les logs RAS sont souvent en UTF-16).
    /// FileShare.ReadWrite permet de lire un fichier ouvert en écriture par le service RAS.
    /// </summary>
    private static string ReadTailText(string path, int maxLines, Encoding fallbackEnc)
    {
        const int block = 64 * 1024;
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                                      FileShare.ReadWrite | FileShare.Delete);

        var enc = DetectEncoding(fs, fallbackEnc);

        long pos = fs.Length;
        if (pos == 0) return string.Empty;

        var chunks = new LinkedList<byte[]>();
        int newlines = 0;

        while (pos > 0 && newlines <= maxLines)
        {
            int toRead = (int)Math.Min(block, pos);
            pos -= toRead;
            var buf = new byte[toRead];
            fs.Seek(pos, SeekOrigin.Begin);
            ReadExactly(fs, buf, toRead);
            chunks.AddFirst(buf);
            for (int i = 0; i < toRead; i++)
                if (buf[i] == (byte)'\n') newlines++;
        }

        using var ms = new MemoryStream();
        foreach (var c in chunks) ms.Write(c, 0, c.Length);
        var text = enc.GetString(ms.ToArray()).TrimStart('\uFEFF');

        var lines = text.Split('\n');
        if (lines.Length > maxLines)
            lines = lines.Skip(lines.Length - maxLines).ToArray();
        return string.Join("\n", lines);
    }

    /// <summary>Détecte l'encodage d'après le BOM en tête de fichier (UTF-8, UTF-16 LE/BE).</summary>
    private static Encoding DetectEncoding(FileStream fs, Encoding fallback)
    {
        var bom = new byte[4];
        fs.Seek(0, SeekOrigin.Begin);
        int n = fs.Read(bom, 0, 4);
        fs.Seek(0, SeekOrigin.Begin);

        if (n >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF) return new UTF8Encoding(false);
        if (n >= 2 && bom[0] == 0xFF && bom[1] == 0xFE) return Encoding.Unicode;          // UTF-16 LE
        if (n >= 2 && bom[0] == 0xFE && bom[1] == 0xFF) return Encoding.BigEndianUnicode;  // UTF-16 BE
        return fallback;
    }

    private static void ReadExactly(Stream s, byte[] buffer, int count)
    {
        int read = 0;
        while (read < count)
        {
            int r = s.Read(buffer, read, count - read);
            if (r == 0) break;
            read += r;
        }
    }

    /// <summary>
    /// Parse le texte d'un log. Une nouvelle entrée commence à chaque ligne matchant
    /// <c>LineStartRegex</c> (par défaut le crochet RAS « [I … », « [E … »). La sévérité
    /// est tirée de la lettre du crochet (groupe "sev") si présente, sinon des mots-clés.
    /// L'horodatage est cherché n'importe où dans la ligne. Les lignes de continuation
    /// (stack traces…) sont rattachées à l'entrée courante.
    /// </summary>
    private IEnumerable<LogEntry> Parse(string text, string server, string role, string file)
    {
        LogEntry? cur = null;
        int ln = 0;

        foreach (var raw in text.Split('\n'))
        {
            ln++;
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 && cur == null) continue;

            var start = _lineStartRegex.Match(line);
            if (start.Success)
            {
                if (cur != null) yield return cur;

                cur = new LogEntry
                {
                    Server = server, Role = role, LogFile = file, LineNumber = ln, Message = line
                };

                // Sévérité : lettre du crochet si capturée, sinon recherche par mot-clé.
                cur.Severity = start.Groups["sev"].Success
                    ? Normalize(start.Groups["sev"].Value)
                    : DetectSeverity(line);

                // Horodatage : n'importe où dans la ligne.
                var tm = _tsRegex.Match(line);
                if (tm.Success)
                {
                    cur.RawTimestamp = tm.Groups["ts"].Value;
                    cur.Timestamp = TryParseTs(tm.Groups["ts"].Value);
                }
            }
            else if (cur != null)
            {
                cur.Message += "\n" + line;
            }
            else
            {
                cur = new LogEntry
                {
                    Server = server, Role = role, LogFile = file, LineNumber = ln,
                    Severity = DetectSeverity(line), Message = line
                };
            }
        }

        if (cur != null) yield return cur;
    }

    private string DetectSeverity(string line)
    {
        var m = _sevRegex.Match(line);
        return m.Success ? Normalize(m.Groups["sev"].Value) : "INFO";
    }

    private static string Normalize(string sev) => sev.ToUpperInvariant() switch
    {
        "F" or "FATAL" or "C" or "CRITICAL" or "CRIT" or "E" or "ERROR" or "ERR" => "ERROR",
        "W" or "WARNING" or "WARN" => "WARN",
        "N" or "NOTICE" or "I" or "INFO" => "INFO",
        "D" or "DEBUG" => "DEBUG",
        "T" or "V" or "TRACE" or "VERBOSE" => "TRACE",
        _ => "OTHER"
    };

    private DateTime? TryParseTs(string s)
    {
        s = s.Trim();
        if (DateTime.TryParseExact(s, _cfg.TimestampFormats, CultureInfo.InvariantCulture,
                                   DateTimeStyles.None, out var dt)) return dt;

        // Certains logs utilisent ':' avant les millisecondes (HH:mm:ss:fff) -> on normalise en '.'.
        var alt = Regex.Replace(s, @"(\d{2}:\d{2}:\d{2}):(\d{1,3})$", "$1.$2");
        if (DateTime.TryParseExact(alt, _cfg.TimestampFormats, CultureInfo.InvariantCulture,
                                   DateTimeStyles.None, out dt)) return dt;

        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt)) return dt;
        return null;
    }
}
