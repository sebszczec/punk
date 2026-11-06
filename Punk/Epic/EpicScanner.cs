using System.Text.Json;
using Microsoft.Win32;
using Punk.BattleNet;
using Punk.Config;
using Punk.Steam;

namespace Punk.Epic;

public sealed record EpicScan(string? Client, List<Game> Games, bool Found, List<string> Warnings, List<string> MissingPaths);

/// <summary>
/// Finds installed Epic Games Store games. The launcher writes one manifest per installed item to
/// %ProgramData%\Epic\EpicGamesLauncher\Data\Manifests (*.item JSON) with the install folder, the game's executable
/// and its display name, so no registry or database parsing is needed. Epic keeps no local last-played time.
/// </summary>
public static class EpicScanner
{
    private sealed record Manifest(string AppName, string DisplayName, string InstallLocation, string LaunchExecutable);

    public static string ManifestsDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");

    public static EpicScan Scan(AppConfig config, IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows() || !config.EpicEnabled) return new(null, [], false, [], []);

        string? client = FindClient();
        bool hasManifests = Directory.Exists(ManifestsDir);
        List<Game> games = [];
        List<string> warnings = [], missing = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        if (hasManifests)
        {
            string[] files = Directory.GetFiles(ManifestsDir, "*.item"); // not recursive: Pending\ holds half-finished installs
            for (int i = 0; i < files.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (ReadManifest(files[i]) is not { } m || !seen.Add(m.AppName)) continue;

                if (!Directory.Exists(m.InstallLocation))
                {
                    // Drive unplugged -> report the root so the game is kept as stale; folder deleted on a live drive -> not installed.
                    string? parent = Path.GetDirectoryName(m.InstallLocation);
                    if (parent is not null && !Directory.Exists(parent) && !missing.Contains(parent, StringComparer.OrdinalIgnoreCase))
                    {
                        missing.Add(parent);
                        warnings.Add($"Epic Games folder unreachable: {parent}");
                    }
                    continue;
                }

                // Leftover folders without the executable are not installs.
                if (!File.Exists(Path.Combine(m.InstallLocation, m.LaunchExecutable))) continue;

                games.Add(new Game(m.AppName, GameSource.Epic, m.DisplayName, m.InstallLocation, BattleNetScanner.DirSize(m.InstallLocation),
                    null, TimeSpan.Zero, Path.GetDirectoryName(m.InstallLocation) ?? m.InstallLocation));
                progress?.Report(new((i + 1.0) / files.Length, m.DisplayName));
            }
        }

        if (client is null && hasManifests) warnings.Add("Epic Games Launcher not found; games are listed but cannot be launched.");
        return new(client, games, client is not null || hasManifests, warnings, missing);
    }

    public static string? FindClient()
    {
        if (!OperatingSystem.IsWindows()) return null;

        // "C:\Program Files\Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe" %1
        if (Registry.GetValue(@"HKEY_CLASSES_ROOT\com.epicgames.launcher\shell\open\command", "", null) is string cmd
            && BattleNetScanner.ExecutableFromCommand(cmd) is { } path && File.Exists(path))
            return path;

        foreach (string env in new[] { "ProgramFiles(x86)", "ProgramFiles" })
        {
            string? pf = Environment.GetEnvironmentVariable(env);
            if (pf is null) continue;
            string candidate = Path.Combine(pf, "Epic Games", "Launcher", "Portal", "Binaries", "Win64", "EpicGamesLauncher.exe");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static Manifest? ReadManifest(string file)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions { AllowTrailingCommas = true });
            JsonElement r = doc.RootElement;

            // Engine and plugin items (bIsApplication=false) share the engine folder with the editor; they are not games.
            if (!Flag(r, "bIsApplication") || !Flag(r, "bIsExecutable") || Flag(r, "bIsIncompleteInstall")) return null;

            string? appName = Text(r, "AppName"), name = Text(r, "DisplayName"), location = Text(r, "InstallLocation"), exe = Text(r, "LaunchExecutable");
            if (string.IsNullOrWhiteSpace(appName) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(location) || string.IsNullOrWhiteSpace(exe)) return null;

            // A drive root would make every game on that drive look running (see GameFolders).
            string full = Path.GetFullPath(location.Trim());
            if (full == Path.GetPathRoot(full)) return null;
            return new Manifest(appName.Trim(), name.Trim(), full.TrimEnd('\\'), exe.Trim());
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static bool Flag(JsonElement e, string name) => e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.True;

    private static string? Text(JsonElement e, string name) => e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
