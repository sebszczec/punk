$ErrorActionPreference = 'Stop'

# --- Dependencies -----------------------------------------------------------
# Installs whatever is missing via winget. Installing needs an elevated terminal; nothing is installed when all is present.
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole('Administrator')

function Install-Winget($id, $what) {
    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) { throw "$what is missing and winget is not available - install it manually." }
    if (-not $isAdmin) { throw "$what is missing. Re-run this script from an administrator terminal to install it." }
    Write-Host "Installing $what ($id)..."
    winget install --id $id -e --accept-source-agreements --accept-package-agreements
    if ($LASTEXITCODE -ne 0) { throw "winget failed to install $what (exit $LASTEXITCODE)" }
}

# .NET 10 SDK (build + publish)
$sdk = if (Get-Command dotnet -ErrorAction SilentlyContinue) { dotnet --list-sdks | Where-Object { $_ -match '^10\.' } } else { $null }
if (-not $sdk) {
    Install-Winget 'Microsoft.DotNet.SDK.10' '.NET 10 SDK'
    $env:PATH = "$env:ProgramFiles\dotnet;$env:PATH"   # pick up the fresh install in this session
}

# PawnIO driver: lets LibreHardwareMonitor read CPU temperatures (the old WinRing0 driver is blocked by Memory Integrity)
$pawn = (Get-Service PawnIO -ErrorAction SilentlyContinue) -or (Test-Path "$env:ProgramFiles\PawnIO")
if (-not $pawn) { Install-Winget 'namazso.PawnIO' 'PawnIO driver' }

# --- Publish ----------------------------------------------------------------
foreach ($rid in 'win-x64', 'linux-x64') {
    dotnet publish "$PSScriptRoot\Punk\Punk.csproj" -c Release -r $rid --self-contained `
        -p:PublishSingleFile=true -o "$PSScriptRoot\dist\$rid"
}

if (-not $isAdmin) { Write-Host "Note: run Punk from an administrator terminal to see CPU temperatures." }
