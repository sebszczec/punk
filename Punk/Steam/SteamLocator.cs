using Microsoft.Win32;

namespace Punk.Steam;

public static class SteamLocator
{
    public static string? Find(string? overridePath)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
            return IsSteamRoot(overridePath) ? Path.GetFullPath(overridePath) : null;

        foreach (string candidate in Candidates())
            if (IsSteamRoot(candidate)) return Path.GetFullPath(candidate);

        return null;
    }

    public static bool IsFlatpak(string steamRoot) => steamRoot.Contains("com.valvesoftware.Steam", StringComparison.Ordinal);

    private static bool IsSteamRoot(string path) => Directory.Exists(Path.Combine(path, "steamapps"));

    private static IEnumerable<string> Candidates()
    {
        if (OperatingSystem.IsWindows())
        {
            foreach (string? p in WindowsRegistryPaths())
                if (!string.IsNullOrWhiteSpace(p)) yield return p;

            yield return @"C:\Program Files (x86)\Steam";
            yield return @"C:\Program Files\Steam";
        }
        else
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            yield return Path.Combine(home, ".steam", "steam");
            yield return Path.Combine(home, ".local", "share", "Steam");
            yield return Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam");
        }
    }

    private static IEnumerable<string?> WindowsRegistryPaths()
    {
        if (!OperatingSystem.IsWindows()) yield break;

        yield return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
        yield return Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;
    }
}
