# Assemble a transferable Windows 11 test bundle without installing anything on the host.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Package,
    [Parameter(Mandatory)][string]$PreviousPackage,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$PythonArchive,
    [switch]$IncludeTestCertificates,
    [switch]$PrepareSandbox,
    [ValidatePattern('^[A-Za-z0-9_-]{11}$')]
    [string]$MediaSmokeVideoId = 'dQw4w9WgXcQ'
)

$ErrorActionPreference = 'Stop'
$pythonUrl = 'https://www.python.org/ftp/python/3.13.17/python-3.13.17-embed-amd64.zip'
$pythonHash = '9E35319BFFEDAB8B36AD44D8F377CB4F1733BCEC257A0B6D04D94F01486DDB38'
$root = [IO.Path]::GetFullPath($OutputDirectory).TrimEnd('\')
$results = "$root-results"
$configuration = "$root.wsb"
$requiredNewPaths = @($root)
if ($PrepareSandbox) { $requiredNewPaths += @($results, $configuration) }
foreach ($path in $requiredNewPaths) {
    if (Test-Path -LiteralPath $path) { throw "Choose a new output path: $path" }
}

$sources = [ordered]@{
    'candidate.msix' = (Resolve-Path -LiteralPath $Package).ProviderPath
    'historical.msix' = (Resolve-Path -LiteralPath $PreviousPackage).ProviderPath
}
$testCertificates = @{}
foreach ($source in $sources.Values) {
    if ([IO.Path]::GetExtension($source) -ine '.msix') { throw 'Provide signed .msix files.' }
    $signature = Get-AuthenticodeSignature -LiteralPath $source
    if ($signature.Status -ne 'Valid' -or $null -eq $signature.SignerCertificate) {
        throw "Package signature is not valid on the staging machine: $source"
    }
    $certificate = $signature.SignerCertificate
    if ($certificate.Subject -ceq $certificate.Issuer) {
        if (-not $IncludeTestCertificates) {
            throw 'A package uses a self-signed certificate. Use -IncludeTestCertificates for disposable-guest testing.'
        }
        $testCertificates[$certificate.Thumbprint] = $certificate
    }
}

$archive = $null
try {
    if ($PythonArchive) {
        $zip = (Resolve-Path -LiteralPath $PythonArchive).ProviderPath
    } else {
        $archive = Join-Path ([IO.Path]::GetTempPath()) "muses-clean-python-$([Guid]::NewGuid().ToString('N')).zip"
        Invoke-WebRequest -Uri $pythonUrl -OutFile $archive -UseBasicParsing -TimeoutSec 90
        $zip = $archive
    }
    if ((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ne $pythonHash) {
        throw 'The Python embeddable archive does not match the pinned SHA-256.'
    }
    $null = New-Item -ItemType Directory -Path $root
    Expand-Archive -LiteralPath $zip -DestinationPath (Join-Path $root 'python')
    $python = Join-Path $root 'python\python.exe'
    $fixtureDirectory = Join-Path $root 'historical-fixture'
    $null = New-Item -ItemType Directory -Path $fixtureDirectory
    & $python -I -B (Join-Path $PSScriptRoot 'package-smoke.py') prepare-historical-roaming $fixtureDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Could not prepare the SQLite fixture from the historical Git revision.' }

    foreach ($file in $sources.Keys) {
        Copy-Item -LiteralPath $sources[$file] -Destination (Join-Path $root $file)
    }
    foreach ($file in @('test-windows11-clean.ps1', 'test-msix.ps1', 'package-smoke.py')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $root
    }
    $certificateFiles = @(
        foreach ($certificate in $testCertificates.Values) {
            $name = "test-signer-$($certificate.Thumbprint).cer"
            [IO.File]::WriteAllBytes((Join-Path $root $name), $certificate.Export('Cert'))
            $name
        }
    )
    $entries = @(
        foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -File -Force | Sort-Object FullName) {
            [ordered]@{
                path = $file.FullName.Substring($root.Length + 1).Replace('\', '/')
                sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
            }
        }
    )
    [ordered]@{ version = 1; files = $entries; testCertificates = $certificateFiles } |
        ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $root 'test-bundle.json') -Encoding UTF8

    if ($PrepareSandbox) {
        $null = New-Item -ItemType Directory -Path $results
        $trust = if ($certificateFiles) { ' -TrustTestCertificates' } else { '' }
        $command = "powershell.exe -NoLogo -NoExit -ExecutionPolicy Bypass -File C:\MusesTestInput\test-windows11-clean.ps1 -DisposableGuest$trust -MediaSmokeVideoId $MediaSmokeVideoId -TranscriptPath C:\MusesTestOutput\clean-win11.log"
        $inputXml = [Security.SecurityElement]::Escape($root)
        $resultsXml = [Security.SecurityElement]::Escape($results)
        $commandXml = [Security.SecurityElement]::Escape($command)
        [xml]$sandbox = @"
<Configuration>
  <Networking>Enable</Networking>
  <AudioInput>Disable</AudioInput>
  <VideoInput>Disable</VideoInput>
  <PrinterRedirection>Disable</PrinterRedirection>
  <ClipboardRedirection>Disable</ClipboardRedirection>
  <MappedFolders>
    <MappedFolder><HostFolder>$inputXml</HostFolder><SandboxFolder>C:\MusesTestInput</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
    <MappedFolder><HostFolder>$resultsXml</HostFolder><SandboxFolder>C:\MusesTestOutput</SandboxFolder><ReadOnly>false</ReadOnly></MappedFolder>
  </MappedFolders>
  <LogonCommand><Command>$commandXml</Command></LogonCommand>
</Configuration>
"@
        $sandbox.Save($configuration)
        Write-Host "Start $configuration. Evidence will be saved in $results."
    } else {
        Write-Host "Transfer $root to a disposable Windows 11 x64 desktop guest."
    }
} finally {
    if ($archive -and (Test-Path -LiteralPath $archive)) { Remove-Item -LiteralPath $archive -Force }
}
