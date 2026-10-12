using System.Collections.ObjectModel;
using Punk.BattleNet;
using Punk.Gog;
using Punk.Config;
using Punk.Launch;
using Punk.Steam;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Punk.Ui;

public enum MessageKind { Info, Success, Warning, Error }

/// <summary>Splash screen with a progress bar, then the game list; refreshes itself on a timer.</summary>
public sealed class MainWindow : Window
{
    private const int TickMs = 50;

    private readonly IApplication _app;
    private AppConfig _config;
    private Symbols _sym;
    private Scheme _scheme;

    // state
    private string? _steamRoot;
    private string? _bnetClient;
    private string? _gogClient;
    private List<Game> _allGames = [];
    private List<Game> _visible = [];
    private readonly Dictionary<string, DateTime> _newUntil = [];
    private readonly HashSet<string> _stale = [];
    private readonly ObservableCollection<string> _rows = [];
    private bool _firstLoadDone;
    private bool _scanning;
    private bool _dialogOpen;
    private MessageKind _messageKind = MessageKind.Info;
    private readonly DateTime _started = DateTime.UtcNow;
    private DateTime _lastRefresh = DateTime.UtcNow;
    private long _tick;

    // splash state
    private bool _splashActive = true;
    private double _targetFraction;
    private double _shownFraction;
    private ScanResult? _pending;

    // views
    private readonly View _splash = new() { Width = Dim.Fill(), Height = Dim.Fill() };
    private readonly View _main = new() { Width = Dim.Fill(), Height = Dim.Fill(), Visible = false };
    private readonly ProgressBar _bar = new();
    private readonly Label _splashStatus = new();
    private readonly Label _splashSpinner = new();
    private readonly Label _title = new();
    private readonly Label _refreshInfo = new();
    private readonly Label _sessionInfo = new();
    private readonly GameSessionTracker _tracker = new();
    private GameSession? _shownSession;     // the session the header currently describes
    private string? _launchStatus;          // launcher progress before the session exists (e.g. Battle.net cold start)
    private DateTime _sessionInfoUntil;     // how long to keep a finished-session line visible
    private readonly TextField _search = new();
    private readonly Label _searchIcon = new();
    private readonly FrameView _listFrame = new();
    private readonly FrameView _detailsFrame = new();
    private readonly ListView _list = new();
    private readonly Label _detailsTitle = new();
    private readonly Label _detailsBody = new();
    private readonly Label _message = new();
    private readonly StatusBar _hints = new();
    private readonly Label[] _banner = [new(), new()];
    private readonly Label _subtitle = new();

    public MainWindow(IApplication app, AppConfig config, string? configWarning)
    {
        _app = app;
        _config = config;
        _sym = Symbols.For(config);
        Theme.Use(config.Preset, config.AccentColor);
        _scheme = Theme.CreateScheme();

        Title = "";
        BorderStyle = LineStyle.None;
        Width = Dim.Fill();
        Height = Dim.Fill();

        BuildSplash();
        BuildMain();
        _main.CanFocus = true;
        _splash.CanFocus = false;
        Add(_splash, _main);
        ApplyTheme();

        _list.Source = new ListWrapper<string>(_rows);
        _list.ValueChanged += (_, _) => UpdateDetails();
        _search.ValueChanged += (_, _) => ApplyFilter();

        _app.Keyboard.KeyDown += OnAppKeyDown;
        _tracker.Changed += s => _app.Invoke(() => OnSessionChanged(s));
        _app.AddTimeout(TimeSpan.FromMilliseconds(TickMs), Tick);

        if (configWarning is not null) SetMessage($"{_sym.Warn} {configWarning}", MessageKind.Warning);
        _ = RefreshAsync(splash: true);
    }

    // ---------------------------------------------------------------- layout

    private void BuildSplash()
    {
        string[] art = [" █▀█ █ █ █▄ █ █▄▀ ", " █▀▀ █▄█ █ ▀█ █ █ "];
        for (int i = 0; i < 2; i++)
        {
            _banner[i].Text = art[i];
            _banner[i].X = Pos.Center();
            _banner[i].Y = Pos.Center() - 5 + i;
            _splash.Add(_banner[i]);
        }

        _subtitle.Text = "Game launcher";
        _subtitle.X = Pos.Center();
        _subtitle.Y = Pos.Center() - 3;

        _bar.X = Pos.Center();
        _bar.Y = Pos.Center();
        _bar.Width = Dim.Percent(50);
        _bar.Height = 1;
        _bar.ProgressBarStyle = ProgressBarStyle.Continuous;

        _splashSpinner.X = Pos.Center() - 20;
        _splashSpinner.Y = Pos.Center() + 2;
        _splashSpinner.Width = 2;

        _splashStatus.X = Pos.Center() - 18;
        _splashStatus.Y = Pos.Center() + 2;
        _splashStatus.Width = 40;
        _splashStatus.Text = "Starting…";

        _splash.Add(_subtitle, _bar, _splashSpinner, _splashStatus);
    }

    private void BuildMain()
    {
        _title.X = 1;
        _title.Y = 0;
        _title.Text = $"PUNK  {_sym.Play}  Game library";

        _sessionInfo.X = Pos.Right(_title) + 4;
        _sessionInfo.Y = 0;
        _sessionInfo.Width = Dim.Fill(42);

        _refreshInfo.X = Pos.AnchorEnd(40);
        _refreshInfo.Y = 0;
        _refreshInfo.Width = 40;

        _searchIcon.X = 1;
        _searchIcon.Y = 1;
        _searchIcon.Text = _sym.Search;
        _search.X = 3;
        _search.Y = 1;
        _search.Width = Dim.Fill(1);

        _listFrame.X = 0;
        _listFrame.Y = 3;
        _listFrame.Width = Dim.Percent(58);
        _listFrame.Height = Dim.Fill(3);
        _listFrame.BorderStyle = LineStyle.Rounded;
        _listFrame.Title = "Games";

        _list.X = 0;
        _list.Y = 0;
        _list.Width = Dim.Fill();
        _list.Height = Dim.Fill();
        _list.CanFocus = false;
        _listFrame.Add(_list);

        _detailsFrame.X = Pos.Right(_listFrame);
        _detailsFrame.Y = 3;
        _detailsFrame.Width = Dim.Fill();
        _detailsFrame.Height = Dim.Fill(3);
        _detailsFrame.BorderStyle = LineStyle.Rounded;
        _detailsFrame.Title = "Details";

        _detailsTitle.X = 1;
        _detailsTitle.Y = 0;
        _detailsTitle.Width = Dim.Fill(1);
        _detailsBody.X = 1;
        _detailsBody.Y = 2;
        _detailsBody.Width = Dim.Fill(1);
        _detailsBody.Height = Dim.Fill();
        _detailsFrame.Add(_detailsTitle, _detailsBody);

        _message.X = 1;
        _message.Y = Pos.AnchorEnd(2);
        _message.Width = Dim.Fill(1);

        _hints.Add(
            new Shortcut { Key = Key.Enter, Title = $"{_sym.Play} Launch" },
            new Shortcut { Key = Key.F5, Title = $"{_sym.Refresh} Refresh" },
            new Shortcut { Key = Key.F3, Title = "Sort" },
            new Shortcut { Key = Key.F4, Title = "Theme" },
            new Shortcut { Key = Key.F2, Title = $"{_sym.Gear} Settings" },
            new Shortcut { Key = Key.Q.WithCtrl, Title = "Quit" });

        _main.Add(_title, _sessionInfo, _refreshInfo, _searchIcon, _search, _listFrame, _detailsFrame, _message, _hints);
    }

    private void ApplyTheme()
    {
        Theme.Use(_config.Preset, _config.AccentColor, (DateTime.UtcNow - _started).TotalSeconds);
        Color accent = Theme.Accent;
        _scheme = Theme.CreateScheme();
        SetScheme(_scheme);
        foreach (View v in new View[] { _splash, _main, _listFrame, _detailsFrame, _list, _search, _hints })
            v.SetScheme(_scheme);
        _listFrame.BorderStyle = Theme.Border;
        _detailsFrame.BorderStyle = Theme.Border;

        _title.SetScheme(Theme.Colored(accent));
        _subtitle.SetScheme(Theme.Colored(Theme.Current == UiPreset.Default ? Theme.Muted : Theme.Accent2));
        _bar.SetScheme(new Scheme { Normal = new Terminal.Gui.Drawing.Attribute(accent, Theme.Panel) });
        _splashSpinner.SetScheme(Theme.Colored(Theme.Accent3));
        _splashStatus.SetScheme(Theme.Colored(Theme.Muted));
        _searchIcon.SetScheme(Theme.Colored(Theme.Accent2));
        _detailsTitle.SetScheme(Theme.Colored(Theme.Accent3));
        _detailsBody.SetScheme(Theme.Colored(Theme.Text));
        _refreshInfo.SetScheme(Theme.Colored(Theme.Muted));
        _sessionInfo.SetScheme(Theme.Colored(Theme.Success));
        _banner[0].SetScheme(Theme.Colored(accent));
        _banner[1].SetScheme(Theme.Colored(Theme.Accent2));
        _message.SetScheme(Theme.Colored(MessageColor(_messageKind)));
        SetNeedsDraw();
    }

    private void CyclePreset()
    {
        UiPreset[] all = Enum.GetValues<UiPreset>();
        _config.Preset = all[((int)_config.Preset + 1) % all.Length];
        try { ConfigStore.Save(_config); } catch { /* preset still applies for this session */ }
        ApplyTheme();
        UpdateSessionInfo();
        SetMessage($"{_sym.Check} Theme: {_config.Preset}", MessageKind.Success);
    }

    // ---------------------------------------------------------------- scanning

    private async Task RefreshAsync(bool splash)
    {
        if (_scanning) return;
        _scanning = true;
        _lastRefresh = DateTime.UtcNow;

        Progress<ScanProgress> progress = new(p => _app.Invoke(() =>
        {
            if (!_splashActive) return;
            _targetFraction = p.Fraction;
            _splashStatus.Text = Format.Fit(p.Message, 40);
        }));

        ScanResult result;
        try
        {
            result = await GameScanner.ScanAsync(_config.Clone(), splash ? progress : null);
        }
        catch (Exception ex)
        {
            result = new ScanResult(_steamRoot, _bnetClient, _allGames, ex.Message);
        }

        _app.Invoke(() =>
        {
            _scanning = false;
            if (_splashActive) _pending = result;
            else ApplyResult(result);
        });
    }

    private void ApplyResult(ScanResult r)
    {
        _lastRefresh = DateTime.UtcNow;
        if (r.Error is not null)
        {
            SetMessage($"{_sym.Warn} {r.Error} Open settings ({_sym.Gear} F2) to set paths.", MessageKind.Error);
            _steamRoot = null; _bnetClient = null; _gogClient = null;
            foreach (Game g in _allGames) _stale.Add(g.Key);
            RebuildList();
            if (!_firstLoadDone) { _allGames = []; RebuildList(); _firstLoadDone = true; }
            return;
        }

        _steamRoot = r.SteamRoot;
        _bnetClient = r.BattleNetClient;
        _gogClient = r.GogClient;

        // Safeguard: if a launcher or library folder is unreachable (moved, unplugged drive, client reinstalling),
        // keep the games we knew about from it instead of letting the list silently empty out.
        HashSet<string> missing = new(r.MissingPaths ?? [], StringComparer.OrdinalIgnoreCase);
        bool steamGone = r.SteamRoot is null;
        bool bnetGone = _config.BattleNetEnabled && !r.BattleNetFound;
        bool gogGone = _config.GogEnabled && !r.GogFound;
        HashSet<string> found = [.. r.Games.Select(g => g.Key)];
        List<Game> retained = [.. _allGames.Where(g => !found.Contains(g.Key) &&
            ((g.Source == GameSource.Steam && steamGone) || (g.Source == GameSource.BattleNet && bnetGone) || (g.Source == GameSource.Gog && gogGone) || missing.Contains(g.LibraryPath)))];

        _stale.Clear();
        foreach (Game g in retained) _stale.Add(g.Key);
        List<Game> merged = [.. r.Games, .. retained];

        HashSet<string> oldIds = [.. _allGames.Select(g => g.Key)];
        HashSet<string> newIds = [.. merged.Select(g => g.Key)];

        List<string> notes = [];
        if (_firstLoadDone)
        {
            foreach (Game g in merged.Where(g => !oldIds.Contains(g.Key)))
            {
                _newUntil[g.Key] = DateTime.UtcNow.AddMinutes(2);
                notes.Add($"{_sym.Added} {g.Name}");
            }
            foreach (Game g in _allGames.Where(g => !newIds.Contains(g.Key)))
                notes.Add($"{_sym.Removed} {g.Name}");
        }

        _allGames = merged;
        _firstLoadDone = true;
        RebuildList();

        List<string> warnings = [.. r.Warnings ?? []];
        if (retained.Count > 0) warnings.Add($"{retained.Count} game(s) kept from the last scan (launcher or folder unreachable)");

        if (warnings.Count > 0) SetMessage($"{_sym.Warn} {string.Join("; ", warnings)}", MessageKind.Warning);
        else if (notes.Count > 0) SetMessage(string.Join("   ", notes), MessageKind.Info);
        else if (_message.Text.StartsWith(_sym.Warn)) SetMessage("", MessageKind.Info);
    }

    // ---------------------------------------------------------------- list

    private void ApplyFilter() => RebuildList();

    private void RebuildList()
    {
        string? keep = SelectedGame?.Key;
        string filter = _search.Text?.Trim() ?? "";

        _visible = [.. SteamScanner.Sort(_allGames, _config.SortBy)
            .Where(g => filter.Length == 0 || g.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase))];

        int width = Math.Max(30, (int)(_app.Screen.Width * 0.58) - 4);
        int nameW = Math.Max(12, width - 2 - 2 - 6 - 10 - 8 - 9);

        _rows.Clear();
        foreach (Game g in _visible)
        {
            string marker = _tracker.IsRunning(g) ? _sym.Play
                : _stale.Contains(g.Key) ? _sym.Warn
                : _newUntil.TryGetValue(g.Key, out DateTime until) && until > DateTime.UtcNow ? _sym.Added
                : g.LastPlayed is { } lp && DateTimeOffset.UtcNow - lp < TimeSpan.FromDays(7) ? _sym.Hot : _sym.Cold;
            _rows.Add($" {marker} {Format.Fit(g.Name, nameW)} {(g.Source switch { GameSource.Steam => "Steam", GameSource.Gog => "GOG", _ => "B.net" }),-5} {Format.Ago(g.LastPlayed),9} {Format.Playtime(g.Playtime),7} {Format.Size(g.SizeBytes),8}");
        }

        int idx = keep is { } id ? _visible.FindIndex(g => g.Key == id) : -1;
        if (_visible.Count > 0) _list.SelectedItem = idx >= 0 ? idx : 0;

        _list.Source = new ListWrapper<string>(_rows); // forces a full repaint; stale rows otherwise linger after a shrink
        if (_visible.Count > 0) _list.SelectedItem = idx >= 0 ? idx : 0;

        string sortLabel = _config.SortBy switch
        {
            SortMode.Name => "name",
            SortMode.Playtime => "playtime",
            SortMode.Size => "size",
            _ => "last played",
        };
        _listFrame.Title = (filter.Length > 0 ? $"Games  {_visible.Count}/{_allGames.Count}" : $"Games  {_allGames.Count}") + $"  · by {sortLabel}";
        UpdateDetails();
        _list.SetNeedsDraw();
        _listFrame.SetNeedsDraw();
    }

    private Game? SelectedGame
    {
        get
        {
            int i = _list.SelectedItem ?? -1;
            return i >= 0 && i < _visible.Count ? _visible[i] : null;
        }
    }

    private void UpdateDetails()
    {
        Game? g = SelectedGame;
        if (g is null)
        {
            _detailsTitle.Text = _allGames.Count == 0 ? "No games found" : "No match";
            _detailsBody.Text = "";
            return;
        }

        _detailsTitle.Text = g.Name;
        _detailsBody.Text =
            $"{_sym.Clock} Last played   {Format.Ago(g.LastPlayed)}{(g.LastPlayed is { } lp ? $"  ({lp.LocalDateTime:yyyy-MM-dd HH:mm})" : "")}\n" +
            $"{_sym.Hot} Playtime      {Format.Playtime(g.Playtime)}\n" +
            $"{_sym.Disk} Size          {Format.Size(g.SizeBytes)}\n" +
            $"# {(g.Source == GameSource.Steam ? "AppID" : "Product")}         {g.Id}\n" +
            $"  Platform      {(g.Source switch { GameSource.Steam => "Steam", GameSource.Gog => "GOG Galaxy", _ => "Battle.net" })}\n" +
            $"  Library       {g.LibraryPath}\n" +
            $"  Folder        {g.InstallDir}\n\n" +
            (_stale.Contains(g.Key) ? $"{_sym.Warn} Not reachable right now - showing last known data\n" : "") +
            $"{_sym.Play} Press Enter to launch";
    }

    // ---------------------------------------------------------------- messages / tick

    private void SetMessage(string text, MessageKind kind)
    {
        _messageKind = kind;
        _message.SetScheme(Theme.Colored(MessageColor(kind)));
        _message.Text = text;
    }

    private static Color MessageColor(MessageKind kind) => kind switch
    {
        MessageKind.Success => Theme.Success,
        MessageKind.Warning => Theme.Warning,
        MessageKind.Error => Theme.Danger,
        _ => Theme.Muted,
    };

    private bool Tick()
    {
        _tick++;

        if (_splashActive)
        {
            double step = 0.035;
            if (_shownFraction < _targetFraction) _shownFraction = Math.Min(_targetFraction, _shownFraction + step);
            _bar.Fraction = (float)_shownFraction;
            _splashSpinner.Text = _sym.Spinner[(int)(_tick % _sym.Spinner.Length)];

            if (_pending is { } r && _shownFraction >= 0.999)
            {
                _splashActive = false;
                _splash.Visible = false;
                _main.Visible = true;
                _search.SetFocus();
                ApplyResult(r);
                _pending = null;
            }
            else if (_pending is not null) _targetFraction = 1;
            return true;
        }

        if (_tick % (1000 / TickMs) == 0) PerSecond();
        return true;
    }

    private void PerSecond()
    {
        if (Theme.Current == UiPreset.Ambient) ApplyTheme(); // slow color drift
        UpdateSessionInfo();

        // Expire "new" flags.
        if (_newUntil.Count > 0 && _newUntil.Values.Any(t => t <= DateTime.UtcNow))
        {
            foreach (string k in _newUntil.Where(p => p.Value <= DateTime.UtcNow).Select(p => p.Key).ToList()) _newUntil.Remove(k);
            RebuildList();
        }

        double since = (DateTime.UtcNow - _lastRefresh).TotalSeconds;
        string info;
        if (_scanning) info = $"{_sym.Refresh} scanning…";
        else if (_config.RefreshIntervalSeconds > 0)
        {
            int next = Math.Max(0, _config.RefreshIntervalSeconds - (int)since);
            info = $"{_sym.Refresh} updated {(int)since}s ago · next in {next}s";
        }
        else info = $"{_sym.Refresh} updated {(int)since}s ago · auto refresh off";
        _refreshInfo.Text = info.PadLeft(40);

        if (!_scanning && !_dialogOpen && _config.RefreshIntervalSeconds > 0 && since >= _config.RefreshIntervalSeconds)
            _ = RefreshAsync(splash: false);
    }

    // ---------------------------------------------------------------- keys & actions

    private void OnAppKeyDown(object? sender, Key key)
    {
        if (key == Key.Q.WithCtrl)
        {
            _app.RequestStop();
            key.Handled = true;
            return;
        }

        if (_splashActive || _dialogOpen) return;

        void Move(int delta)
        {
            if (_visible.Count == 0) return;
            _list.SelectedItem = Math.Clamp((_list.SelectedItem ?? 0) + delta, 0, _visible.Count - 1);
        }

        if (key == Key.Enter) { LaunchSelected(); key.Handled = true; }
        else if (key == Key.CursorDown) { Move(1); key.Handled = true; }
        else if (key == Key.CursorUp) { Move(-1); key.Handled = true; }
        else if (key == Key.PageDown) { Move(10); key.Handled = true; }
        else if (key == Key.PageUp) { Move(-10); key.Handled = true; }
        else if (key == Key.F5 || key == Key.R.WithCtrl)
        {
            SetMessage($"{_sym.Refresh} Refreshing…", MessageKind.Info);
            _ = RefreshAsync(splash: false);
            key.Handled = true;
        }
        else if (key == Key.F3 || key == Key.O.WithCtrl)
        {
            _config.SortBy = _config.SortBy == SortMode.Name ? SortMode.LastPlayed : SortMode.Name;
            try { ConfigStore.Save(_config); } catch { /* sort order still applies for this session */ }
            RebuildList();
            key.Handled = true;
        }
        else if (key == Key.F4) { CyclePreset(); key.Handled = true; }
        else if (key == Key.F2 || key == Key.S.WithCtrl) { OpenSettings(); key.Handled = true; }
        else if (key == Key.Esc && (_search.Text?.Length ?? 0) > 0) { _search.Text = ""; key.Handled = true; }
    }

    private void LaunchSelected()
    {
        Game? g = SelectedGame;
        if (g is null) return;

        // Safeguard: make sure the game and its launcher are still where we last saw them.
        string? problem = ValidateLaunch(g);
        if (problem is not null)
        {
            SetMessage($"{_sym.Warn} {problem} Rescanning…", MessageKind.Warning);
            _ = RefreshAsync(splash: false);
            return;
        }

        if (_tracker.IsRunning(g))
        {
            SetMessage($"{_sym.Play} {g.Name} is already running.", MessageKind.Info);
            return;
        }

        // Launchers can block (Battle.net waits for a cold-started client), so launch off the UI thread.
        // The launcher only reports that the request was accepted; the session tracker then follows the
        // game process itself (Starting -> Playing -> Closed), the same way for every launcher.
        string? steamRoot = _steamRoot, client = _bnetClient, gogClient = _gogClient;
        SetMessage("", MessageKind.Info);
        ShowLaunchStatus($"Launching {g.Name}…");
        _ = Task.Run(() =>
        {
            Action<string> status = s => _app.Invoke(() => ShowLaunchStatus(s));
            string? err = g.Source switch
            {
                GameSource.Steam => SteamLauncher.Launch(g, steamRoot),
                GameSource.BattleNet => BattleNetLauncher.Launch(g, client, status),
                GameSource.Gog => GogLauncher.Launch(g, gogClient, status),
                _ => $"No launcher for {g.Source}.",
            };
            _app.Invoke(() =>
            {
                _launchStatus = null;
                if (err is null) _tracker.Track(g);
                else
                {
                    UpdateSessionInfo();
                    SetMessage($"{_sym.Warn} Could not launch {g.Name}: {err}", MessageKind.Error);
                }
            });
        });
    }

    // ---------------------------------------------------------------- game sessions

    private void ShowLaunchStatus(string text)
    {
        _launchStatus = text;
        UpdateSessionInfo();
    }

    private void OnSessionChanged(GameSession s)
    {
        _shownSession = s;
        switch (s.State)
        {
            case SessionState.Exited:
                _sessionInfoUntil = DateTime.UtcNow.AddMinutes(1);
                _ = RefreshAsync(splash: false); // pick up the new "last played" / playtime
                break;
            case SessionState.NotStarted:
                _sessionInfoUntil = DateTime.UtcNow;
                SetMessage($"{_sym.Warn} {s.Game.Name} did not start (no game process within {GameSessionTracker.StartTimeout.TotalMinutes:0} min). Check the launcher window.", MessageKind.Warning);
                break;
        }
        UpdateSessionInfo();
        RebuildList();
    }

    private void UpdateSessionInfo()
    {
        GameSession? s = _shownSession;
        Color color = Theme.Success;
        string text;

        if (_launchStatus is not null) { text = $"{_sym.Refresh} {_launchStatus}"; color = Theme.Accent; }
        else if (s is null) text = "";
        else switch (s.State)
        {
            case SessionState.Starting:
                text = $"{_sym.Spinner[(int)(_tick / 4 % _sym.Spinner.Length)]} Starting {s.Game.Name}…";
                color = Theme.Accent;
                break;
            case SessionState.Running:
                text = $"{_sym.Play} Playing {s.Game.Name} · {Format.Duration(s.Played)}";
                break;
            case SessionState.Exited when DateTime.UtcNow < _sessionInfoUntil:
                text = $"{_sym.Check} Closed {s.Game.Name} · played {Format.Duration(s.Played)}";
                color = Theme.Muted;
                break;
            default:
                text = "";
                break;
        }

        // Several games can run at once: mention the others.
        int others = _tracker.Active.Count(a => a.State == SessionState.Running && a != s);
        if (others > 0 && text.Length > 0) text += $"  (+{others} running)";

        _sessionInfo.SetScheme(Theme.Colored(color));
        _sessionInfo.Text = text;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tracker.Dispose();
        base.Dispose(disposing);
    }

    private string? ValidateLaunch(Game g)
    {
        if (_stale.Contains(g.Key)) return $"{g.Name} is not reachable (launcher or drive missing).";

        string folder = GameFolders.InstallFolder(g);
        if (!Directory.Exists(folder)) return $"{g.Name} folder no longer exists: {folder}.";

        if (g.Source == GameSource.BattleNet && (_bnetClient is null || !File.Exists(_bnetClient)))
        {
            _bnetClient = BattleNetScanner.FindClient(); // the client may have moved since the last scan
            if (_bnetClient is null) return "Battle.net client not found.";
        }
        if (g.Source == GameSource.Gog && (_gogClient is null || !File.Exists(_gogClient)))
        {
            _gogClient = GogScanner.FindClient(); // Galaxy may have moved since the last scan
            if (_gogClient is null) return "GOG Galaxy client not found.";
        }
        return null;
    }

    private void OpenSettings()
    {
        _dialogOpen = true;
        try
        {
            AppConfig? updated = SettingsDialog.Show(_app, _config, _scheme);
            if (updated is null) return;

            bool rescan = updated.SteamPathOverride != _config.SteamPathOverride
                || updated.HideSteamTools != _config.HideSteamTools
                || updated.BattleNetEnabled != _config.BattleNetEnabled
                || updated.GogEnabled != _config.GogEnabled
                || !updated.BattleNetExtraRoots.SequenceEqual(_config.BattleNetExtraRoots)
                || !updated.ExtraLibraryPaths.SequenceEqual(_config.ExtraLibraryPaths);

            _config = updated;
            _sym = Symbols.For(_config);
            ApplyTheme();
            _title.Text = $"PUNK  {_sym.Play}  Game library";
            _lastRefresh = DateTime.UtcNow;
            RebuildList();
            SetMessage($"{_sym.Check} Settings saved", MessageKind.Success);
            if (rescan) _ = RefreshAsync(splash: false);
        }
        finally
        {
            _dialogOpen = false;
            _search.SetFocus();
        }
    }
}
