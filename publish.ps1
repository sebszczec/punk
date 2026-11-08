<#
.SYNOPSIS
Builds Punk for Windows and Linux, creates the Windows installer and the update files, and optionally publishes a GitHub release.

Run without arguments for an interactive menu: create a new version (with a proposed number checked against GitHub),
or release a version that was built earlier.

.EXAMPLE
./publish.ps1                         # interactive menu
./publish.ps1 -CheckVersion           # show the version on GitHub and the suggested next version
./publish.ps1 -Version 1.2.0          # build 1.2.0 into dist/ (nothing is published)
./publish.ps1 -Bump patch -Release    # bump the patch number, build, and publish to GitHub
./publish.ps1 -NonInteractive         # no menu: build the version in Punk.csproj
#>
[CmdletBinding()]
param(
    [string]$Version,
    [ValidateSet('major', 'minor', 'patch')][string]$Bump,
    [switch]$CheckVersion,
    [switch]$Release,
    [string]$Notes,
    [switch]$Yes,
    [switch]$NonInteractive
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$repo = 'sebszczec/punk'
$csprojPath = "$PSScriptRoot\Punk\Punk.csproj"
$distDir = "$PSScriptRoot\dist"
$releaseDir = "$distDir\release"
$buildMarker = "$releaseDir\build.json"
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

# --- Version helpers --------------------------------------------------------

function Get-SemVer([string]$text) {
    if ($text -match '^(\d+)\.(\d+)\.(\d+)$') { return [version]::new([int]$Matches[1], [int]$Matches[2], [int]$Matches[3]) }
    return $null
}

function Format-SemVer([version]$v) { "$($v.Major).$($v.Minor).$($v.Build)" }

function Get-CsprojVersion {
    Get-SemVer ([regex]::Match((Get-Content $csprojPath -Raw), '<Version>([^<]+)</Version>').Groups[1].Value)
}

function Set-CsprojVersion([version]$v) {
    $text = [regex]::Replace((Get-Content $csprojPath -Raw), '<Version>[^<]+</Version>', "<Version>$(Format-SemVer $v)</Version>")
    [IO.File]::WriteAllText($csprojPath, $text)
    Write-Host "Punk.csproj version set to $(Format-SemVer $v)"
}

function Get-NextVersions([version]$base) {
    if (-not $base) { return '0.0.1 (patch) / 0.1.0 (minor) / 1.0.0 (major)' }
    $patch = [version]::new($base.Major, $base.Minor, $base.Build + 1)
    $minor = [version]::new($base.Major, $base.Minor + 1, 0)
    $major = [version]::new($base.Major + 1, 0, 0)
    "$(Format-SemVer $patch) (patch) / $(Format-SemVer $minor) (minor) / $(Format-SemVer $major) (major)"
}

# Asks GitHub: latest release and all v* tags on origin. Known=$false when GitHub cannot be asked.
function Get-GitHubState {
    $state = [ordered]@{ Known = $false; Latest = $null; Tags = @(); Reason = '' }
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { $state.Reason = 'gh CLI is not installed'; return $state }
    gh auth status *> $null
    if ($LASTEXITCODE -ne 0) { $state.Reason = 'gh is not logged in (run: gh auth login)'; return $state }

    $remoteTags = git ls-remote --tags origin 'v*' 2>$null
    if ($LASTEXITCODE -ne 0) { $state.Reason = 'cannot reach origin'; return $state }
    $state.Tags = @($remoteTags | ForEach-Object { if ($_ -match 'refs/tags/v(\d+\.\d+\.\d+)$') { Get-SemVer $Matches[1] } } | Where-Object { $_ })

    # "release not found" is normal before the first release; keep gh's stderr from aborting the script.
    $ErrorActionPreference = 'Continue'
    $latestTag = gh release view --repo $repo --json tagName --jq .tagName 2>$null
    $ErrorActionPreference = 'Stop'
    if ($LASTEXITCODE -eq 0 -and $latestTag) { $state.Latest = Get-SemVer $latestTag.TrimStart('v') }

    $state.Known = $true
    return $state
}

function Get-HighestPublished($state) {
    $all = @($state.Tags) + @($state.Latest) | Where-Object { $_ }
    if ($all) { $all | Sort-Object -Descending | Select-Object -First 1 } else { $null }
}

# Throws when $target cannot be published. Strict mode (used with -Release) also fails when GitHub cannot be asked.
function Assert-VersionFree([version]$target, $state, [bool]$strict) {
    $tag = "v$(Format-SemVer $target)"
    if (-not $state.Known) {
        $msg = "Cannot check GitHub versions: $($state.Reason)."
        if ($strict) { throw "$msg Version $target was not validated, so nothing was published." }
        Write-Warning "$msg Version $target was not validated against GitHub."
        return
    }

    $highest = Get-HighestPublished $state
    $localTag = [bool](git tag -l $tag)
    if ($state.Tags -contains $target -or $localTag) {
        throw "Tag $tag already exists. Pick a higher version. Next: $(Get-NextVersions $highest)."
    }
    if ($highest -and $target -le $highest) {
        throw "$(Format-SemVer $target) is not newer than the latest release v$(Format-SemVer $highest). Next: $(Get-NextVersions $highest)."
    }
    if ($highest) {
        $bigJump = ($target.Major - $highest.Major -ge 2) -or (($target.Major -eq $highest.Major) -and ($target.Minor - $highest.Minor -ge 10))
        if ($bigJump) {
            Write-Warning "$(Format-SemVer $target) skips far ahead of v$(Format-SemVer $highest). Check for a typo."
            if (-not $Yes -and (Read-Host 'Continue with this version? (y/N)') -ne 'y') { throw 'Aborted.' }
        }
    }
}

# --- Prompts ----------------------------------------------------------------

# Reads one answer from the list (case-insensitive). Enter gives $default; anything else asks again.
function Read-Choice([string]$prompt, [string[]]$allowed, [string]$default) {
    while ($true) {
        $answer = (Read-Host $prompt).Trim().ToLower()
        if (-not $answer) { $answer = $default }
        if ($allowed -contains $answer) { return $answer }
        Write-Host "  Please answer one of: $($allowed -join ', ')" -ForegroundColor Yellow
    }
}

# --- Build marker -----------------------------------------------------------

function Read-BuildMarker {
    if (-not (Test-Path $buildMarker)) { return $null }
    try { Get-Content $buildMarker -Raw | ConvertFrom-Json } catch { $null }
}

function Get-ReleaseAssets([string]$v) {
    @("$releaseDir\punk-win-x64.zip", "$releaseDir\punk-linux-x64.zip", "$releaseDir\Setup-Punk-$v.exe")
}

# --- Release preconditions ----------------------------------------------------

function Test-ReleasePreconditions {
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw 'gh CLI is required to release (winget install GitHub.cli).' }
    $branch = git rev-parse --abbrev-ref HEAD
    if ($branch -ne 'main') { throw "Release from main (current branch: $branch)." }
    $dirty = git status --porcelain | Where-Object { $_ -notmatch 'Punk/Punk\.csproj$' }
    if ($dirty) { throw "Working tree has uncommitted changes (only Punk.csproj may differ):`n$($dirty -join "`n")" }
}

# --- Interactive menu -------------------------------------------------------

function Show-Status($state, [version]$current) {
    Write-Host ""
    Write-Host "Punk status" -ForegroundColor Cyan
    Write-Host "  csproj version : $(Format-SemVer $current)"
    if ($state.Known) {
        $highest = Get-HighestPublished $state
        Write-Host "  latest release : $(if ($state.Latest) { 'v' + (Format-SemVer $state.Latest) } else { 'none' })"
        Write-Host "  highest tag    : $(if ($highest) { 'v' + (Format-SemVer $highest) } else { 'none' })"
    } else {
        Write-Warning "GitHub not checked: $($state.Reason)."
    }
    Write-Host "  branch         : $(git rev-parse --abbrev-ref HEAD)"
    $marker = Read-BuildMarker
    if ($marker) { Write-Host "  built          : v$($marker.version) (not released yet, see option 2)" }
    Write-Host ""
}

# Option 1: propose a version (checked against GitHub), or let the user type one.
function New-VersionMenu($state, [version]$current) {
    $highest = Get-HighestPublished $state
    if ($highest) {
        $suggest = [ordered]@{
            '1' = [version]::new($highest.Major, $highest.Minor, $highest.Build + 1)
            '2' = [version]::new($highest.Major, $highest.Minor + 1, 0)
            '3' = [version]::new($highest.Major + 1, 0, 0)
        }
        $labels = @{ '1' = 'patch'; '2' = 'minor'; '3' = 'major' }
        Write-Host "Suggested versions (latest on GitHub: v$(Format-SemVer $highest)):"
    } else {
        $suggest = [ordered]@{ '1' = $current }
        $labels = @{ '1' = 'first release' }
        Write-Host "Nothing is published yet. Suggested first version (from Punk.csproj):"
    }
    foreach ($k in $suggest.Keys) {
        Write-Host "  [$k] $(Format-SemVer $suggest[$k])  $($labels[$k])$(if ($k -eq '1') { ' (Enter)' })"
    }
    Write-Host "  [t] type another number"
    Write-Host "  [q] cancel"
    $pick = Read-Choice '> ' (@($suggest.Keys) + @('t', 'q')) '1'
    if ($pick -eq 'q') { return @{ Mode = 'quit' } }

    if ($pick -eq 't') {
        $target = $null
        while (-not $target) {
            $text = (Read-Host 'Version (x.y.z, empty = cancel)').Trim()
            if (-not $text) { return @{ Mode = 'quit' } }
            $candidate = Get-SemVer $text
            if (-not $candidate) { Write-Host "  '$text' is not x.y.z" -ForegroundColor Yellow; continue }
            try {
                Assert-VersionFree $candidate $state $false
                $target = $candidate
            } catch {
                Write-Host "  $($_.Exception.Message)" -ForegroundColor Yellow
            }
        }
    } else {
        $target = $suggest[$pick]
    }

    $now = (Read-Choice 'Release to GitHub now, or build only and release later? [n]ow / [l]ater (Enter)' @('n', 'l') 'l') -eq 'n'
    $notes = $null
    if ($now) {
        $notes = (Read-Host 'Release notes (Enter = generated from commits)').Trim()
        if (-not $notes) { $notes = $null }
    }
    return @{ Mode = 'build'; Version = $target; Release = $now; Notes = $notes }
}

# Option 2: release the build in dist/release, if it still matches the code.
function Release-BuiltMenu($state) {
    $marker = Read-BuildMarker
    $assetsOk = $marker -and (Get-ReleaseAssets $marker.version | ForEach-Object { Test-Path $_ }) -notcontains $false
    if (-not $assetsOk -or -not (Test-Path "$releaseDir\SHA256SUMS")) {
        Write-Host 'Nothing is built yet. Choose 1 to build a version.' -ForegroundColor Yellow
        return @{ Mode = 'quit' }
    }

    $head = (git rev-parse HEAD).Trim()
    if ($head -ne $marker.commit) {
        Write-Host "The code changed after v$($marker.version) was built (built at $($marker.commit.Substring(0, 7)), now $($head.Substring(0, 7)))." -ForegroundColor Yellow
        Write-Host 'Choose 1 to build it again.' -ForegroundColor Yellow
        return @{ Mode = 'quit' }
    }

    $version = Get-SemVer $marker.version
    try {
        Assert-VersionFree $version $state $true
    } catch {
        Write-Host "  $($_.Exception.Message)" -ForegroundColor Yellow
        return @{ Mode = 'quit' }
    }
    $notes = (Read-Host 'Release notes (Enter = generated from commits)').Trim()
    if (-not $notes) { $notes = $null }
    return @{ Mode = 'release'; Version = $version; Notes = $notes }
}

function Invoke-Menu([version]$current) {
    $state = Get-GitHubState
    Show-Status $state $current
    Write-Host 'What do you want to do?'
    Write-Host '  [1] Create a new version'
    Write-Host '  [2] Release a version that was already built'
    Write-Host '  [q] Quit'
    switch (Read-Choice '> ' @('1', '2', 'q') '') {
        '1' { return New-VersionMenu $state $current }
        '2' { return Release-BuiltMenu $state }
        default { return @{ Mode = 'quit' } }
    }
}

# --- Dependencies -----------------------------------------------------------
# Installs whatever is missing via winget. Installing needs an elevated terminal; nothing is installed when all is present.

function Install-Winget($id, $what) {
    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) { throw "$what is missing and winget is not available - install it manually." }
    if (-not $isAdmin) { throw "$what is missing. Re-run this script from an administrator terminal to install it." }
    Write-Host "Installing $what ($id)..."
    winget install --id $id -e --accept-source-agreements --accept-package-agreements | Out-Host  # keep winget's text out of the function's return value
    if ($LASTEXITCODE -ne 0) { throw "winget failed to install $what (exit $LASTEXITCODE)" }
}

function Find-Iscc {
    # Any "Inno Setup N" folder (6, 7, ...) in the usual install locations; the newest one wins.
    $roots = "${env:ProgramFiles(x86)}", $env:ProgramFiles, "$env:LOCALAPPDATA\Programs"
    Get-ChildItem -Path $roots -Filter 'Inno Setup *' -Directory -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending |
        ForEach-Object { Join-Path $_.FullName 'ISCC.exe' } |
        Where-Object { Test-Path $_ } |
        Select-Object -First 1
}

# Returns the path to ISCC.exe.
function Install-BuildDependencies {
    # .NET 10 SDK (build + publish)
    $sdk = if (Get-Command dotnet -ErrorAction SilentlyContinue) { dotnet --list-sdks | Where-Object { $_ -match '^10\.' } } else { $null }
    if (-not $sdk) {
        Install-Winget 'Microsoft.DotNet.SDK.10' '.NET 10 SDK'
        $env:PATH = "$env:ProgramFiles\dotnet;$env:PATH"   # pick up the fresh install in this session
    }

    # PawnIO driver: lets LibreHardwareMonitor read CPU temperatures (the old WinRing0 driver is blocked by Memory Integrity)
    $pawn = (Get-Service PawnIO -ErrorAction SilentlyContinue) -or (Test-Path "$env:ProgramFiles\PawnIO")
    if (-not $pawn) { Install-Winget 'namazso.PawnIO' 'PawnIO driver' }

    # Inno Setup (compiles the Windows installer)
    $iscc = Find-Iscc
    if (-not $iscc) {
        Install-Winget 'JRSoftware.InnoSetup' 'Inno Setup 6'
        $iscc = Find-Iscc
    }
    if (-not $iscc) { throw 'Inno Setup 6 (ISCC.exe) was not found, even after installing it.' }
    return $iscc
}

# --- Build ------------------------------------------------------------------

function Invoke-Build([version]$target) {
    $v = Format-SemVer $target
    $iscc = Install-BuildDependencies

    if ((Get-CsprojVersion) -ne $target) { Set-CsprojVersion $target }

    Write-Host "Building Punk $v"
    foreach ($rid in 'win-x64', 'linux-x64') {
        dotnet publish "$PSScriptRoot\Punk\Punk.csproj" -c Release -r $rid --self-contained `
            -p:PublishSingleFile=true -p:Version=$v -o "$distDir\$rid"
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish ($rid) failed (exit $LASTEXITCODE)" }
    }

    # Package: update zips, installer, checksums.
    Remove-Item $releaseDir -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path $releaseDir | Out-Null
    Add-Type -AssemblyName System.IO.Compression.FileSystem

    # Update zips: files at the zip root, which is what the in-app updater expects.
    foreach ($rid in 'win-x64', 'linux-x64') {
        [IO.Compression.ZipFile]::CreateFromDirectory("$distDir\$rid", "$releaseDir\punk-$rid.zip",
            [IO.Compression.CompressionLevel]::Optimal, $false)
    }

    & $iscc "/DVersion=$v" "/DSourceDir=$distDir\win-x64" "/DOutputDir=$releaseDir" "$PSScriptRoot\installer\punk.iss"
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed (exit $LASTEXITCODE)" }

    $assets = Get-ReleaseAssets $v
    $sumLines = foreach ($a in $assets) { "$((Get-FileHash $a -Algorithm SHA256).Hash.ToLower())  $(Split-Path $a -Leaf)" }
    [IO.File]::WriteAllText("$releaseDir\SHA256SUMS", (($sumLines -join "`n") + "`n"))

    # Remember what was built, so option 2 can refuse a release when the code has changed since.
    $marker = [ordered]@{ version = $v; commit = (git rev-parse HEAD).Trim(); builtAt = (Get-Date).ToString('o') }
    [IO.File]::WriteAllText($buildMarker, ($marker | ConvertTo-Json))

    Write-Host "Built into ${releaseDir}:"
    Get-ChildItem $releaseDir | ForEach-Object { Write-Host "  $($_.Name)" }
}

# --- Publish to GitHub ------------------------------------------------------

# One line per commit since the previous release (or the last 50 commits for the first release).
function Get-ReleaseNotes($previous, [string]$tag) {
    $range = if ($previous) { "v$(Format-SemVer $previous)..HEAD" } else { 'HEAD' }
    $lines = git log --max-count 50 '--pretty=format:- %s' $range
    if ($lines) { $lines -join "`n" } else { "Punk $tag" }
}

function Publish-Release([version]$version, [string]$notes) {
    $v = Format-SemVer $version
    $tag = "v$v"
    $assets = Get-ReleaseAssets $v
    foreach ($a in $assets) { if (-not (Test-Path $a)) { throw "Missing $(Split-Path $a -Leaf). Build the version first (option 1)." } }

    Write-Host ""
    Write-Host "Publish Punk $v to github.com/$repo as release $tag with:"
    $assets | ForEach-Object { Write-Host "  $(Split-Path $_ -Leaf)" }
    if (-not $Yes -and (Read-Host 'Publish now? (y/N)') -ne 'y') { throw 'Aborted. Nothing was published.' }

    # Re-check: a release may have been published while the build ran.
    $state = Get-GitHubState
    Assert-VersionFree $version $state $true
    $previous = Get-HighestPublished $state

    # Punk.csproj must carry this version in the release commit.
    if ((Get-CsprojVersion) -ne $version) { Set-CsprojVersion $version }
    if (git status --porcelain -- Punk/Punk.csproj) {
        git add Punk/Punk.csproj
        git commit -m "Release $tag" -m "Co-Authored-By: Claude Haiku 5.5 <noreply@anthropic.com>" -m "Claude-Session: https://claude.ai/code/session_01B6UQ7e8aw4QoApAPsUPgsE"
        if ($LASTEXITCODE -ne 0) { throw 'git commit failed.' }
    }
    git tag -a $tag -m "Punk $tag"
    git push origin main
    if ($LASTEXITCODE -ne 0) { throw 'git push (main) failed. Tag is only local; fix and re-run.' }
    git push origin $tag
    if ($LASTEXITCODE -ne 0) { throw "git push ($tag) failed." }

    # Draft first, so releases/latest (used by the in-app updater) never points at a half-uploaded release.
    # Notes are written here, not with gh's --generate-notes, which failed on this machine.
    $notesText = if ($notes) { $notes } else { Get-ReleaseNotes $previous $tag }
    gh release create $tag @assets --repo $repo --draft --title "Punk $tag" --notes $notesText
    if ($LASTEXITCODE -ne 0) { throw "gh release create failed. Delete the draft release and tag $tag on GitHub before retrying." }
    gh release edit $tag --repo $repo --draft=false
    if ($LASTEXITCODE -ne 0) { throw "Assets uploaded, but publishing the draft failed. Publish it on GitHub." }

    Write-Host "Published: https://github.com/$repo/releases/tag/$tag"
}

# --- Main -------------------------------------------------------------------

$current = Get-CsprojVersion
if (-not $current) { throw 'Punk/Punk.csproj has no valid <Version>x.y.z</Version>.' }

if ($CheckVersion) {
    $state = Get-GitHubState
    Write-Host "Version in Punk.csproj : $(Format-SemVer $current)"
    if (-not $state.Known) {
        Write-Warning "GitHub not checked: $($state.Reason)."
        exit 0
    }
    $highest = Get-HighestPublished $state
    Write-Host "Latest GitHub release  : $(if ($state.Latest) { 'v' + (Format-SemVer $state.Latest) } else { 'none' })"
    Write-Host "Highest tag on origin  : $(if ($highest) { 'v' + (Format-SemVer $highest) } else { 'none' })"
    Write-Host "Next version           : $(Get-NextVersions $highest)"
    if ($highest -and $current -le $highest) {
        Write-Host "Punk.csproj is not newer than GitHub. Bump it before publishing." -ForegroundColor Yellow
        exit 1
    }
    exit 0
}

$interactive = -not ($Version -or $Bump -or $Release -or $NonInteractive)
if ($interactive) {
    $plan = Invoke-Menu $current
} else {
    if ($Version -and $Bump) { throw 'Use -Version or -Bump, not both.' }
    if ($Version) {
        $target = Get-SemVer $Version
        if (-not $target) { throw "'$Version' is not a version in x.y.z form." }
    } elseif ($Bump) {
        $target = switch ($Bump) {
            'major' { [version]::new($current.Major + 1, 0, 0) }
            'minor' { [version]::new($current.Major, $current.Minor + 1, 0) }
            'patch' { [version]::new($current.Major, $current.Minor, $current.Build + 1) }
        }
    } else {
        $target = $current
    }
    $plan = @{ Mode = 'build'; Version = $target; Release = [bool]$Release; Notes = $Notes }
}

if ($plan.Mode -eq 'quit') { exit 0 }

$target = $plan.Version
$doRelease = $plan.Mode -eq 'release' -or [bool]$plan.Release
if ($doRelease) { Test-ReleasePreconditions }

if ($plan.Mode -eq 'build') {
    # Validate before anything is built or written.
    Assert-VersionFree $target (Get-GitHubState) $doRelease
    Invoke-Build $target
}

if ($doRelease) {
    Publish-Release $target $plan.Notes
} else {
    $v = Format-SemVer $target
    if ($interactive) {
        Write-Host "Built v$v into dist/release. Nothing was published. Run this script again and choose 2 to release it."
    } else {
        Write-Host "Nothing was published. To release: ./publish.ps1 -Version $v -Release"
    }
}

if (-not $isAdmin) { Write-Host "Note: run Punk from an administrator terminal to see CPU temperatures." }
