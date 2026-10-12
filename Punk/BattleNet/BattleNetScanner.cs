using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Win32;
using Punk.Config;
using Punk.Steam;

namespace Punk.BattleNet;

/// <summary>
/// Finds installed Battle.net games. Battle.net has no public library format, so installs are detected by
/// looking for each known product's executable under the client's install roots, and last-played times
/// come from Battle.net.config.
/// </summary>
public sealed record BattleNetScan(string? Client, List<Game> Games, bool Found, List<string> Warnings, List<string> MissingPaths);

public static class BattleNetScanner
{
    private sealed record Product(string ConfigKey, string Name, string LaunchCode, string Folder, string[] Exes);

    private static readonly Product[] Catalog =
    [
        new("fenris", "Diablo IV", "Fen", "Diablo IV", ["Diablo IV.exe"]),
        new("osi", "Diablo II: Resurrected", "OSI", "Diablo II Resurrected", ["D2R.exe"]),
        new("diablo3", "Diablo III", "D3", "Diablo III", ["Diablo III64.exe", "Diablo III.exe"]),
        new("anbs", "Diablo Immortal", "ANBS", "Diablo Immortal", ["Diablo Immortal.exe"]),
        new("s1", "StarCraft", "S1", "StarCraft", ["StarCraft.exe"]),
        new("s2", "StarCraft II", "S2", "StarCraft II", ["SC2Switcher.exe", "StarCraft II.exe"]),
        new("prometheus", "Overwatch 2", "Pro", "Overwatch", ["Overwatch Launcher.exe", "Overwatch.exe"]),
        new("wow", "World of Warcraft", "WoW", "World of Warcraft", [@"_retail_\Wow.exe", "Wow.exe"]),
        new("wtcg", "Hearthstone", "WTCG", "Hearthstone", ["Hearthstone.exe"]),
        new("hero", "Heroes of the Storm", "Hero", "Heroes of the Storm", ["HeroesSwitcher.exe"]),
        new("w3", "Warcraft III: Reforged", "W3", "Warcraft III", ["Warcraft III.exe", @"_retail_\x86_64\Warcraft III.exe"]),
    ];

    // Folder sizes are expensive to compute; keep them for the life of the process.
    private static readonly ConcurrentDictionary<string, long> SizeCache = new(StringComparer.OrdinalIgnoreCase);

    public static string ConfigPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Battle.net", "Battle.net.config");

    public static BattleNetScan Scan(AppConfig config, IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows() || !config.BattleNetEnabled) return new(null, [], false, [], []);

        string? client = FindClient();
        JsonElement? root = ReadConfig();

        List<string> roots = [];
        if (root is { } r && TryGet(r, "Client", "Install", "DefaultInstallPath") is { } def) roots.Add(def);
        if (client is not null && Path.GetDirectoryName(client) is { } clientDir && Path.GetDirectoryName(clientDir) is { } parent) roots.Add(parent);
        roots.AddRange(config.BattleNetExtraRoots);
        List<string> wanted = [.. roots.Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => { try { return Path.GetFullPath(p.Trim()); } catch { return ""; } })
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)];
        roots = [.. wanted.Where(Directory.Exists)];
        List<string> missing = [.. wanted.Where(p => !Directory.Exists(p))];
        List<string> warnings = [.. missing.Select(m => $"Battle.net folder unreachable: {m}")];

        if (client is null && roots.Count == 0) return new(null, [], false, warnings, missing);

        List<Game> games = [];
        for (int i = 0; i < Catalog.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            Product p = Catalog[i];

            foreach (string libRoot in roots)
            {
                string dir = Path.Combine(libRoot, p.Folder);
                if (!p.Exes.Any(e => File.Exists(Path.Combine(dir, e)))) continue;

                DateTimeOffset? last = null;
                if (root is { } rr && TryGet(rr, "Games", p.ConfigKey, "LastPlayed") is { } lp && long.TryParse(lp, out long secs) && secs > 0)
                    last = DateTimeOffset.FromUnixTimeSeconds(secs);

                games.Add(new Game(p.LaunchCode, GameSource.BattleNet, p.Name, dir, DirSize(dir), last, TimeSpan.Zero, libRoot));
                progress?.Report(new((i + 1.0) / Catalog.Length, p.Name));
                break;
            }
        }

        if (client is null) warnings.Add("Battle.net client not found; games are listed but cannot be launched.");
        return new(client, games, true, warnings, missing);
    }

    public static string? FindClient()
    {
        if (!OperatingSystem.IsWindows()) return null;

        // "D:\Gry\Battle.net\Battle.net.exe" --uri="%1"
        if (Registry.GetValue(@"HKEY_CLASSES_ROOT\battlenet\shell\open\command", "", null) is string cmd
            && ExecutableFromCommand(cmd) is { } path && File.Exists(path))
            return path;

        foreach (string env in new[] { "ProgramFiles(x86)", "ProgramFiles" })
        {
            string? pf = Environment.GetEnvironmentVariable(env);
            if (pf is null) continue;
            string candidate = Path.Combine(pf, "Battle.net", "Battle.net.exe");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>Extracts the executable from a registry shell command such as <c>"C:\x\a.exe" --uri="%1"</c>.</summary>
    internal static string? ExecutableFromCommand(string cmd)
    {
        cmd = cmd.Trim();
        if (cmd.Length == 0) return null;
        if (cmd[0] != '"') return cmd.Split(' ')[0];
        int end = cmd.IndexOf('"', 1);
        return end > 1 ? cmd[1..end] : null;
    }

    private static JsonElement? ReadConfig()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return null;
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(ConfigPath), new JsonDocumentOptions { AllowTrailingCommas = true });
            return doc.RootElement.Clone();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? TryGet(JsonElement e, params string[] path)
    {
        foreach (string key in path)
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(key, out JsonElement next)) return null;
            e = next;
        }
        return e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString();
    }

    internal static long DirSize(string dir)
    {
        if (SizeCache.TryGetValue(dir, out long cached)) return cached;
        long total = 0;
        try
        {
            foreach (string file in Directory.EnumerateFiles(dir, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
            {
                try { total += new FileInfo(file).Length; } catch (IOException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        SizeCache[dir] = total;
        return total;
    }
}
