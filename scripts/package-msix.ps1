# Muses Windows 11 MSIX Packaging Script
# Note: Store/WinGet are NOT published. Run this on Windows only — macOS cannot produce MSIX.
# Usage:
#   powershell -ExecutionPolicy Bypass -File scripts\package-msix.ps1 [-Configuration Release] [-Version 0.1.0.0]

param(
    [string]$Configuration = "Release",
    [string]$Version = "0.1.0.0",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent $ScriptDir
$AppProj = Join-Path $RepoRoot "src\Muses.App\Muses.App.csproj"
$PublishDir = Join-Path $RepoRoot "artifacts\publish\Muses"
$MsixOutDir = Join-Path $RepoRoot "artifacts\msix"

Write-Host "==> Publishing Muses ($Runtime, $Configuration)..." -ForegroundColor Cyan
if (Test-Path $PublishDir) { Remove-Item -Recurse -Force $PublishDir }
New-Item -ItemType Directory -Force -Path $PublishDir | Out-Null
New-Item -ItemType Directory -Force -Path $MsixOutDir | Out-Null

dotnet publish $AppProj -c $Configuration -r $Runtime --self-contained false -o $PublishDir /p:PublishSingleFile=false

Write-Host "==> Copying Package.appxmanifest and packaging assets..." -ForegroundColor Cyan
Copy-Item (Join-Path $RepoRoot "src\Muses.App\Package.appxmanifest") (Join-Path $PublishDir "AppxManifest.xml") -Force
$AssetsSrc = Join-Path $RepoRoot "src\Muses.App\Assets\Packaging"
$AssetsDst = Join-Path $PublishDir "Assets\Packaging"
New-Item -ItemType Directory -Force -Path $AssetsDst | Out-Null
Copy-Item -Recurse -Force "$AssetsSrc\*" $AssetsDst

# Locate Windows SDK MakeAppx.exe if available
$makeAppx = (Get-ChildItem -Path "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\makeappx.exe" -ErrorAction SilentlyContinue | Select-Object -Last 1).FullName

if ($makeAppx) {
    Write-Host "==> Creating MSIX package using $makeAppx..." -ForegroundColor Green
    $msixPath = Join-Path $MsixOutDir "Muses-$Version-$Runtime.msix"
    & $makeAppx pack /d $PublishDir /p $msixPath /o
    Write-Host "==> Successfully created MSIX: $msixPath" -ForegroundColor Green
} else {
    Write-Host "==> Windows SDK MakeAppx.exe not found. Files are prepared in $PublishDir for packaging." -ForegroundColor Yellow
    Write-Host "==> You can package manually using: MakeAppx.exe pack /d `"$PublishDir`" /p `"$MsixOutDir\Muses.msix`"" -ForegroundColor Yellow
}
