using System.Diagnostics;
using System.Text.Json;
using Punk.Steam;

namespace Punk.Gog;

/// <summary>
/// Launches a GOG game through Galaxy using the same command line as Galaxy's own desktop shortcuts:
/// GalaxyClient.exe /gameId=ID /command=runGame /path="folder". UNTESTED against a real launch.
/// With launch options set, the game's primary play task (goggame-ID.info) is started directly instead,
/// because Galaxy's runGame command has no argument parameter. That run bypasses Galaxy.
/// </summary>
public static class GogLauncher
{
    /// <summary>Returns an error message, or null once Galaxy (or the game) has been asked to run.</summary>
    public static string? Launch(Game game, string? clientExe, Action<string>? status = null, string? launchOptions = null)
    {
        if (string.IsNullOrWhiteSpace(launchOptions))
            return LaunchThroughGalaxy(game, clientExe, status);
        return LaunchDirectly(game, launchOptions.Trim());
    }

    private static string? LaunchThroughGalaxy(Game game, string? clientExe, Action<string>? status)
    {
        if (clientExe is null || !File.Exists(clientExe)) return "GOG Galaxy client not found.";
        try
        {
            if (Process.GetProcessesByName("GalaxyClient").Length == 0) status?.Invoke("Starting GOG Galaxy…");
            Process.Start(new ProcessStartInfo(clientExe)
            {
                Arguments = $"/gameId={game.Id} /command=runGame /path=\"{game.InstallDir}\"",
                // Shell start detaches Galaxy from Punk's console and handles; as a plain child it stalled its cloud-save sync until Punk exited.
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(clientExe) ?? "",
            });
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private static string? LaunchDirectly(Game game, string launchOptions)
    {
        string infoPath = Path.Combine(game.InstallDir, $"goggame-{game.Id}.info");
        if (!File.Exists(infoPath))
            return $"GOG manifest not found ({Path.GetFileName(infoPath)}). Clear the launch options to start through Galaxy.";

        try
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(infoPath));
            if (!doc.RootElement.TryGetProperty("playTasks", out JsonElement tasks) || tasks.ValueKind != JsonValueKind.Array)
                return "GOG manifest has no play tasks. Clear the launch options to start through Galaxy.";

            JsonElement? task = null;
            foreach (JsonElement t in tasks.EnumerateArray())
            {
                if (!IsFileTask(t)) continue;
                if (t.TryGetProperty("isPrimary", out JsonElement p) && p.ValueKind == JsonValueKind.True) { task = t; break; }
                if (task is null && t.TryGetProperty("category", out JsonElement c) && c.GetString() == "game") task = t;
            }
            if (task is null) return "GOG manifest has no primary play task. Clear the launch options to start through Galaxy.";

            string relPath = task.Value.GetProperty("path").GetString() ?? "";
            string root = Path.GetFullPath(game.InstallDir);
            string exe = Path.GetFullPath(Path.Combine(root, relPath));
            if (!exe.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(exe))
                return $"Game executable not found: {relPath}";

            string workDir = task.Value.TryGetProperty("workingDir", out JsonElement w) && w.GetString() is { Length: > 0 } wd
                ? Path.GetFullPath(Path.Combine(root, wd))
                : Path.GetDirectoryName(exe) ?? root;

            Process.Start(new ProcessStartInfo(exe)
            {
                Arguments = launchOptions,
                WorkingDirectory = workDir,
                UseShellExecute = true,
            });
            return null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or KeyNotFoundException or InvalidOperationException)
        {
            return $"Could not read the GOG manifest: {ex.Message}";
        }
    }

    private static bool IsFileTask(JsonElement t) =>
        t.TryGetProperty("type", out JsonElement type) && type.GetString() == "FileTask"
        && t.TryGetProperty("path", out JsonElement path) && path.GetString() is { Length: > 0 };
}
