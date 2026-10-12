using System.Diagnostics;
using System.Globalization;
using Punk.Steam;

namespace Punk.BattleNet;

/// <summary>
/// Launches Battle.net games through the client.
/// The client drops a launch request that arrives before it has loaded its game list (seen in its logs:
/// the IPC command is received but no "LaunchBinary" follows). So after a cold start we wait until the client
/// log shows the game list is ready, then send the request and confirm it via the log, retrying if needed.
/// </summary>
public static class BattleNetLauncher
{
    private static readonly TimeSpan ClientReadyTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan LaunchConfirmTimeout = TimeSpan.FromSeconds(10);
    private const int MaxAttempts = 4;

    // Log markers (client log lines are timestamped in UTC).
    private static readonly string[] ReadyMarkers = ["Active family set to", "Game Family Added"];
    private const string LaunchedMarker = "LaunchBinary:";

    public static string LogDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Battle.net", "Logs");

    public static bool IsClientRunning() => Process.GetProcessesByName("Battle.net").Length > 0;

    /// <summary>Starts a game. Returns an error message, or null on success. Blocking: call from a background thread.</summary>
    public static string? Launch(Game game, string? clientExe, Action<string>? status = null)
    {
        try
        {
            if (clientExe is null)
            {
                Process.Start(new ProcessStartInfo($"battlenet://{game.Id}") { UseShellExecute = true });
                return null;
            }

            bool canConfirm = Directory.Exists(LogDir);

            if (!IsClientRunning())
            {
                status?.Invoke("Starting Battle.net client…");
                DateTime started = DateTime.UtcNow;
                Process.Start(new ProcessStartInfo(clientExe) { UseShellExecute = false });

                if (canConfirm)
                {
                    status?.Invoke("Waiting for Battle.net to load your games…");
                    if (!WaitUntil(() => ReadyMarkers.Any(m => LogHas(m, started)), ClientReadyTimeout))
                        return "Battle.net did not finish loading (waiting for a login or an update?). Finish it in the Battle.net window, then launch again.";
                    Thread.Sleep(TimeSpan.FromSeconds(2));
                }
                else
                {
                    Thread.Sleep(TimeSpan.FromSeconds(20)); // no logs to watch: best effort
                }
            }

            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                status?.Invoke(attempt == 1 ? $"Launching {game.Name}…" : $"Launching {game.Name}… (retry {attempt - 1})");
                DateTime sent = DateTime.UtcNow.AddSeconds(-1);
                // Same command line Blizzard's own game shortcuts use: --exec="launch CODE".
                Process.Start(new ProcessStartInfo(clientExe) { UseShellExecute = false, Arguments = $"--exec=\"launch {game.Id}\"" });

                if (!canConfirm) return null;
                if (WaitUntil(() => LogHas(LaunchedMarker, sent), LaunchConfirmTimeout)) return null;
            }

            return "Battle.net received the request but did not start the game. Check the Battle.net window for a prompt (update, region, login).";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>True if a Battle.net client log contains <paramref name="marker"/> on a line timestamped at or after <paramref name="sinceUtc"/>.</summary>
    public static bool LogHas(string marker, DateTime sinceUtc)
    {
        if (!Directory.Exists(LogDir)) return false;

        foreach (FileInfo file in new DirectoryInfo(LogDir).GetFiles("battle.net-*.log").Where(f => f.LastWriteTimeUtc >= sinceUtc.AddSeconds(-5)))
        {
            try
            {
                using FileStream fs = new(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using StreamReader reader = new(fs);
                while (reader.ReadLine() is { } line)
                {
                    if (line.Contains(marker, StringComparison.Ordinal) && LineTime(line) is { } t && t >= sinceUtc)
                        return true;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return false;
    }

    // "I 2026-10-12 09:38:35.514114 [Main] ..." -> UTC timestamp
    private static DateTime? LineTime(string line) =>
        line.Length > 28 && DateTime.TryParseExact(line.AsSpan(2, 26), "yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime t) ? t : null;

    private static bool WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            Thread.Sleep(500);
        }
        return condition();
    }
}
