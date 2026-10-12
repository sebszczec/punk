using Punk.Config;

namespace Punk.Steam;

public readonly record struct ScanProgress(double Fraction, string Message);

/// <param name="Warnings">Human-readable problems (moved/missing folders) that did not stop the scan.</param>
/// <param name="MissingPaths">Library/root folders that were expected but unreachable (e.g. unplugged drive).</param>
public sealed record ScanResult(
    string? SteamRoot, string? BattleNetClient, IReadOnlyList<Game> Games, string? Error,
    IReadOnlyList<string>? Warnings = null, IReadOnlyList<string>? MissingPaths = null, bool BattleNetFound = false,
    bool GogFound = false, string? GogClient = null);

public static class SteamScanner
{
    private record struct LocalStats(DateTimeOffset? LastPlayed, TimeSpan Playtime);

    // Steam components that show up as "installed apps" but are not games.
    private static readonly string[] ToolNamePrefixes = ["Steamworks Common Redistributables", "Proton", "Steam Linux Runtime", "Steam Play"];

    public static Task<ScanResult> ScanAsync(AppConfig config, IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
        => Task.Run(() => Scan(config, progress, ct), ct);

    public static ScanResult Scan(AppConfig config, IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
    {
        progress?.Report(new(0, "Locating Steam…"));
        List<string> warnings = [], missing = [];
        string? root = SteamLocator.Find(config.SteamPathOverride);
        if (root is null && !string.IsNullOrWhiteSpace(config.SteamPathOverride))
        {
            // The configured path moved or vanished: fall back to auto-detection instead of giving up.
            warnings.Add($"Steam path override not found ({config.SteamPathOverride}); using auto-detected Steam.");
            root = SteamLocator.Find(null);
        }
        if (root is null) return new(null, null, [], "Steam installation not found.");

        List<string> libraries = FindLibraries(root, config, missing);
        warnings.AddRange(missing.Select(m => $"Steam library unreachable: {m}"));
        progress?.Report(new(0.05, $"Found {libraries.Count} librar{(libraries.Count == 1 ? "y" : "ies")}"));

        List<(string Library, string Manifest)> manifests = [];
        foreach (string lib in libraries)
        {
            ct.ThrowIfCancellationRequested();
            string apps = Path.Combine(lib, "steamapps");
            if (!Directory.Exists(apps)) continue;
            foreach (string file in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
                manifests.Add((lib, file));
        }

        progress?.Report(new(0.1, "Reading play statistics…"));
        Dictionary<long, LocalStats> stats = LoadLocalStats(root);

        List<Game> games = [];
        for (int i = 0; i < manifests.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            (string lib, string file) = manifests[i];
            Game? game = ReadManifest(lib, file, stats);
            if (game is not null && !(config.HideSteamTools && IsTool(game.Name)))
                games.Add(game);

            progress?.Report(new(0.15 + 0.85 * (i + 1) / manifests.Count, game?.Name ?? Path.GetFileName(file)));
        }

        progress?.Report(new(1, $"{games.Count} games"));
        return new(root, null, Sort(games, config.SortBy), null, warnings, missing);
    }

    public static List<Game> Sort(IEnumerable<Game> games, SortMode mode) => mode switch
    {
        SortMode.Name => [.. games.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)],
        SortMode.Playtime => [.. games.OrderByDescending(g => g.Playtime).ThenBy(g => g.Name)],
        SortMode.Size => [.. games.OrderByDescending(g => g.SizeBytes).ThenBy(g => g.Name)],
        _ => [.. games.OrderByDescending(g => g.LastPlayed ?? DateTimeOffset.MinValue).ThenBy(g => g.Name)],
    };

    private static bool IsTool(string name) => ToolNamePrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    private static List<string> FindLibraries(string root, AppConfig config, List<string> missing)
    {
        List<string> result = [root];
        string vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
        try
        {
            if (File.Exists(vdf))
            {
                VdfNode folders = Vdf.ParseFile(vdf);
                VdfNode top = folders["libraryfolders"] ?? folders;
                foreach (VdfNode entry in top.Children.Values)
                {
                    // New format: "0" { "path" "..." }, old format: "1" "path".
                    string? path = entry.GetString("path") ?? entry.Value;
                    if (!string.IsNullOrWhiteSpace(path)) result.Add(path);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or FormatException or UnauthorizedAccessException)
        {
            // Fall back to the root library only.
        }

        result.AddRange(config.ExtraLibraryPaths.Where(p => !string.IsNullOrWhiteSpace(p)));
        List<string> all = [.. result.Select(p => Path.GetFullPath(p.Trim())).Distinct(StringComparer.OrdinalIgnoreCase)];
        missing.AddRange(all.Where(p => !Directory.Exists(Path.Combine(p, "steamapps"))));
        return all;
    }

    private static Game? ReadManifest(string library, string file, Dictionary<long, LocalStats> stats)
    {
        try
        {
            VdfNode? state = Vdf.ParseFile(file)["AppState"];
            if (state is null) return null;

            long appId = state.GetLong("appid");
            string? name = state.GetString("name");
            if (appId == 0 || string.IsNullOrWhiteSpace(name)) return null;
            if ((state.GetLong("StateFlags") & 4) == 0) return null; // not fully installed

            long manifestPlayed = state.GetLong("LastPlayed");
            DateTimeOffset? lastPlayed = manifestPlayed > 0 ? DateTimeOffset.FromUnixTimeSeconds(manifestPlayed) : null;
            TimeSpan playtime = TimeSpan.Zero;

            if (stats.TryGetValue(appId, out LocalStats s))
            {
                if (lastPlayed is null || s.LastPlayed > lastPlayed) lastPlayed = s.LastPlayed;
                playtime = s.Playtime;
            }

            return new Game(appId.ToString(), GameSource.Steam, name, state.GetString("installdir") ?? "", state.GetLong("SizeOnDisk"), lastPlayed, playtime, library);
        }
        catch (Exception ex) when (ex is IOException or FormatException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // localconfig.vdf: UserLocalConfigStore/Software/Valve/Steam/apps/<appid> { LastPlayed, Playtime (minutes) }
    private static Dictionary<long, LocalStats> LoadLocalStats(string root)
    {
        Dictionary<long, LocalStats> result = [];
        try
        {
            string userdata = Path.Combine(root, "userdata");
            if (!Directory.Exists(userdata)) return result;

            string? file = Directory.EnumerateDirectories(userdata)
                .Select(d => Path.Combine(d, "config", "localconfig.vdf"))
                .Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (file is null) return result;

            VdfNode? apps = Vdf.ParseFile(file)["UserLocalConfigStore"]?["Software"]?["Valve"]?["Steam"]?["apps"];
            if (apps is null) return result;

            foreach ((string key, VdfNode app) in apps.Children)
            {
                if (!long.TryParse(key, out long id)) continue;
                long played = app.GetLong("LastPlayed");
                result[id] = new(played > 0 ? DateTimeOffset.FromUnixTimeSeconds(played) : null, TimeSpan.FromMinutes(app.GetLong("Playtime")));
            }
        }
        catch (Exception ex) when (ex is IOException or FormatException or UnauthorizedAccessException)
        {
            // Stats are optional.
        }
        return result;
    }
}
