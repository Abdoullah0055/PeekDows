<#
.SYNOPSIS
    Publishes PeekDows as a self-contained, single-file Windows x64 build.

.DESCRIPTION
    Produces a portable build (no .NET runtime required on the target machine)
    into artifacts\publish\win-x64. This is the input consumed by the Inno Setup
    installer (installer\PeekDows.iss).

.PARAMETER Configuration
    Build configuration. Defaults to Release.

.PARAMETER Runtime
    Target RID. Defaults to win-x64.

.PARAMETER OutputDir
    Output directory. Defaults to <repo>\artifacts\publish\win-x64.

.EXAMPLE
    .\scripts\publish-windows.ps1
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$OutputDir = ''
)

$ErrorActionPreference = 'Stop'

# Resolve the script directory robustly: $PSScriptRoot is empty when the script
# is launched via "powershell -File <path>" (without dot-sourcing), so fall back
# to $MyInvocation.MyCommand.Path.
$scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$repoRoot = Split-Path -Parent $scriptDir
Set-Location -Path $repoRoot

# Default output dir is computed here (not in param default) so it works even
# when $PSScriptRoot was empty at bind time.
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $repoRoot (Join-Path 'artifacts' (Join-Path 'publish' 'win-x64'))
}

$project = 'src\PeekDows.App\PeekDows.App.csproj'
$OutputDir = [System.IO.Path]::GetFullPath($OutputDir)

Write-Host 'Publishing PeekDows (self-contained, single-file)...' -ForegroundColor Cyan
Write-Host "  Project : $project"
Write-Host "  Config  : $Configuration"
Write-Host "  Runtime : $Runtime"
Write-Host "  Output  : $OutputDir"
Write-Host ''

# Clean previous output so stale files (renamed/removed assets) don't leak in.
if (Test-Path $OutputDir) {
    Write-Host 'Cleaning previous publish output...' -ForegroundColor DarkGray
    Remove-Item -Recurse -Force $OutputDir
}

dotnet publish $project `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o $OutputDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish failed with exit code $LASTEXITCODE."
    exit $LASTEXITCODE
}

# The single-file publish pipeline drops the tray icon assets (.ico is also embedded
# as the PE icon via <ApplicationIcon>, so the SDK treats the loose copy as redundant
# and skips it; the .png is dropped with it). TrayIconController loads the icon at
# runtime from AppContext.BaseDirectory\Assets\peekdows_tray_icon_bw.ico, which for a
# single-file app resolves to the directory next to the .exe. Copy the Assets folder
# here so the installed/installed app shows the real icon instead of the Windows default.
$assetsSource = Join-Path $repoRoot (Join-Path 'src' (Join-Path 'PeekDows.App' 'Assets'))
$assetsDest = Join-Path $OutputDir 'Assets'
if (Test-Path $assetsSource) {
    if (-not (Test-Path $assetsDest)) {
        New-Item -ItemType Directory -Path $assetsDest | Out-Null
    }
    Copy-Item -Path (Join-Path $assetsSource '*') -Destination $assetsDest -Recurse -Force
    Write-Host "Copied tray icon assets to $assetsDest" -ForegroundColor DarkGray
} else {
    Write-Warning "Assets source not found: $assetsSource"
}

$exe = Join-Path $OutputDir 'PeekDows.App.exe'
if (-not (Test-Path $exe)) {
    Write-Error "Expected output not found: $exe"
    exit 1
}

Write-Host ''
Write-Host 'Publish succeeded.' -ForegroundColor Green
Write-Host "Portable build: $exe" -ForegroundColor Green
Write-Host ''
Write-Host 'Next step: generate the installer with Inno Setup:' -ForegroundColor Cyan
Write-Host '    ISCC.exe installer\PeekDows.iss'
