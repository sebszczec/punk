using System.Diagnostics;
using Punk.Steam;

namespace Punk.Epic;

/// <summary>
/// Launches an Epic Games Store game through the launcher's URI handler, the same call its desktop shortcuts make:
/// com.epicgames.launcher://apps/AppName?action=launch&amp;silent=true. UNTESTED against a real launch.
/// The launcher only accepts the request; running state comes from the session tracker.
/// </summary>
public static class EpicLauncher
{
    /// <summary>Returns an error message, or null once the launcher has been asked to run the game.</summary>
    public static string? Launch(Game game, string? clientExe, Action<string>? status = null)
    {
        if (clientExe is null || !File.Exists(clientExe)) return "Epic Games Launcher not found.";
        try
        {
            if (Process.GetProcessesByName("EpicGamesLauncher").Length == 0) status?.Invoke("Starting Epic Games Launcher…");
            Process.Start(new ProcessStartInfo($"com.epicgames.launcher://apps/{Uri.EscapeDataString(game.Id)}?action=launch&silent=true")
            {
                UseShellExecute = true,
            });
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}
