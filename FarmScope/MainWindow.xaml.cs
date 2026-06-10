using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Microsoft.Win32;
using RASLogAggregator.Models;
using RASLogAggregator.Services;
using RASLogAggregator.Views;

namespace RASLogAggregator;

public partial class MainWindow : Window
{
    private readonly AppConfig _cfg = AppConfig.Load();
    private readonly RasDiscoveryService _discovery = new();
    private LogReaderService _reader = null!;

    private readonly List<LogEntry> _all = new();
    private ICollectionView? _view;

    private List<RasComponent> _components = new();
    private string _server = "", _user = "";
    private System.Security.SecureString? _password;
    private bool _useAltCreds;
    private string _shareUser = "", _sharePass = "";

    private readonly DispatcherTimer _timer = new();
    private bool _busy;
    private bool _uiReady;   // évite que les handlers tirent pendant InitializeComponent

    public MainWindow()
    {
        InitializeComponent();
        _reader = new LogReaderService(_cfg);
        _timer.Tick += async (_, _) => await RefreshLogsAsync();

        // État initial : langue + thème depuis les préférences
        LangCombo.SelectedIndex = Loc.Lang switch { "fr" => 0, "de" => 2, _ => 1 };
        ThemeBtn.Content = ThemeManager.Current == "Dark" ? "☀" : "🌙";
        ApplyLocalization();
        _uiReady = true;

        Loaded += async (_, _) => await PromptConnectAsync();
    }

    // ---------- Localisation / thème ----------

    private void ApplyLocalization()
    {
        Title = "FarmScope";
        SubtitleText.Text = "· " + Loc.T("app_subtitle");
        ConnectBtn.Content = Loc.T("btn_connect");
        RefreshBtn.Content = Loc.T("btn_refresh");
        ExportBtn.Content = Loc.T("btn_export");
        SearchLabel.Content = Loc.T("lbl_search");
        RoleLabel.Text = Loc.T("lbl_role");
        ServerLabel.Text = Loc.T("lbl_server");
        FileLabel.Text = Loc.T("lbl_file");
        ThemeBtn.ToolTip = Loc.T("tip_theme");
        AboutBtn.ToolTip = Loc.T("tip_about");

        ColTime.Header = Loc.T("col_time");
        ColSev.Header = Loc.T("col_sev");
        ColRole.Header = Loc.T("col_role");
        ColServer.Header = Loc.T("col_server");
        ColFile.Header = Loc.T("col_file");
        ColMsg.Header = Loc.T("col_msg");

        if (!_busy) Status(Loc.T("st_ready"));
        UpdateCounts(); // rafraîchit les libellés des pills (dont « Autre »)

        // Les sentinelles "(tous)" des combos sont repositionnées au prochain peuplement ;
        // si déjà peuplées, on remplace juste l'élément 0.
        ReplaceSentinel(RoleFilter);
        ReplaceSentinel(ServerFilter);
        ReplaceSentinel(FileFilter);
    }

    private static void ReplaceSentinel(ComboBox cb)
    {
        if (cb.Items.Count > 0)
        {
            int sel = cb.SelectedIndex;
            cb.Items[0] = Loc.T("filter_all");
            cb.SelectedIndex = sel < 0 ? 0 : sel;
        }
    }

    private void ThemeBtn_Click(object sender, RoutedEventArgs e)
    {
        var t = ThemeManager.Toggle();
        ThemeBtn.Content = t == "Dark" ? "☀" : "🌙";
        UserSettings.Current.Theme = t;
        UserSettings.Current.Save();
    }

    private void LangCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady) return;
        var tag = (LangCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "en";
        Loc.Lang = tag;
        UserSettings.Current.Language = tag;
        UserSettings.Current.Save();
        ApplyLocalization();
    }

    private void AboutBtn_Click(object sender, RoutedEventArgs e)
    {
        new AboutWindow { Owner = this }.ShowDialog();
    }

    // ---------- Connexion / découverte ----------

    private async void Connect_Click(object sender, RoutedEventArgs e) => await PromptConnectAsync();

    private async Task PromptConnectAsync()
    {
        var dlg = new ConnectionWindow { Owner = this };
        if (dlg.ShowDialog() != true) return;

        _server = dlg.Server;
        _user = dlg.User;
        _password = dlg.Password;
        _useAltCreds = dlg.UseAltShareCreds;
        _shareUser = dlg.ShareUser;
        _sharePass = dlg.SharePassword;

        await DiscoverAsync();
    }

    private async Task DiscoverAsync()
    {
        if (_busy || _password == null) return;
        SetBusy(true, Loc.F("st_discovering", _server));

        try
        {
            _components = await _discovery.DiscoverAsync(_server, _user, _password);
            if (_components.Count == 0)
            {
                Status(Loc.T("st_no_components"));
                SetBusy(false);
                return;
            }

            PopulateFilters();
            RefreshBtn.IsEnabled = true;
            SetBusy(false); // libère le drapeau « occupé » : sinon RefreshLogsAsync sort aussitôt
            Status(Loc.F("st_found", _components.Count));
            await RefreshLogsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, Loc.T("mb_disc_title"),
                            MessageBoxButton.OK, MessageBoxImage.Error);
            Status(Loc.T("st_disc_fail"));
            SetBusy(false);
        }
    }

    // ---------- Lecture des logs ----------

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshLogsAsync();

    private async Task RefreshLogsAsync()
    {
        if (_busy || _components.Count == 0) return;
        SetBusy(true, Loc.T("st_reading"));

        var errors = new List<string>();
        int filesOk = 0, filesErr = 0;

        try
        {
            var progress = new Progress<string>(s => StatusText.Text = s);

            var entries = await Task.Run(() =>
            {
                using IDisposable? mounts = MountSharesIfNeeded();
                void Report(LogReaderService.FileResult r)
                {
                    if (!r.Ok) { filesErr++; errors.Add($"{r.Server} / {r.File} : {r.Error}"); }
                    else filesOk++;
                }
                return _reader.ReadAll(_components, Report, progress);
            });

            _all.Clear();
            _all.AddRange(entries);

            if (_view == null)
            {
                _view = CollectionViewSource.GetDefaultView(_all);
                _view.Filter = FilterPredicate;
                LogGrid.ItemsSource = _view;
            }
            else
            {
                _view.Refresh();
            }

            PopulateFileFilter();
            UpdateCounts();

            var errSuffix = filesErr > 0 ? Loc.F("st_done_err", filesErr) : "";
            Status(Loc.F("st_done", _all.Count, filesOk) + errSuffix);

            if (errors.Count > 0)
                CountText.ToolTip = string.Join("\n", errors.Take(30));
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, Loc.T("mb_read_title"),
                            MessageBoxButton.OK, MessageBoxImage.Error);
            Status(Loc.T("st_read_fail"));
        }
        finally
        {
            SetBusy(false);
        }
    }

    private IDisposable? MountSharesIfNeeded()
    {
        if (!_useAltCreds) return null;
        // Monte chaque \\serveur\C$ avec les identifiants alternatifs.
        var mounts = new List<NetworkConnection>();
        foreach (var srv in _components.Select(c => c.Server).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (_reader.IsLocal(srv)) continue; // local : accès direct, pas de montage
            var unc = $@"\\{srv}\{_cfg.AdminShare}";
            try { mounts.Add(new NetworkConnection(unc, _shareUser, _sharePass)); }
            catch { /* la lecture remontera l'erreur d'accès par fichier */ }
        }
        return new MultiDispose(mounts);
    }

    // ---------- Filtrage ----------

    private void PopulateFilters()
    {
        var roles = _components.Select(c => c.Role).Distinct().OrderBy(r => r).ToList();
        RoleFilter.Items.Clear();
        RoleFilter.Items.Add(Loc.T("filter_all"));
        foreach (var r in roles) RoleFilter.Items.Add(r);
        RoleFilter.SelectedIndex = 0;

        var servers = _components.Select(c => c.Server).Distinct(StringComparer.OrdinalIgnoreCase)
                                 .OrderBy(s => s).ToList();
        ServerFilter.Items.Clear();
        ServerFilter.Items.Add(Loc.T("filter_all"));
        foreach (var s in servers) ServerFilter.Items.Add(s);
        ServerFilter.SelectedIndex = 0;
    }

    /// <summary>Peuple le filtre Fichier d'après les logs réellement chargés, en gardant la sélection.</summary>
    private void PopulateFileFilter()
    {
        var current = FileFilter.SelectedItem as string;
        var files = _all.Select(e => e.LogFile).Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(f => f).ToList();

        FileFilter.Items.Clear();
        FileFilter.Items.Add(Loc.T("filter_all"));
        foreach (var f in files) FileFilter.Items.Add(f);

        if (current != null && FileFilter.Items.Contains(current))
            FileFilter.SelectedItem = current;
        else
            FileFilter.SelectedIndex = 0;
    }

    private void Filter_Changed(object sender, RoutedEventArgs e) => _view?.Refresh();
    private void Filter_Changed(object sender, TextChangedEventArgs e) => _view?.Refresh();
    private void Filter_Changed(object sender, SelectionChangedEventArgs e) => _view?.Refresh();

    private bool FilterPredicate(object obj)
    {
        if (obj is not LogEntry le) return false;

        // Sévérité
        bool sevOk = le.Severity switch
        {
            "ERROR" => ChkError.IsChecked == true,
            "WARN" => ChkWarn.IsChecked == true,
            "INFO" => ChkInfo.IsChecked == true,
            "DEBUG" or "TRACE" => ChkDebug.IsChecked == true,
            _ => ChkOther.IsChecked == true
        };
        if (!sevOk) return false;

        // Rôle
        if (RoleFilter.SelectedIndex > 0 &&
            !string.Equals(le.Role, RoleFilter.SelectedItem as string, StringComparison.Ordinal))
            return false;

        // Serveur
        if (ServerFilter.SelectedIndex > 0 &&
            !string.Equals(le.Server, ServerFilter.SelectedItem as string, StringComparison.OrdinalIgnoreCase))
            return false;

        // Fichier
        if (FileFilter.SelectedIndex > 0 &&
            !string.Equals(le.LogFile, FileFilter.SelectedItem as string, StringComparison.OrdinalIgnoreCase))
            return false;

        // Recherche plein texte
        var q = SearchBox.Text;
        if (!string.IsNullOrWhiteSpace(q) &&
            le.Message.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0)
            return false;

        return true;
    }

    private void LogGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LogGrid.SelectedItem is LogEntry le)
            DetailBox.Text =
                $"{le.Server}  |  {le.Role}  |  {le.LogFile}  (#{le.LineNumber})\n" +
                $"{le.TimeDisplay}  [{le.Severity}]\n\n{le.Message}";
    }

    // ---------- Auto-refresh ----------

    private void AutoRefresh_Changed(object sender, RoutedEventArgs e)
    {
        if (AutoRefreshCheck.IsChecked == true)
        {
            var tag = (IntervalCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "30";
            _timer.Interval = TimeSpan.FromSeconds(int.TryParse(tag, out var s) ? s : 30);
            _timer.Start();
        }
        else _timer.Stop();
    }

    // ---------- Export CSV ----------

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_view == null) return;
        var dlg = new SaveFileDialog
        {
            Filter = "CSV (*.csv)|*.csv",
            FileName = $"farmscope_logs_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
        };
        if (dlg.ShowDialog() != true) return;

        var sb = new StringBuilder();
        sb.AppendLine("Timestamp;Severity;Role;Server;LogFile;Message");
        foreach (LogEntry le in _view)
        {
            var msg = le.Message.Replace("\r", " ").Replace("\n", " ").Replace("\"", "\"\"");
            sb.AppendLine($"{le.TimeDisplay};{le.Severity};{le.Role};{le.Server};{le.LogFile};\"{msg}\"");
        }
        File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));
        Status(Loc.F("st_export", dlg.FileName));
    }

    // ---------- Utilitaires UI ----------

    private void UpdateCounts()
    {
        int shown = _view?.Cast<object>().Count() ?? 0;
        CountText.Text = Loc.F("st_count", shown, _all.Count);

        // Compteurs live sur les pills de sévérité (sur l'ensemble chargé).
        int err = 0, warn = 0, info = 0, dbg = 0, other = 0;
        foreach (var e in _all)
        {
            switch (e.Severity)
            {
                case "ERROR": err++; break;
                case "WARN": warn++; break;
                case "INFO": info++; break;
                case "DEBUG" or "TRACE": dbg++; break;
                default: other++; break;
            }
        }
        var oth = Loc.T("sev_other");
        ChkError.Content = err > 0 ? $"Error · {err}" : "Error";
        ChkWarn.Content = warn > 0 ? $"Warn · {warn}" : "Warn";
        ChkInfo.Content = info > 0 ? $"Info · {info}" : "Info";
        ChkDebug.Content = dbg > 0 ? $"Debug · {dbg}" : "Debug";
        ChkOther.Content = other > 0 ? $"{oth} · {other}" : oth;
    }

    private void SetBusy(bool busy, string? status = null)
    {
        _busy = busy;
        Cursor = busy ? System.Windows.Input.Cursors.Wait : System.Windows.Input.Cursors.Arrow;
        RefreshBtn.IsEnabled = !busy && _components.Count > 0;
        ConnectBtn.IsEnabled = !busy;
        if (status != null) Status(status);
        if (!busy) UpdateCounts();
    }

    private void Status(string s) => StatusText.Text = s;

    /// <summary>Libère plusieurs IDisposable d'un coup (montages SMB).</summary>
    private sealed class MultiDispose : IDisposable
    {
        private readonly IEnumerable<IDisposable> _items;
        public MultiDispose(IEnumerable<IDisposable> items) => _items = items;
        public void Dispose() { foreach (var i in _items) i.Dispose(); }
    }
}
