using System.ComponentModel;
using System.Diagnostics;

namespace Punk.Launch;

/// <summary>
/// Detects whether a game is running, independent of the launcher: a game counts as running while any process
/// runs from inside its install folder. Works for Steam, Battle.net and future launchers alike.
/// </summary>
public static class GameProcesses
{
    private const int MaxDepth = 3;

    // Executable names per install folder; enumerating a game folder is not free, so do it once.
    private static readonly Dictionary<string, string[]> ExeNames = new(StringComparer.OrdinalIgnoreCase);

    public static bool IsRunning(string folder)
    {
        if (!Directory.Exists(folder)) return false;
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
        return OperatingSystem.IsWindows() ? IsRunningWindows(root) : IsRunningUnix(root);
    }

    // Windows: look up processes by the executable names found in the folder, then confirm their path.
    private static bool IsRunningWindows(string root)
    {
        foreach (string name in ExecutablesIn(root))
        {
            foreach (Process p in Process.GetProcessesByName(name))
            {
                using (p)
                {
                    try
                    {
                        string? path = p.MainModule?.FileName;
                        if (path is null || path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return true;
                    }
                    catch (Win32Exception)
                    {
                        return true; // elevated/protected (anti-cheat) process: the name match is the best we can do
                    }
                    catch (InvalidOperationException) { } // exited meanwhile
                }
            }
        }
        return false;
    }

    // Linux: any process whose executable or command line points into the folder (covers native games and Proton/Wine).
    private static bool IsRunningUnix(string root)
    {
        foreach (string dir in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(dir), out _)) continue;
            try
            {
                string? exe = new FileInfo(Path.Combine(dir, "exe")).LinkTarget;
                if (exe is not null && exe.StartsWith(root, StringComparison.Ordinal)) return true;

                string cmd = File.ReadAllText(Path.Combine(dir, "cmdline")).Replace('\0', ' ');
                if (cmd.Contains(root, StringComparison.Ordinal) || cmd.Contains(root.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return false;
    }

    private static string[] ExecutablesIn(string root)
    {
        lock (ExeNames)
        {
            if (ExeNames.TryGetValue(root, out string[]? cached)) return cached;
            string[] names;
            try
            {
                names = [.. Directory.EnumerateFiles(root, "*.exe", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = MaxDepth, IgnoreInaccessible = true })
                    .Select(Path.GetFileNameWithoutExtension)
                    .OfType<string>()
                    .Distinct(StringComparer.OrdinalIgnoreCase)];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { names = []; }
            ExeNames[root] = names;
            return names;
        }
    }
}
