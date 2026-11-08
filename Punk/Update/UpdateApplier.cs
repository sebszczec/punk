using System.Diagnostics;
using Punk.Config;

namespace Punk.Update;

/// <summary>
/// Installs a staged update before the UI starts. Windows lets a running exe be renamed, so the current
/// files are moved aside (<c>.old</c>) and the new ones moved in; the old process then starts the new build and exits.
/// </summary>
public static class UpdateApplier
{
    private const UnixFileMode ExecutableMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
        UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
        UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    /// <summary><c>Relaunched</c>: a new process was started and the caller must exit. <c>Warning</c>: shown on the status line when an install failed.</summary>
    public readonly record struct Outcome(bool Relaunched, string? Warning);

    private sealed record Swap(string Target, string Staging, string? Backup);

    public static Outcome TryApply(AppConfig config)
    {
        if (!Updater.IsEnabled(config) || Environment.ProcessPath is not { } exe) return new(false, null);

        string installDir = Path.GetDirectoryName(exe)!;
        RemoveLeftovers(installDir);

        if (!File.Exists(UpdatePaths.MarkerFile)) return new(false, null);
        ReadyMarker? ready = Updater.ReadReady();
        if (ready is null) return new(false, null);

        string source = UpdatePaths.FilesDir(ready.Version);
        if (!Directory.Exists(source))
        {
            Updater.ClearReady();
            return new(false, null);
        }

        try
        {
            Install(source, installDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Read-only install folder (e.g. Program Files): keep the marker, so it is retried next start.
            return new(false, $"Update {ready.Version} was not installed ({ex.Message}). Running the current version.");
        }

        Updater.ClearReady();

        try
        {
            ProcessStartInfo start = new(exe) { UseShellExecute = false };
            foreach (string arg in Environment.GetCommandLineArgs().Skip(1)) start.ArgumentList.Add(arg);
            Process.Start(start);
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new(false, $"Updated to {ready.Version}. Start Punk again to run it ({ex.Message}).");
        }

        return new(true, null);
    }

    /// <summary>
    /// Moves each file in <paramref name="source"/> over the one in <paramref name="installDir"/>.
    /// The file is first copied next to its target, and the previous file is kept as <c>.old</c>, so a failure
    /// on any file rolls every change back.
    /// </summary>
    private static void Install(string source, string installDir)
    {
        List<Swap> swaps = [];
        try
        {
            foreach (string file in Directory.EnumerateFiles(source))
            {
                string target = Path.Combine(installDir, Path.GetFileName(file));
                string staging = target + ".new";
                File.Copy(file, staging, overwrite: true);
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(staging, ExecutableMode);

                Swap swap = new(target, staging, File.Exists(target) ? target + ".old" : null);
                swaps.Add(swap);
                if (swap.Backup is not null) File.Move(target, swap.Backup, overwrite: true);
                File.Move(staging, target, overwrite: true);
            }
        }
        catch
        {
            Rollback(swaps);
            throw;
        }
    }

    private static void Rollback(List<Swap> swaps)
    {
        foreach (Swap swap in Enumerable.Reverse(swaps))
        {
            Updater.TryDelete(swap.Staging);
            if (swap.Backup is null)
            {
                Updater.TryDelete(swap.Target); // the new file, which did not exist before
                continue;
            }

            try
            {
                if (!File.Exists(swap.Backup)) continue;
                Updater.TryDelete(swap.Target);
                File.Move(swap.Backup, swap.Target, overwrite: true);
            }
            catch
            {
                // best effort; the .old file is still on disk
            }
        }
    }

    /// <summary>Deletes <c>.old</c>/<c>.new</c> files left by an earlier update that were still in use then.</summary>
    private static void RemoveLeftovers(string installDir)
    {
        foreach (string file in Directory.EnumerateFiles(installDir, "*", SearchOption.TopDirectoryOnly))
            if (file.EndsWith(".old", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".new", StringComparison.OrdinalIgnoreCase))
                Updater.TryDelete(file);
    }
}
