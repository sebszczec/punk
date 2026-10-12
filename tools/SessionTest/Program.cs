using System.Diagnostics;
using Punk.Launch;
using Punk.Steam;

// Usage: sessiontest <fake game exe source> <args...>
// Copies the executable into a fresh "game folder", tracks a Game pointing there, runs it, prints transitions.
if (args[0] == "collide") { Collide.Run(); return; }
string src = args[0];
string folder = Path.Combine(Path.GetTempPath(), "punk_fakegame_" + Guid.NewGuid().ToString("N")[..6]);
Directory.CreateDirectory(Path.Combine(folder, "bin"));
string exe = Path.Combine(folder, "bin", OperatingSystem.IsWindows() ? "FakeGame.exe" : "fakegame");
File.Copy(src, exe);
if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(exe, UnixFileMode.UserRead | UnixFileMode.UserExecute | UnixFileMode.UserWrite);

GameSessionTracker.PollInterval = TimeSpan.FromMilliseconds(500);
GameSessionTracker.ExitGrace = TimeSpan.FromSeconds(2);
GameSessionTracker.StartTimeout = TimeSpan.FromSeconds(6);

Game game = new("TEST", GameSource.BattleNet, "Fake Game", folder, 0, null, TimeSpan.Zero, folder);
Stopwatch sw = Stopwatch.StartNew();
List<string> seen = [];
using GameSessionTracker tracker = new();
tracker.Changed += s => { string line = $"{sw.Elapsed.TotalSeconds,5:F1}s {s.State} played={s.Played.TotalSeconds:F0}s"; seen.Add(s.State.ToString()); Console.WriteLine(line); };

Console.WriteLine($"other process with same name outside folder running: {GameProcesses.IsRunning(folder)} (expect False)");
tracker.Track(game);
Thread.Sleep(1500);                                   // launcher delay before the game appears
using Process p = Process.Start(new ProcessStartInfo(exe, args[1..]) { UseShellExecute = false, RedirectStandardOutput = true })!;
p.WaitForExit();
Thread.Sleep(4000);

// Second session that never starts -> NotStarted
Game ghost = game with { Id = "GHOST", InstallDir = Path.Combine(folder, "nothing") };
Directory.CreateDirectory(ghost.InstallDir);
tracker.Track(ghost);
Thread.Sleep(8000);

string expected = "Starting,Running,Exited,Starting,NotStarted";
Console.WriteLine(string.Join(",", seen) == expected ? $"PASS {expected}" : $"FAIL got {string.Join(",", seen)}");
