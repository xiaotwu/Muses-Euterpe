# Package Muses for Windows 11 x64. Without a signing certificate, the output is for inspection.
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(?:\.[0-9]+)?$')]
    [string]$Version = "0.1.0",
    [ValidateSet("win-x64")]
    [string]$Runtime = "win-x64",
    [string]$OutputDir = (Join-Path (Split-Path -Parent $PSScriptRoot) "artifacts\msix"),
    [string]$SdkToolsDir,
    [string]$PythonExecutable = "python",
    [ValidatePattern('^[0-9A-Fa-f]{40}$')]
    [string]$CertificateThumbprint
)

$ErrorActionPreference = "Stop"
if ($env:OS -ne "Windows_NT") {
    throw "Run MSIX packaging on Windows with the Windows SDK tools."
}

$appVersion = [version]$Version
$versionParts = @($appVersion.Major, $appVersion.Minor, $appVersion.Build, [Math]::Max(0, $appVersion.Revision))
$manifestVersion = $versionParts -join "."
if (($versionParts | Measure-Object -Maximum).Maximum -gt [uint16]::MaxValue -or $manifestVersion -eq "0.0.0.0") {
    throw "Use an MSIX version greater than zero with components from 0 through 65535."
}

function Find-SdkTool([string]$Name) {
    if ($SdkToolsDir) {
        return (Get-Item -LiteralPath (Join-Path $SdkToolsDir $Name)).FullName
    }

    $toolOnPath = Get-Command -Name $Name -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($toolOnPath) { return $toolOnPath.Source }

    $programFilesX86 = [Environment]::GetEnvironmentVariable("ProgramFiles(x86)")
    $kitTool = Get-ChildItem -File -Path "$programFilesX86\Windows Kits\10\bin\*\x64\$Name" -ErrorAction SilentlyContinue |
        Sort-Object { [version]$_.Directory.Parent.Name } -Descending |
        Select-Object -First 1
    if ($kitTool) { return $kitTool.FullName }

    $nugetPackages = $env:NUGET_PACKAGES
    if (-not $nugetPackages) { $nugetPackages = Join-Path $env:USERPROFILE ".nuget\packages" }
    $nugetTool = Get-ChildItem -File -Path "$nugetPackages\microsoft.windows.sdk.buildtools\*\bin\*\x64\$Name" -ErrorAction SilentlyContinue |
        Sort-Object { [version]$_.Directory.Parent.Name } -Descending |
        Select-Object -First 1
    if ($nugetTool) { return $nugetTool.FullName }

    throw "Cannot find $Name. Install the Windows SDK or restore Microsoft.Windows.SDK.BuildTools and set -SdkToolsDir."
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$appDir = Join-Path $repoRoot "src\Muses.App"
$appProject = Join-Path $appDir "Muses.App.csproj"
$helperProject = Join-Path $repoRoot "src\Muses.WebHome.Helper\Muses.WebHome.Helper.csproj"
[xml]$manifest = Get-Content -LiteralPath (Join-Path $appDir "Package.appxmanifest") -Raw
$manifest.Package.Identity.SetAttribute("Version", $manifestVersion)
$makeAppx = Find-SdkTool "makeappx.exe"

if ($CertificateThumbprint) {
    $signTool = Find-SdkTool "signtool.exe"
    $certificate = Get-Item -LiteralPath "Cert:\CurrentUser\My\$CertificateThumbprint"
    if (-not $certificate.HasPrivateKey -or $certificate.Subject -cne $manifest.Package.Identity.Publisher) {
        throw "Use a signing certificate with a private key and subject $($manifest.Package.Identity.Publisher)."
    }
}

$tempRoot = [IO.Path]::TrimEndingDirectorySeparator(
    (Resolve-Path -LiteralPath ([IO.Path]::GetTempPath())).ProviderPath
)
$scratch = Join-Path $tempRoot "muses-msix-$([guid]::NewGuid().ToString('N'))"
$publishDir = Join-Path $scratch "payload"
$artifactDir = Join-Path $scratch "dotnet"
$packageName = "Muses-$appVersion-$Runtime.msix"
$stagedPackage = Join-Path $scratch $packageName

try {
    Write-Host "Publishing Muses and its isolated Web Home helper ($Runtime, $Configuration)..."
    $publishOptions = @(
        "-c", $Configuration, "-r", $Runtime, "--self-contained",
        "--artifacts-path", $artifactDir, "-p:Version=$appVersion"
    )
    dotnet publish $appProject @publishOptions -o $publishDir | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "App publish failed (exit $LASTEXITCODE)." }
    dotnet publish $helperProject @publishOptions -o (Join-Path $publishDir "Helpers") | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Web Home helper publish failed (exit $LASTEXITCODE)." }

    foreach ($file in @("Muses.exe", "coreclr.dll", "Helpers\MusesWebHomeHelper.exe", "Helpers\coreclr.dll")) {
        if (-not (Test-Path -LiteralPath (Join-Path $publishDir $file) -PathType Leaf)) {
            throw "Missing $file in the self-contained MSIX payload."
        }
    }

    # Remove checkout-local binaries before staging the pinned media tools.
    foreach ($file in @("yt-dlp", "yt-dlp.exe", "mpv.exe", "d3dcompiler_43.dll")) {
        $mediaPath = Join-Path $publishDir $file
        if (Test-Path -LiteralPath $mediaPath) { Remove-Item -LiteralPath $mediaPath -Force }
    }
    $mediaDir = Join-Path $publishDir "MediaTools"
    & $PythonExecutable (Join-Path $PSScriptRoot "stage-media-tools.py") $Runtime $mediaDir | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Media tools staging failed (exit $LASTEXITCODE)." }
    foreach ($file in @("mpv.exe", "yt-dlp.exe", "deno.exe")) {
        if (-not (Test-Path -LiteralPath (Join-Path $mediaDir $file) -PathType Leaf)) {
            throw "Missing $file in the MSIX media payload."
        }
    }
    & $PythonExecutable (Join-Path $PSScriptRoot "check-media-tools.py") --media-dir $mediaDir | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Packaged media tools check failed (exit $LASTEXITCODE)." }

    $manifest.Save((Join-Path $publishDir "AppxManifest.xml"))
    $assetsParent = Join-Path $publishDir "Assets"
    [IO.Directory]::CreateDirectory($assetsParent) | Out-Null
    Copy-Item -LiteralPath (Join-Path $appDir "Assets\Packaging") -Destination $assetsParent -Recurse
    Copy-Item -LiteralPath (Join-Path $repoRoot "LICENSE") -Destination $publishDir

    $packLog = & $makeAppx pack /d $publishDir /p $stagedPackage
    if ($LASTEXITCODE -ne 0) {
        $packLog | Out-Host
        throw "MakeAppx pack failed (exit $LASTEXITCODE)."
    }

    if ($CertificateThumbprint) {
        & $signTool sign /fd SHA256 /sha1 $certificate.Thumbprint $stagedPackage | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "SignTool failed (exit $LASTEXITCODE)." }
    }

    [IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($OutputDir)) | Out-Null
    $packagePath = Join-Path $OutputDir $packageName
    Copy-Item -LiteralPath $stagedPackage -Destination $packagePath -Force
    if (-not $CertificateThumbprint) {
        Write-Warning "This unsigned package is for structural verification. Sign with the manifest publisher's certificate for installation."
    }
    Write-Output (Get-Item -LiteralPath $packagePath).FullName
} finally {
    if (Test-Path -LiteralPath $scratch) {
        $resolvedScratch = (Resolve-Path -LiteralPath $scratch).ProviderPath
        if ($resolvedScratch -ne $scratch -or (Split-Path -Parent $resolvedScratch) -ne $tempRoot) {
            throw "Refusing to remove unexpected MSIX staging directory $resolvedScratch."
        }
        Remove-Item -LiteralPath $resolvedScratch -Recurse -Force
    }
}
