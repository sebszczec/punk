---
name: punk-tui-selftest
description: Verify Punk's Terminal.Gui interface without a human - headless screen dumps, injected keystrokes, Linux tmux runs - and look up Terminal.Gui 2.5 APIs. Use when changing anything under Punk/Ui, debugging layout/key handling, or checking that a UI change really works.
---

# Testing and developing the Punk TUI

## Headless self-test (Windows, ANSI driver)
`Program.cs` has a test hook. Set env vars, run, read the dump:
```powershell
cd C:\Users\sebsz\source\punk\Punk
$env:PUNK_SELFTEST="$env:TEMP\punk_dump.txt"; dotnet run; $env:PUNK_SELFTEST=$null
Get-Content "$env:TEMP\punk_dump.txt" -Encoding UTF8
```
- It writes labelled screen snapshots (`--- main ---`, `filter ob`, `settings`, ...) and exits by itself; a 45 s watchdog writes `--- TIMEOUT ---` and exits 3 if it hangs. The timeline of injected keys is the `switch (n)` in `Program.cs` (one step = 500 ms).
- `PUNK_TEST_LIB=<dir>`: with the test, drops a fake Steam manifest into that extra library at step 21 (tests auto refresh, the `✚` flag). Add `PUNK_TEST_REMOVE=1` to delete the library instead (tests the unreachable-library safeguard: `⚠` rows kept).
- To test settings/paths, temporarily edit `%APPDATA%\Punk\config.json` (refresh interval 5 makes auto refresh visible), and **restore it afterwards** (copy a backup first).
- Hide blank/noise lines when reading: filter lines that are blank or match `^[│╭╰]\s{60}`.

## Game launch / running status
The self-test never presses Enter on a game: that would start a real game. The header line (`_sessionInfo`: Starting → Playing · 12m → Closed · played 1h 05m) is driven by `GameSessionTracker`. Test that logic with `tools/SessionTest`, which uses a fake game process (see the `add-game-launcher` skill), not with the UI self-test.

## Linux (WSL + tmux, real keystrokes)
`wsl -d Ubuntu-22.04 -e bash /mnt/c/Users/sebsz/source/punk/scripts/linux_test.sh` (needs `dist/linux-x64/Punk` published; see `punk-build-publish`). It builds a fake `~/.steam/steam` tree and drives the app with tmux. Piping keys through `script` does NOT work reliably - use tmux.

## Terminal.Gui 2.5 notes (API differs from v1; most online samples are wrong)
- No web docs needed: the XML docs ship in the package. Query them:
  `$x=[xml](Get-Content "$env:USERPROFILE\.nuget\packages\terminal.gui\2.5.0\lib\net10.0\Terminal.Gui.xml" -Raw); $x.doc.members.member.name | ? { $_ -match 'ProgressBar' }`
  (Reflection on the DLL fails - missing transitive assemblies.)
- Setup: `using IApplication app = Application.Create().Init();` then `app.Run(window)`. Drivers: `"ansi"`, `"dotnet"`, `"windows"` (no "fake"). Timers: `app.AddTimeout(TimeSpan, Func<bool>)`; marshal from threads with `app.Invoke(...)`.
- `using Attribute = Terminal.Gui.Drawing.Attribute;` - otherwise ambiguous with `System.Attribute`.
- Colors/themes: build a `Scheme` and call `view.SetScheme(...)`; see `Punk/Ui/Theme.cs`.
- A `ListView` keeps stale rows after its source shrinks: re-assign `list.Source = new ListWrapper<string>(rows)` after rebuilding.
- Dialog buttons: handle `button.Accepting`; set `e.Handled = true` to keep the dialog open (validation) - do not name the lambda parameters `_` and `e` together if you also use `out _` inside (discard clash); use `s, e`.
- Global keys: handle `app.Keyboard.KeyDown` (see `MainWindow.OnAppKeyDown`); `Ctrl+Q` must call `app.RequestStop()` explicitly - binding `Command.Quit` alone did not quit on Linux.
- Injecting keys in tests: `inj.InjectKey(...)` must run off the UI thread (`Task.Run`), otherwise it deadlocks.
- Writing files from bash heredocs mangles backslashes (`'\\'` in C#) - create source files with the Write tool.
