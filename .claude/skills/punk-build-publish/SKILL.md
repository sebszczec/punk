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
3. Windows smoke test: `.\dist\win-x64\Punk.exe` in PowerShell, or the headless self-test (see `punk-tui-selftest`).
4. Linux smoke test in WSL (Ubuntu is a close stand-in for Zorin): `wsl -d Ubuntu-22.04 -e bash /mnt/c/Users/sebsz/source/punk/scripts/linux_test.sh`. It must end with `exited cleanly`.

## Gotchas
- **Publish fails with `Access to ... dist\win-x64\Punk.exe is denied`**: a Punk instance is running (often the user's). Check `Get-Process Punk`; ask the user to close it - do not kill a process you did not start. Test runs from `dotnet run` use `Punk/bin/...`, not `dist/`.
- `InvariantGlobalization=true` is set in the csproj so the Linux binary needs no libicu. Keep it unless culture support is added.
- Trimming is NOT enabled; Terminal.Gui uses reflection, so only enable it after a full UI test.
- Do not use `Remove-Item` on temp dirs in this environment (it can be blocked as a "system path"); create uniquely named temp dirs instead.
- Console output is in Polish (locale); match on stable tokens like `error`, not on localized text.
