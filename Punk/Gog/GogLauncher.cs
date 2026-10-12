using System.Diagnostics;
using Punk.Steam;

namespace Punk.Gog;

/// <summary>
/// Launches a GOG game through Galaxy using the same command line as Galaxy's own desktop shortcuts:
/// GalaxyClient.exe /gameId=ID /command=runGame /path="folder". UNTESTED against a real launch.
/// </summary>
public static class GogLauncher
{
    /// <summary>Returns an error message, or null once Galaxy has been asked to run the game.</summary>
    public static string? Launch(Game game, string? clientExe, Action<string>? status = null)
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
}
