using Microsoft.Win32;
using Punk.BattleNet;
using Punk.Config;
using Punk.Steam;

namespace Punk.Gog;

public sealed record GogScan(string? Client, List<Game> Games, bool Found, List<string> Warnings, List<string> MissingPaths);

/// <summary>
/// Finds installed GOG Galaxy games. Galaxy registers every installed game under
/// HKLM\SOFTWARE\WOW6432Node\GOG.com\Games\&lt;gameID&gt; (gameName, path, exe), so no database parsing is needed.
/// Last-played time and playtime come from Galaxy's database (see GogPlayStats).
/// </summary>
public static class GogScanner
{
    private const string GamesKey = @"SOFTWARE\GOG.com\Games";          // under the 32-bit registry view
    private const string ClientKey = @"SOFTWARE\GOG.com\GalaxyClient";

    public static GogScan Scan(AppConfig config, IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows() || !config.GogEnabled) return new(null, [], false, [], []);

        string? client = FindClient();
        List<Game> games = [];
        List<string> warnings = [], missing = [];
        bool hasRegistry = false;
        var stats = GogPlayStats.Read();

        using RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
        using RegistryKey? root = hklm.OpenSubKey(GamesKey);
        if (root is not null)
        {
            hasRegistry = true;
            string[] ids = root.GetSubKeyNames();
            for (int i = 0; i < ids.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                using RegistryKey? k = root.OpenSubKey(ids[i]);
                string id = k?.GetValue("gameID") as string ?? ids[i];
                string? name = k?.GetValue("gameName") as string;
                string? path = (k?.GetValue("path") as string)?.Trim().TrimEnd('\\', '/');
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path)) continue;

                string? parent = Path.GetDirectoryName(path);
                if (!Directory.Exists(path))
                {
                    // Drive unplugged -> report the root so the game is kept as stale; folder deleted on a live drive -> not installed.
                    if (parent is not null && !Directory.Exists(parent) && !missing.Contains(parent, StringComparer.OrdinalIgnoreCase))
                    {
                        missing.Add(parent);
                        warnings.Add($"GOG folder unreachable: {parent}");
                    }
                    continue;
                }

                stats.TryGetValue($"gog_{id}", out var st);
                games.Add(new Game(id, GameSource.Gog, name, path, BattleNetScanner.DirSize(path), st.LastPlayed, st.Playtime, parent ?? path));
                progress?.Report(new((i + 1.0) / ids.Length, name));
            }
        }

        if (client is null && hasRegistry) warnings.Add("GOG Galaxy client not found; games are listed but cannot be launched.");
        return new(client, games, client is not null || hasRegistry, warnings, missing);
    }

    public static string? FindClient()
    {
        if (!OperatingSystem.IsWindows()) return null;

        using RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
        using RegistryKey? k = hklm.OpenSubKey(ClientKey);
        using RegistryKey? paths = k?.OpenSubKey("paths");
        if (paths?.GetValue("client") is string dir && k?.GetValue("clientExecutable") is string exe)
        {
            string candidate = Path.Combine(dir, exe);
            if (File.Exists(candidate)) return candidate;
        }

        // "C:\Program Files (x86)\GOG Galaxy\GalaxyClient.exe" /urlProtocol="%1"
        if (Registry.GetValue(@"HKEY_CLASSES_ROOT\goggalaxy\shell\open\command", "", null) is string { Length: > 0 } cmd)
        {
            string path = cmd.StartsWith('"') ? cmd[1..cmd.IndexOf('"', 1)] : cmd.Split(' ')[0];
            if (File.Exists(path)) return path;
        }

        foreach (string env in new[] { "ProgramFiles(x86)", "ProgramFiles" })
        {
            string? pf = Environment.GetEnvironmentVariable(env);
            if (pf is null) continue;
            string candidate = Path.Combine(pf, "GOG Galaxy", "GalaxyClient.exe");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
