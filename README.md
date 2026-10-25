# Punk

**One keyboard-driven terminal launcher for all your installed PC games.**

Punk scans your Steam, Battle.net and GOG Galaxy installs, shows them in a single list, and starts any of them with Enter. It runs in the terminal, works on Windows and Linux, and is built with C# / .NET 10 and [Terminal.Gui](https://github.com/gui-cs/Terminal.Gui) 2.5.

## Features

- **One library for every launcher**: installed games from Steam, Battle.net and GOG Galaxy in one sorted, searchable list.
- **Type to filter**: start typing to narrow the list, Esc clears the filter.
- **Game details**: last played, total playtime, size on disk, app/product ID, library and install folder.
- **Sorting**: by last played, name, playtime or size (F3).
- **Live session tracking**: after a launch Punk follows the game's own process (Launching → Starting → Playing · 12m → Closed · played 1h 05m) and tells you if the game never started.
- **Auto refresh**: rescans on a timer (60 s by default) and marks newly installed games.
- **Survives missing drives**: games on an unplugged drive or unreachable library stay in the list with a warning instead of disappearing.
- **CPU / GPU temperatures** shown in the header.
- **Themes**: Default (with a custom accent color), Cyberpunk, Colorful and Ambient presets (F4), plus an ASCII-only symbol mode for terminals without good Unicode fonts.

## Supported platforms

| Launcher       | Windows | Linux |
|----------------|:-------:|:-----:|
| Steam          | ✅      | ✅ (native and Proton games) |
| Battle.net     | ✅      | —     |
| GOG Galaxy     | ✅      | —     |

Linux builds are tested on Ubuntu / Zorin OS (x64).

## Getting started

### Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download) to build. The published binaries are self-contained and need no .NET install.
- A terminal with Unicode and true color support (Windows Terminal, GNOME Terminal, Konsole, kitty, ...).

### Run from source

```sh
git clone https://github.com/sebszczec/punk.git
cd punk
dotnet run --project Punk
```

### Build standalone binaries

On Windows, `publish.ps1` installs anything missing (.NET 10 SDK, PawnIO driver) via `winget` and publishes single-file builds for both platforms:

```powershell
./publish.ps1
```

Output:

- `dist/win-x64/Punk.exe`
- `dist/linux-x64/Punk`

Installing missing dependencies needs an administrator terminal. To publish by hand on any OS:

```sh
dotnet publish Punk/Punk.csproj -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -o dist/linux-x64
```

(use `-r win-x64` for Windows).

## Keyboard shortcuts

| Key                     | Action                     |
|-------------------------|----------------------------|
| *type*                  | Filter games by name       |
| Esc                     | Clear the filter           |
| ↑ / ↓, PgUp / PgDn      | Move through the list      |
| Enter                   | Launch the selected game   |
| F5 / Ctrl+R             | Rescan now                 |
| F3 / Ctrl+O             | Change sort order          |
| F4                      | Cycle theme preset         |
| F2 / Ctrl+S             | Settings                   |
| Ctrl+Q                  | Quit                       |

## Configuration

Everything can be changed in the settings dialog (F2): refresh interval (0 turns auto refresh off), Steam path override, extra Steam library folders, extra Battle.net game folders, enabling or disabling Battle.net / GOG, hiding Steam tools (Proton, runtimes, redistributables), accent color and ASCII symbols.

Settings are stored as JSON in:

- Windows: `%APPDATA%\Punk\config.json`
- Linux: `$XDG_CONFIG_HOME/punk/config.json` (falls back to `~/.config/punk/config.json`)

Steam, Battle.net and GOG are detected automatically. Set paths only if a launcher or library is installed somewhere unusual.

## Temperatures

- **Linux**: read from `/sys/class/hwmon` (k10temp, coretemp, zenpower, amdgpu, nouveau, ...), no extra setup needed.
- **NVIDIA GPUs** (both OSes): read through `nvidia-smi` if it is installed.
- **Windows CPU**: read with [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor), which needs the [PawnIO](https://pawnio.eu/) driver and an **administrator terminal**. Without them Punk falls back to the Windows thermal zone counter, which is less accurate or missing on some machines. Install PawnIO with `winget install namazso.PawnIO` (`publish.ps1` does this for you).

Temperatures are optional: if no sensor is available, Punk just leaves that part of the header empty.

## How it works

Punk only reads launcher data. It never changes launcher files:

- **Steam**: finds the Steam install, reads `libraryfolders.vdf` and the `appmanifest_*.acf` files, and gets last-played times and playtime from the user's `localconfig.vdf`. Games are started through `steam://` URLs.
- **Battle.net**: checks for the executables of known Blizzard games under the client's install folder and any extra folders you add; last-played times come from `Battle.net.config`. Games are started through the Battle.net client.
- **GOG Galaxy**: lists games from the GOG registry entries; playtime and last-played times come from Galaxy's local database, opened read-only. Games are started through the Galaxy client.
- **Running games** are found by watching for processes started from the game's install folder. On Linux this covers Proton / Wine games too.

## Project layout

```
Punk/
  Steam/       Steam detection, VDF parser, scanner, launcher, shared Game model
  BattleNet/   Battle.net scanner and launcher
  Gog/         GOG Galaxy scanner, launcher, playtime stats
  Launch/      Process tracking, session tracker, temperatures, elevation check
  Config/      AppConfig + JSON store
  Ui/          Terminal.Gui main window, settings dialog, themes
tools/SessionTest/   Console test for the session tracker using a fake game process
scripts/             Linux test script (WSL + tmux)
publish.ps1          Dependency check + Windows/Linux publish
```

## Contributing

Issues and pull requests are welcome, especially support for more launchers (Epic, Ubisoft Connect, EA app, Xbox, Lutris, Heroic, ...). A new launcher needs a scanner that returns installed games, a launcher that starts them, and a matching install-folder entry so the session tracker can follow the running game. The Steam, Battle.net and GOG implementations show the pattern.

For automated UI checks, run with `PUNK_SELFTEST=<file>`: Punk runs a scripted key sequence, writes screen snapshots to that file and exits.

## License

Released under the [MIT License](LICENSE): use, copy, modify and distribute it however you like.
