---
name: punk-build-publish
description: Build, package, release and smoke-test the Punk TUI (C#/.NET 10 + Terminal.Gui 2.5) for Windows (win-x64, Inno Setup installer) and Linux/Zorin (linux-x64). Use when asked to build, publish, release, ship, package, bump a version, make the installer or icon, or verify Punk on both platforms, or when publish/release fails.
---

# Build, package and release Punk

Repo: `C:\Users\sebsz\source\punk`. Requires .NET SDK 10 (`dotnet --version` should say 10.x).
Layout: `Punk/` (app, `Punk.csproj` holds `<Version>` and `<ApplicationIcon>`), `build.ps1` (build + release), `installer/punk.iss` (Inno Setup), `installer/icon/` (icon source + generator), `dist/` (git-ignored build output).

## Build and release: what to run
- `./build.ps1` (no arguments) - interactive menu, Enter = `[1]` build locally: builds the version in `Punk.csproj`, publishes nothing. `[2]` create a new version: proposes patch/minor/major checked against GitHub (or a typed number), then "release now or later". `[3]` release a version already built in `dist/release` (refused if the commit changed since the build; `dist/release/build.json` records the commit).
- `./build.ps1 -NonInteractive` - builds the version in `Punk.csproj`, publishes nothing. Refuses if that tag already exists on origin, so it only works for an unreleased version.
- `./build.ps1 -CheckVersion` - csproj version, latest release, highest `v*` tag, suggested next versions. Exit 1 if the csproj is not newer than GitHub.
- `./build.ps1 -Version x.y.z` / `-Bump patch|minor|major` - writes `<Version>` into the csproj and builds, nothing published.
- `./build.ps1 -Bump patch -Release` - builds, asks to confirm, commits the csproj as `Release vX.Y.Z`, tags, pushes `main` and the tag, creates the GitHub release (draft first, then published once all assets are up).
- Release preconditions: branch `main`, no uncommitted changes except `Punk.csproj`, `gh auth status` ok, GitHub reachable.
- Output in `dist/release/`: `Setup-Punk-<v>.exe`, `punk-win-x64.zip`, `punk-linux-x64.zip`, `SHA256SUMS`, `build.json`. The release uploads all four (the in-app updater needs `SHA256SUMS`).

## Gotchas found the hard way
- **Multi-RID `dotnet restore` fails**: `-p:RuntimeIdentifiers=win-x64;linux-x64` is split by MSBuild into two switches (`MSB1006`). Also, a restore for one RID overwrites `obj/project.assets.json` for the other, so one shared restore followed by `--no-restore` publishes is impossible.
- **Parallel `dotnet publish` of both RIDs is not reliable**: they share `obj/` and fail with duplicate `AssemblyInfo` / `TargetFrameworkAttribute` errors. A separate `BaseIntermediateOutputPath` per RID makes it worse (stale generated files get compiled). The script publishes the RIDs one after another. Parallelism is only used for the two zips and the Inno Setup compile, which share nothing.
- **Inno Setup**: the installed compiler here is `C:\Program Files\Inno Setup 7\ISCC.exe` (v7.1.1). `Find-Iscc` accepts any `Inno Setup N` folder. Run ISCC from PowerShell: Git Bash rewrites `/DVersion=...` arguments as paths and fails with "more than one script filename".
- **`gh release create --generate-notes` failed** on this machine with `no matches found for '-'` (gh 2.103.3, Windows). The script writes notes itself from `git log` (`Get-ReleaseNotes`): one line per commit since the previous release, or "Punk <tag>" for the first release.
- **A failed release leaves things behind**: the tag is pushed before `gh release create`, so a failure leaves a pushed tag with no release. Recovery used for v1.0.0/v1.0.1: `gh release create vX --repo sebszczec/punk --draft --title "Punk vX" --notes "..." <assets>` from `dist/release`, check the draft with `gh release view --json isDraft,assets`, then `gh release edit vX --draft=false`. A draft shows an `untagged-...` URL until it is published; that is normal.
- **A release without `SHA256SUMS` is broken**: v1.0.2 was published without it, so existing installs cannot verify or offer that update. Fixed in the script (`Publish-Release` adds `SHA256SUMS`). Check every release: `gh release view vX --json assets`.
- **Writes to public releases can be blocked by the permission system** (for example uploading an asset to a published release). Don't try to route around it: explain the exact `gh` command to the user and let them run it.
- **Interactive prompts can't be tested here**: the Bash/PowerShell tools run non-interactively, so `Read-Host` fails and piping answers into `build.ps1` does not work. Test the menu in the user's own terminal. Non-prompt paths (`-CheckVersion`, `-NonInteractive`) can be run here.
- **Git Bash `/c/...` paths break Node and Windows programs**: pass `C:/...` paths to Node and PowerShell paths to Windows tools.
- **Icon**: `installer/punk.ico` is generated from `installer/icon/punk-icon.svg` by `installer/icon/build-ico.cjs` and committed. The renderer `@resvg/resvg-js` is not committed: `npm install --prefix <scratch> @resvg/resvg-js`, then `NODE_PATH=<scratch>/node_modules node installer/icon/build-ico.cjs`. The exe icon comes from `ApplicationIcon` in the csproj; the installer's desktop shortcut task is `desktopicon` (ticked by default).
- **Installer tests touch the desktop**: a silent install creates `Punk.lnk` on the real desktop. Check for an existing `Punk.lnk` first, install to a unique temp `/DIR`, check the shortcut, then uninstall with `unins000.exe /VERYSILENT` and confirm the shortcut is gone. `/MERGETASKS=!desktopicon` must create none.
- **Publish fails with `Access to ... dist\win-x64\Punk.exe is denied`**: a Punk instance is running (often the user's). Check `Get-Process Punk`; ask the user to close it - do not kill a process you did not start.
- A repo-root `dist/` from an earlier build can hold stale files; the package step rebuilds `dist/release` from scratch, but `dist/win-x64` is only overwritten by the publish.

- **The taskbar icon belongs to the terminal host, not the app.** A console app has no window of its own. Under Windows Terminal (the default terminal on Windows 11 here) the taskbar shows the terminal icon, and Punk cannot change it. So the installer's shortcuts and `[Run]` start `{sys}\conhost.exe "{app}\Punk.exe"` with `IconFilename` set to `Punk.exe`, which gives a classic console window with the Punk icon. `Console.Title` is set to "Punk" in `Punk/Program.cs`. To check a build: start the shortcut, then read the window icon with `WM_GETICON`.

- **Installed copies only learn about a release on their next update check.** `MainWindow.CheckForUpdatesAsync` checks at most every 6 hours (`UpdateCheckInterval`), and it stores `LastUpdateCheck` in `%APPDATA%\Punk\config.json`. A release published inside that window is invisible until the window closes, so a "why no notice?" report is usually this. F5 forces a check (at most every 2 minutes, `ManualUpdateCheckGap`). Check the state with `gh release view vX --json assets` and the config file. A running build only has the F5 change if it was built after it, so the change reaches users only in the next release.

## Smoke tests (after a build)
1. `dotnet build Punk/Punk.csproj` must be clean (0 errors).
2. Windows: `.\dist\win-x64\Punk.exe` in PowerShell, or the headless self-test (see `punk-tui-selftest`).
3. Linux in WSL (Ubuntu stands in for Zorin): `wsl -d Ubuntu-22.04 -e bash /mnt/c/Users/sebsz/source/punk/scripts/linux_test.sh`. It must end with `exited cleanly`.
4. Checksums: each line of `SHA256SUMS` must match `Get-FileHash` for that file.

## Other gotchas
- `InvariantGlobalization=true` is set in the csproj so the Linux binary needs no libicu. Keep it unless culture support is added.
- Trimming is NOT enabled; Terminal.Gui uses reflection, so only enable it after a full UI test.
- Native AOT cannot cross-compile OS to OS, so it is not used.
- Do not use `Remove-Item` on temp dirs in this environment (it can be blocked as a "system path"); create uniquely named temp dirs instead.
- Console output is in Polish (locale); match on stable tokens like `error`, not on localized text.
