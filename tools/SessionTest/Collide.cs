using System.Diagnostics;
using Punk.Launch;

public static class Collide
{
    // A game folder containing PING.EXE while the system's PING.EXE runs elsewhere: must NOT count as the game running.
    public static void Run()
    {
        string folder = Path.Combine(Path.GetTempPath(), "punk_collide_" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(folder);
        File.Copy(@"C:\Windows\System32\PING.EXE", Path.Combine(folder, "PING.EXE"));
        using Process sys = Process.Start(new ProcessStartInfo(@"C:\Windows\System32\PING.EXE", "-n 4 127.0.0.1") { UseShellExecute = false, RedirectStandardOutput = true })!;
        Thread.Sleep(700);
        bool r1 = GameProcesses.IsRunning(folder);
        using Process mine = Process.Start(new ProcessStartInfo(Path.Combine(folder, "PING.EXE"), "-n 3 127.0.0.1") { UseShellExecute = false, RedirectStandardOutput = true })!;
        Thread.Sleep(700);
        bool r2 = GameProcesses.IsRunning(folder);
        Console.WriteLine($"{(!r1 ? "PASS" : "FAIL")} system ping outside folder -> {r1} (expect False)");
        Console.WriteLine($"{(r2 ? "PASS" : "FAIL")} ping inside folder -> {r2} (expect True)");
        sys.WaitForExit(); mine.WaitForExit();
    }
}
