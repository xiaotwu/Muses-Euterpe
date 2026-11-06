# Package Muses for Windows 11 x64. Production signing is an explicit opt-in.
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
    [string]$CertificateThumbprint,
    [switch]$ProductionSigning,
    [string]$Publisher,
    [uri]$TimestampUrl,
    [ValidatePattern('^[0-9A-Fa-f]{64}$')]
    [string]$TrustedRootSha256,
    [string]$SigningDlib,
    [string]$SigningMetadata
)

$ErrorActionPreference = "Stop"
if ($env:OS -ne "Windows_NT") {
    throw "Run MSIX packaging on Windows with the Windows SDK tools."
}

$useSigningProvider = $SigningDlib -or $SigningMetadata
if ($ProductionSigning) {
    if ($Configuration -ne "Release") {
        throw "Production signing requires a Release build."
    }
    if ([string]::IsNullOrWhiteSpace($Publisher) -or -not $TimestampUrl -or -not $TrustedRootSha256) {
        throw "Production signing requires -Publisher, -TimestampUrl and -TrustedRootSha256."
    }
    if (-not $TimestampUrl.IsAbsoluteUri -or $TimestampUrl.Scheme -ne "https" -or $TimestampUrl.UserInfo) {
        throw "Production signing requires an HTTPS timestamp URL without embedded credentials."
    }
    if ($useSigningProvider) {
        if ($CertificateThumbprint -or -not $SigningDlib -or -not $SigningMetadata) {
            throw "Use either -CertificateThumbprint or both -SigningDlib and -SigningMetadata."
        }
        $dlibFile = Get-Item -LiteralPath $SigningDlib
        $metadataFile = Get-Item -LiteralPath $SigningMetadata
        if ($dlibFile.PSIsContainer -or $metadataFile.PSIsContainer) {
            throw "The signing provider DLL and metadata must be files."
        }
        $signingArguments = @("/dlib", $dlibFile.FullName, "/dmdf", $metadataFile.FullName)
    } elseif (-not $CertificateThumbprint) {
        throw "Production signing requires a certificate thumbprint or a signing provider."
    }
} elseif ($Publisher -or $TimestampUrl -or $TrustedRootSha256 -or $useSigningProvider) {
    throw "Publisher, timestamp, trusted-root and signing-provider overrides require -ProductionSigning."
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

function Assert-ProductionSignature([string]$Package, [string]$SignTool) {
    & $SignTool verify /pa /all /tw /v $Package | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "SignTool signature verification failed (exit $LASTEXITCODE)." }

    $signature = Get-AuthenticodeSignature -LiteralPath $Package
    if ($signature.Status -ne "Valid" -or -not $signature.SignerCertificate -or
        $signature.SignerCertificate.Subject -cne $Publisher -or -not $signature.TimeStamperCertificate) {
        throw "Expected a valid timestamped package signature from publisher $Publisher."
    }

    $chain = [Security.Cryptography.X509Certificates.X509Chain]::new()
    try {
        $chain.ChainPolicy.ApplicationPolicy.Add([Security.Cryptography.Oid]::new("1.3.6.1.5.5.7.3.3")) | Out-Null
        $chain.ChainPolicy.RevocationMode = "Online"
        $chain.ChainPolicy.UrlRetrievalTimeout = [TimeSpan]::FromSeconds(15)
        if (-not $chain.Build($signature.SignerCertificate) -or $chain.ChainElements.Count -lt 2) {
            $status = ($chain.ChainStatus | ForEach-Object { $_.Status }) -join ", "
            throw "Production signing requires a trusted issuing certificate chain. Chain status: $status."
        }
        $root = $chain.ChainElements[$chain.ChainElements.Count - 1].Certificate
        $rootSha256 = $root.GetCertHashString([Security.Cryptography.HashAlgorithmName]::SHA256)
        if ($rootSha256 -ine $TrustedRootSha256) {
            throw "The production signing chain root SHA-256 does not match the expected root."
        }
    } finally {
        $chain.Dispose()
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$appDir = Join-Path $repoRoot "src\Muses.App"
$appProject = Join-Path $appDir "Muses.App.csproj"
$helperProject = Join-Path $repoRoot "src\Muses.WebHome.Helper\Muses.WebHome.Helper.csproj"
[xml]$manifest = Get-Content -LiteralPath (Join-Path $appDir "Package.appxmanifest") -Raw
$manifest.Package.Identity.SetAttribute("Version", $manifestVersion)
if ($ProductionSigning) {
    if ($Publisher -cne $manifest.Package.Identity.Publisher) {
        Write-Warning "The production publisher changes the package family. This package cannot update an installation from the development family."
    }
    $manifest.Package.Identity.SetAttribute("Publisher", $Publisher)
}
$makeAppx = Find-SdkTool "makeappx.exe"

if ($CertificateThumbprint -or $ProductionSigning) {
    $signTool = Find-SdkTool "signtool.exe"
}
if ($CertificateThumbprint) {
    $certificate = Get-Item -LiteralPath "Cert:\CurrentUser\My\$CertificateThumbprint"
    if (-not $certificate.HasPrivateKey -or $certificate.Subject -cne $manifest.Package.Identity.Publisher) {
        throw "Use a signing certificate with a private key and subject $($manifest.Package.Identity.Publisher)."
    }
    if ($ProductionSigning -and $certificate.Subject -ceq $certificate.Issuer) {
        throw "Production signing requires an issuing certificate chain."
    }
    $signingArguments = @("/sha1", $certificate.Thumbprint)
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

    if ($ProductionSigning -or $CertificateThumbprint) {
        if ($ProductionSigning) {
            $signingArguments += @("/tr", $TimestampUrl.AbsoluteUri, "/td", "SHA256")
        }
        & $signTool sign /fd SHA256 @signingArguments $stagedPackage | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "SignTool failed (exit $LASTEXITCODE)." }
        if ($ProductionSigning) { Assert-ProductionSignature $stagedPackage $signTool }
    }

    [IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($OutputDir)) | Out-Null
    $packagePath = Join-Path $OutputDir $packageName
    Copy-Item -LiteralPath $stagedPackage -Destination $packagePath -Force
    if (-not $CertificateThumbprint -and -not $ProductionSigning) {
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
