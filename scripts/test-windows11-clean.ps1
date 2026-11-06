# Run only on a disposable, clean Windows 11 desktop guest.
[CmdletBinding()]
param(
    [switch]$DisposableGuest,
    [switch]$TrustTestCertificates,
    [Parameter(Mandatory)]
    [ValidatePattern('^[A-Za-z0-9_-]{11}$')]
    [string]$MediaSmokeVideoId,
    [string]$TranscriptPath = (Join-Path ([IO.Path]::GetTempPath()) "muses-win11-$([Guid]::NewGuid().ToString('N')).log")
)

$ErrorActionPreference = 'Stop'
if (-not $DisposableGuest) { throw 'Use -DisposableGuest only inside an isolated Windows 11 guest.' }
if ($PSVersionTable.PSEdition -ne 'Desktop' -or -not [Environment]::UserInteractive -or
    [Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne 'X64') {
    throw 'Use Windows PowerShell 5.1 in an interactive Windows 11 x64 desktop guest.'
}
$os = Get-CimInstance Win32_OperatingSystem
if ($os.ProductType -ne 1 -or [int]$os.BuildNumber -lt 22000) { throw 'A Windows 11 client guest is required.' }
if ((Get-AppxPackage -Name 'xiaotwu.Muses') -or (Get-Process -Name Muses -ErrorAction SilentlyContinue)) {
    throw 'Start the guest from a clean Windows snapshot.'
}

$bundle = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'test-bundle.json') -Raw | ConvertFrom-Json
if ($bundle.version -ne 1) { throw 'Unsupported test bundle format.' }
$entries = @{}
foreach ($file in $bundle.files) {
    if ($file.path -notmatch '^[A-Za-z0-9._-]+(/[A-Za-z0-9._-]+)*$' -or
        $file.path.Split('/') -contains '..' -or $file.path.Split('/') -contains '.' -or
        $file.sha256 -notmatch '^[A-F0-9]{64}$' -or $entries.ContainsKey($file.path)) {
        throw 'The test bundle lists an invalid file.'
    }
    $entries[$file.path] = $file.sha256
}
$actual = @(Get-ChildItem -LiteralPath $PSScriptRoot -Recurse -Force)
if ($actual | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) {
    throw 'The test bundle contains a linked file or directory.'
}
$actual = @($actual | Where-Object {
    -not $_.PSIsContainer -and $_.FullName -ne (Join-Path $PSScriptRoot 'test-bundle.json')
})
if ($actual.Count -ne $entries.Count) { throw 'The test bundle file set changed during transfer.' }
foreach ($file in $actual) {
    $relative = $file.FullName.Substring($PSScriptRoot.Length + 1).Replace('\', '/')
    if ((Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash -ne $entries[$relative]) {
        throw "The test bundle file failed SHA-256 verification: $relative"
    }
}

function Get-MsixSignerThumbprint([string]$PackagePath) {
    $zip = [IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        $entry = $zip.GetEntry('AppxSignature.p7x')
        if (-not $entry -or $entry.Length -lt 5 -or $entry.Length -gt 1MB) { throw 'Invalid MSIX signature.' }
        $stream = $entry.Open()
        $signature = [IO.MemoryStream]::new()
        try {
            $stream.CopyTo($signature)
            $bytes = $signature.ToArray()
        } finally {
            $stream.Dispose()
            $signature.Dispose()
        }
    } finally {
        $zip.Dispose()
    }
    if ([Text.Encoding]::ASCII.GetString($bytes, 0, 4) -cne 'PKCX') { throw 'Invalid MSIX signature header.' }
    $cms = [Security.Cryptography.Pkcs.SignedCms]::new()
    $cms.Decode([byte[]]$bytes[4..($bytes.Length - 1)])
    $cms.CheckSignature($true)
    foreach ($signer in $cms.SignerInfos) { $signer.Certificate.Thumbprint }
}

$certificates = @($bundle.testCertificates | Where-Object { $_ })
if ($certificates -and -not $TrustTestCertificates) {
    throw 'This bundle contains test signers. Use -TrustTestCertificates from an elevated guest session.'
}

$added = [Collections.Generic.List[string]]::new()
$workDirectory = Join-Path ([IO.Path]::GetTempPath()) "muses-win11-packages-$([Guid]::NewGuid().ToString('N'))"
$candidate = Join-Path $workDirectory 'candidate.msix'
$baseline = Join-Path $workDirectory 'historical.msix'
$createdWorkDirectory = $false
Start-Transcript -LiteralPath $TranscriptPath -NoClobber | Out-Null
try {
    Write-Host "Guest OS $($os.Caption), build $($os.BuildNumber), x64"
    Write-Host "Candidate SHA-256 $($entries['candidate.msix'])"
    Write-Host "Historical package SHA-256 $($entries['historical.msix'])"
    # The deployment service can open local files regardless of how the guest receives its inputs.
    $null = New-Item -ItemType Directory -Path $workDirectory
    $createdWorkDirectory = $true
    foreach ($path in @($candidate, $baseline)) {
        $name = Split-Path -Leaf $path
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $path
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entries[$name]) {
            throw "The local installer failed SHA-256 verification: $name"
        }
    }
    if ($certificates) {
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
        $principal = [Security.Principal.WindowsPrincipal]::new($identity)
        if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
            throw 'Open an elevated Windows PowerShell session inside the guest to trust the test signers.'
        }
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        Add-Type -AssemblyName System.Security
        $packageSigners = @(Get-MsixSignerThumbprint $candidate; Get-MsixSignerThumbprint $baseline)
        foreach ($name in $certificates) {
            if ($name -notmatch '^test-signer-[A-F0-9]{40}\.cer$' -or -not $entries.ContainsKey($name)) {
                throw 'Unexpected certificate in the test bundle.'
            }
            $file = Join-Path $PSScriptRoot $name
            $certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($file)
            if ($certificate.Subject -cne $certificate.Issuer -or
                $certificate.Thumbprint -notin $packageSigners -or $name -cne "test-signer-$($certificate.Thumbprint).cer") {
                throw 'The certificate does not match a self-signed package in this bundle.'
            }
            $storePath = "Cert:\LocalMachine\TrustedPeople\$($certificate.Thumbprint)"
            if (-not (Test-Path -LiteralPath $storePath)) {
                $added.Add($storePath)
                Import-Certificate -FilePath $file -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null
            }
        }
    }
    foreach ($path in @($candidate, $baseline)) {
        if ((Get-AuthenticodeSignature -LiteralPath $path).Status -ne 'Valid') {
            throw "The guest does not trust the signature: $path"
        }
    }
    $arguments = @{
        Package = $candidate
        PythonExecutable = (Join-Path $PSScriptRoot 'python\python.exe')
    }
    $smoke = Join-Path $PSScriptRoot 'test-msix.ps1'
    & $smoke @arguments -MediaSmokeVideoId $MediaSmokeVideoId
    $arguments.PreviousPackage = $baseline
    $arguments.HistoricalRoaming = $true
    $arguments.HistoricalFixture = Join-Path $PSScriptRoot 'historical-fixture\muses-youtube-native.sqlite'
    & $smoke @arguments
    Write-Host 'PASS: direct installation, first playback, resume, and historical upgrade.'
} finally {
    try {
        foreach ($path in $added) {
            if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path }
        }
    } finally {
        try {
            if ($createdWorkDirectory) {
                foreach ($path in @($candidate, $baseline)) {
                    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path }
                }
                [IO.Directory]::Delete($workDirectory)
            }
        } finally {
            Stop-Transcript | Out-Null
        }
    }
}
Write-Host "Evidence: $TranscriptPath"
