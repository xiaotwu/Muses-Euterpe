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

Write-Host "==> Publishing isolated Web Home helper next to the app..." -ForegroundColor Cyan
$HelperProj = Join-Path $RepoRoot "src\Muses.WebHome.Helper\Muses.WebHome.Helper.csproj"
$HelperOut = Join-Path $PublishDir "Helpers"
New-Item -ItemType Directory -Force -Path $HelperOut | Out-Null
dotnet publish $HelperProj -c $Configuration -r $Runtime --self-contained false -o $HelperOut /p:PublishSingleFile=false

Write-Host "==> Copying Package.appxmanifest and packaging assets..." -ForegroundColor Cyan
Copy-Item (Join-Path $RepoRoot "src\Muses.App\Package.appxmanifest") (Join-Path $PublishDir "AppxManifest.xml") -Force
$AssetsSrc = Join-Path $RepoRoot "src\Muses.App\Assets\Packaging"
$AssetsDst = Join-Path $PublishDir "Assets\Packaging"
New-Item -ItemType Directory -Force -Path $AssetsDst | Out-Null
Copy-Item -Recurse -Force "$AssetsSrc\*" $AssetsDst

function Find-SdkTool([string]$name) {
    $fromKits = Get-ChildItem -Path "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\$name" -ErrorAction SilentlyContinue | Select-Object -Last 1
    if ($fromKits) { return $fromKits.FullName }
    $nugetRoot = Join-Path $env:USERPROFILE ".nuget\packages\microsoft.windows.sdk.buildtools"
    $fromNuget = Get-ChildItem -Path $nugetRoot -Recurse -Filter $name -ErrorAction SilentlyContinue |
        Where-Object { $_.Directory.Name -eq "x64" } |
        Select-Object -Last 1
    if ($fromNuget) { return $fromNuget.FullName }
    return $null
}

function Get-MusesSideloadCertificate {
    $existing = Get-ChildItem Cert:\CurrentUser\My |
        Where-Object {
            $_.Subject -eq "CN=xiaotwu" -and
            $_.HasPrivateKey -and
            $_.NotAfter -gt (Get-Date) -and
            ($_.EnhancedKeyUsageList | Where-Object { $_.ObjectId -eq "1.3.6.1.5.5.7.3.3" })
        } |
        Select-Object -First 1
    if ($existing) { return $existing }

    Write-Host "==> Creating local CN=xiaotwu code-signing certificate (sideload, not Store)..." -ForegroundColor Cyan
    return New-SelfSignedCertificate `
        -Type Custom `
        -Subject "CN=xiaotwu" `
        -FriendlyName "Muses Euterpe Sideload" `
        -KeyUsage DigitalSignature `
        -HashAlgorithm SHA256 `
        -KeyLength 2048 `
        -NotAfter (Get-Date).AddYears(5) `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
}

function Install-MusesSideloadTrust($cert) {
    $cerPath = Join-Path $MsixOutDir "Muses-sideload.cer"
    Export-Certificate -Cert $cert -FilePath $cerPath | Out-Null

    $userPeople = Get-ChildItem Cert:\CurrentUser\TrustedPeople -ErrorAction SilentlyContinue |
        Where-Object { $_.Thumbprint -eq $cert.Thumbprint }
    if (-not $userPeople) {
        try {
            Import-Certificate -FilePath $cerPath -CertStoreLocation Cert:\CurrentUser\TrustedPeople | Out-Null
            Write-Host "==> Trusted sideload cert in CurrentUser\TrustedPeople ($($cert.Thumbprint))" -ForegroundColor Green
        } catch {
            Write-Host "==> CurrentUser\TrustedPeople import failed: $($_.Exception.Message)" -ForegroundColor Yellow
        }
    }

    # AppX sideload requires the signing root in LocalMachine Trusted People (admin).
    $machinePeople = Get-ChildItem Cert:\LocalMachine\TrustedPeople -ErrorAction SilentlyContinue |
        Where-Object { $_.Thumbprint -eq $cert.Thumbprint }
    if (-not $machinePeople) {
        try {
            Import-Certificate -FilePath $cerPath -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
            Write-Host "==> Trusted sideload cert in LocalMachine\TrustedPeople" -ForegroundColor Green
        } catch {
            Write-Host "==> LocalMachine\TrustedPeople needs elevation. One-time admin:" -ForegroundColor Yellow
            Write-Host "    Import-Certificate -FilePath `"$cerPath`" -CertStoreLocation Cert:\LocalMachine\TrustedPeople" -ForegroundColor Yellow
            Write-Host "    Add-AppxPackage -Path `"$(Join-Path $MsixOutDir "Muses-$Version-$Runtime.msix")`"" -ForegroundColor Yellow
        }
    }
}

# Locate MakeAppx.exe: Windows SDK, then NuGet Microsoft.Windows.SDK.BuildTools (no full SDK install required).
$makeAppx = Find-SdkTool "makeappx.exe"

if ($makeAppx) {
    Write-Host "==> Creating MSIX package using $makeAppx..." -ForegroundColor Green
    $msixPath = Join-Path $MsixOutDir "Muses-$Version-$Runtime.msix"
    & $makeAppx pack /d $PublishDir /p $msixPath /o
    if ($LASTEXITCODE -ne 0) { throw "MakeAppx pack failed with exit $LASTEXITCODE" }
    Write-Host "==> Successfully created MSIX: $msixPath" -ForegroundColor Green

    $signTool = Find-SdkTool "signtool.exe"
    if (-not $signTool) {
        Write-Host "==> SignTool not found. Package is unsigned; SmartScreen / untrusted-publisher will block sideload." -ForegroundColor Yellow
    } else {
        $cert = Get-MusesSideloadCertificate
        Install-MusesSideloadTrust $cert
        Write-Host "==> Signing $msixPath with CN=xiaotwu ($($cert.Thumbprint))..." -ForegroundColor Cyan
        & $signTool sign /fd SHA256 /td SHA256 /sha1 $cert.Thumbprint /v $msixPath
        if ($LASTEXITCODE -ne 0) { throw "SignTool failed with exit $LASTEXITCODE" }
        & $signTool verify /pa $msixPath
        if ($LASTEXITCODE -ne 0) {
            Write-Host "==> Signature verify /pa reported a problem (often missing root). Sideload still works if TrustedPeople has this cert." -ForegroundColor Yellow
        } else {
            Write-Host "==> Signed and verified: $msixPath" -ForegroundColor Green
        }
    }
} else {
    Write-Host "==> Windows SDK MakeAppx.exe not found. Files are prepared in $PublishDir for packaging." -ForegroundColor Yellow
    Write-Host "==> You can package manually using: MakeAppx.exe pack /d `"$PublishDir`" /p `"$MsixOutDir\Muses.msix`"" -ForegroundColor Yellow
}
