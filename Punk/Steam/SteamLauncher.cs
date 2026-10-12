using System.ComponentModel;
using System.Diagnostics;

namespace Punk.Steam;

public static class SteamLauncher
{
    /// <summary>Starts a game through the Steam client. Returns an error message, or null on success.</summary>
    public static string? Launch(Game game, string? steamRoot)
    {
        string uri = $"steam://rungameid/{game.Id}";
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            }
            else if (steamRoot is not null && SteamLocator.IsFlatpak(steamRoot))
            {
                Start("flatpak", "run", "com.valvesoftware.Steam", uri);
            }
            else
            {
                try { Start("steam", uri); }
                catch (Win32Exception) { Start("xdg-open", uri); }
            }
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private static void Start(string file, params string[] args)
    {
        ProcessStartInfo info = new(file) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string a in args) info.ArgumentList.Add(a);
        Process.Start(info);
    }
}
