---
name: punk-build-publish
description: Build, publish and smoke-test the Punk TUI (C#/.NET 10 + Terminal.Gui 2.5) for Windows (win-x64) and Linux/Zorin (linux-x64). Use when asked to build, publish, ship, package or verify Punk on both platforms, or when publish fails.
---

# Build and publish Punk

Repo: `C:\Users\sebsz\source\punk` (`Punk/` project, `publish.ps1`, `scripts/linux_test.sh`, `dist/` is git-ignored).
Requires .NET SDK 10 (a 6.0 SDK is also installed; `dotnet --version` should say 10.x).

## Steps
1. `dotnet build Punk/Punk.csproj` - must be clean (0 errors).
2. `./publish.ps1` from the repo root. It publishes self-contained single-file builds to `dist/win-x64/Punk.exe` and `dist/linux-x64/Punk` (~80 MB each, plus a small `libonigwrap` next to each - ship both files). Native AOT cannot cross-compile OS to OS, so it is not used.
   It also writes `dist/release/`: `Setup-Punk-<v>.exe` (Inno Setup per-user installer, `installer/punk.iss`), `punk-win-x64.zip`, `punk-linux-x64.zip` and `SHA256SUMS`.

### Versions and releases
- `./publish.ps1` with no arguments - interactive menu: `[1]` create a new version (proposes patch/minor/major checked against GitHub, or a typed number, then release now or later), `[2]` release a version already built in `dist/release` (refused if the commit changed since the build; `dist/release/build.json` records the build).
- `-NonInteractive` - no menu, builds the version in `Punk.csproj`.
- `./publish.ps1 -CheckVersion` - shows the csproj version, the latest GitHub release, the highest `v*` tag on origin and the suggested next versions. Exit 1 if the csproj is not newer than GitHub.
- `./publish.ps1 -Version 1.2.0` or `-Bump patch|minor|major` - sets `<Version>` in `Punk/Punk.csproj` and builds. Nothing is published.
- `./publish.ps1 -Bump patch -Release` - builds, then (after a confirmation) commits the csproj as `Release vX.Y.Z`, tags `vX.Y.Z`, pushes `main` and the tag, and creates the GitHub release with the installer and update files. The release is created as a draft and published only after all assets are uploaded.
- Version validation runs before any build: a version that is not newer than GitHub's, or whose tag exists, is refused. A big jump asks for confirmation. Strict with `-Release`: GitHub must be reachable and `gh` logged in.
- `-Release` needs: branch `main`, no uncommitted changes apart from `Punk.csproj`, `gh auth status` ok.
- A run that fails after the tag is pushed leaves a tag and maybe a draft release: delete both on GitHub and retry, or use the next version.
3. Windows smoke test: `.\dist\win-x64\Punk.exe` in PowerShell, or the headless self-test (see `punk-tui-selftest`).
4. Linux smoke test in WSL (Ubuntu is a close stand-in for Zorin): `wsl -d Ubuntu-22.04 -e bash /mnt/c/Users/sebsz/source/punk/scripts/linux_test.sh`. It must end with `exited cleanly`.

## Gotchas
- **Publish fails with `Access to ... dist\win-x64\Punk.exe is denied`**: a Punk instance is running (often the user's). Check `Get-Process Punk`; ask the user to close it - do not kill a process you did not start. Test runs from `dotnet run` use `Punk/bin/...`, not `dist/`.
- `InvariantGlobalization=true` is set in the csproj so the Linux binary needs no libicu. Keep it unless culture support is added.
- Trimming is NOT enabled; Terminal.Gui uses reflection, so only enable it after a full UI test.
- Do not use `Remove-Item` on temp dirs in this environment (it can be blocked as a "system path"); create uniquely named temp dirs instead.
- Console output is in Polish (locale); match on stable tokens like `error`, not on localized text.
