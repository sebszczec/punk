---
name: add-game-launcher
description: Add support for another game launcher (Epic, GOG, Ubisoft, EA, Xbox, Lutris, ...) to Punk, or change how Steam/Battle.net are detected, launched or tracked while running. Use when asked to "add games from <launcher>", when launcher paths/detection break, or when launch/running/exit status is wrong.
---

# Adding a game launcher to Punk

Rules every launcher must follow (same as Steam/Battle.net):
- installed games only; scanned at startup behind the progress bar; rescanned on the configurable timer (default 60 s);
- selectable from the list and launched with Enter; configurable from the F2 settings dialog;
- resilient to moved/missing folders;
- **the status follows the real game process**: Launching → Starting → Playing (elapsed) → Closed (played time), or "did not start". Never leave a "Launched" message that outlives the game.

## Existing layout
- `Punk/Steam/Game.cs` - `Game` record + `GameSource` enum. `Id` is a string (Steam appid / Battle.net launch code); `Key` = `Source:Id` is the identity used everywhere in the UI.
- `Punk/Steam/SteamScanner.cs` (`ScanResult`), `Punk/BattleNet/BattleNetScanner.cs` (`BattleNetScan`) - per-launcher scanners.
- `Punk/Steam/GameScanner.cs` - runs all scanners, maps progress ranges (Steam 0-70 %, Battle.net 70-100 %), merges, sorts.
- `Punk/Steam/SteamLauncher.cs`, `Punk/BattleNet/BattleNetLauncher.cs` - send the launch request.
- `Punk/Launch/` - launcher-independent running-game tracking:
  - `GameFolders.InstallFolder(game)` - the folder the game's processes run from (one `switch` arm per `GameSource`).
  - `GameProcesses.IsRunning(folder)` - Windows: exe names found in the folder (depth 3, cached) + path check via `MainModule`, name-only if access is denied (anti-cheat/elevated); Linux: `/proc/*/exe` and `/proc/*/cmdline` pointing into the folder (covers Proton/Wine).
  - `GameSessionTracker` - polls every 2 s; Starting → Running → Exited, or NotStarted after 5 min; 6 s exit grace for games that restart/hand over between processes.
- `Punk/Config/AppConfig.cs` (+ `Clone`, `Normalize`), `Punk/Ui/SettingsDialog.cs`, `Punk/Ui/MainWindow.cs` (`ApplyResult`, `ValidateLaunch`, `LaunchSelected`, `OnSessionChanged`, `UpdateSessionInfo`, `OpenSettings`).

## Checklist for a new launcher `X`
1. **Investigate the machine first, read-only** (registry uninstall keys, `ProgramData`, `%APPDATA%`, protocol handler `HKCR\<scheme>\shell\open\command`, manifest/db files, **the launcher's log folder**). Do not guess formats. Check what is actually installed and which files prove a game is installed (Battle.net has no library file here: detection = known product catalog + executable exists under install roots; last played from `Battle.net.config`).
2. Add a `GameSource.X` value; create `Punk/X/XScanner.cs` returning games plus `Found`, `Warnings`, `MissingPaths` (copy the `BattleNetScan` shape). Skip silently (no error) on platforms where the launcher doesn't exist.
3. Add it to `GameScanner.Scan` with its own progress slice; include its warnings/missing paths and a `Found` flag in `ScanResult` (add a field with a default value so other call sites keep compiling).
4. Add `Punk/X/XLauncher.cs` with `string? Launch(Game, ..., Action<string>? status)`: blocking is fine (it runs on a background task), report progress through `status`, return an error string or null once the **launcher accepted** the request. Add the arm to the `switch` in `MainWindow.LaunchSelected`, and the platform tag in `RebuildList` (row badge) and `UpdateDetails`.
5. **Running-game tracking (required):** add the `GameSource.X` arm to `GameFolders.InstallFolder` so it returns the folder the game actually runs from (not the launcher's folder, not a shared parent like `D:\Gry`, or every game there would count). Nothing else is needed: `LaunchSelected` calls `_tracker.Track(game)` after a successful launch, and the tracker handles Playing/Closed/did-not-start, the `▶` row marker and a rescan on exit. If X games run from somewhere else (e.g. a store sandbox), extend `GameProcesses` rather than special-casing the UI.
6. Config: `XEnabled` + optional `XExtraRoots`; update `Normalize`, `Clone`, `SettingsDialog` (fields, validation, reset-to-defaults, layout rows/height) and the rescan condition in `OpenSettings`.
7. **Safeguards (required):** report unreachable roots in `MissingPaths`/`Warnings`; in `ApplyResult` add the launcher to the "retain previous games when its root is gone" logic (`steamGone`/`bnetGone` pattern) so games are kept as stale (`⚠`) instead of vanishing; extend `ValidateLaunch` to check the game folder and re-resolve the launcher executable before launching; fall back to auto-detection when a configured override is invalid.
8. Verify:
   - session tracking: `tools/SessionTest` (see below) - must print `PASS Starting,Running,Exited,Starting,NotStarted` on Windows and Linux;
   - UI: `punk-tui-selftest` (fake install tree + removal test); build/publish: `punk-build-publish`.
   - Never launch a real game on the user's machine without being asked; say clearly what was not tested (the launch command itself is the usual unknown) and ask the user to try it once - with the launcher **closed**, since cold start is where launchers misbehave.

## Session test (`tools/SessionTest`)
Copies an executable into a fresh fake game folder, tracks it with short timeouts, runs it, and checks the state sequence; `collide` mode checks a same-named process outside the folder is ignored.
```
cd tools/SessionTest
dotnet run -- 'C:\Windows\System32\PING.EXE' -n 5 127.0.0.1      # Windows lifecycle
dotnet run -- collide                                              # Windows name-collision check
dotnet publish -c Release -r linux-x64 --self-contained -o out-linux
wsl -d Ubuntu-22.04 -e bash -c "cd <repo>/tools/SessionTest/out-linux && chmod +x sessiontest && ./sessiontest /bin/sleep 4"
```

## Lessons
- A launcher returning success only means it **accepted** the request. Status must come from the game process, not from the launcher call (bug fixed: "Launched Diablo II: Resurrected" stayed after the game closed).
- Launch product codes and command lines for undocumented launchers come from memory/community docs - flag them as untested and ask the user to confirm with one click.
- Launchers that forward a command to an already-running client can silently drop it while the client is still starting. Battle.net does this: a cold-started client logs `Received IPC Message: IPC_COMMAND` but no `LaunchBinary:` until its game list is loaded (`Game Family Added` / `Active family set to`). Don't guess with fixed delays. Use the launcher's own logs (`%LOCALAPPDATA%\Battle.net\Logs`, UTC timestamps) to wait for readiness and to confirm the launch, and retry if it isn't confirmed. See `BattleNetLauncher`. When the user says "it doesn't work", read these logs first - they show exactly what the client received.
- Process detection must not match by name alone when the path is readable: names like `launcher.exe` collide across games.
- Folder size scans are slow: cache per path for the process lifetime (see `BattleNetScanner.DirSize`); same for exe-name lists (`GameProcesses`).
- Leftover folders without the executable are NOT installs (Diablo IV on this machine) - require the exe.
- Do not edit the user's real launcher config files; tests that change `%APPDATA%\Punk\config.json` must restore it.
- Parse registry shell commands (`"C:\x\Client.exe" --uri="%1"`) with `BattleNetScanner.ExecutableFromCommand`, never ad-hoc `IndexOf('"')` slicing (throws on malformed values). Wrap anything that touches user-supplied paths (`Path.GetFullPath`) so one bad entry cannot abort a whole scan.
- A game's install folder must never resolve to a shared parent (empty Steam `installdir` -> `steamapps/common`), or every game in it looks "running". `GameFolders.InstallFolder` returns "" in that case; keep that invariant for new launchers.
- Scans run on thread-pool threads and may overlap: static caches must be `ConcurrentDictionary`/locked. Redirected child-process pipes must be drained. Exceptions in `GameSessionTracker.Changed` subscribers are swallowed so the poll loop survives.
- Settings changes while a scan is in flight queue one more scan (`_rescanQueued` in `MainWindow`), otherwise they are ignored until the next interval.
