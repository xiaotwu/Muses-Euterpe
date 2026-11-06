[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Package,
    [string]$PreviousPackage,
    [string]$PythonExecutable = 'python.exe',
    [ValidatePattern('^[A-Za-z0-9_-]{11}$')]
    [string]$MediaSmokeVideoId,
    [switch]$HistoricalRoaming,
    [string]$HistoricalFixture,
    [ValidateNotNullOrEmpty()]
    [string]$ExpectedPublisher = 'CN=xiaotwu',
    [ValidatePattern('^xiaotwu\.Muses_[a-z0-9]{13}$')]
    [string]$ExpectedPackageFamilyName = 'xiaotwu.Muses_r58mek0329vyw'
)

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -ne 'Desktop') {
    throw 'Run this check in Windows PowerShell 5.1 (powershell.exe).'
}
if (-not [Environment]::UserInteractive -or [Environment]::OSVersion.Version.Build -lt 22000 -or
    [Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne 'X64') {
    throw 'Run this check from an interactive Windows 11 x64 desktop session.'
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
if ($PSBoundParameters.ContainsKey('ExpectedPublisher') -ne $PSBoundParameters.ContainsKey('ExpectedPackageFamilyName')) {
    throw 'Pass -ExpectedPublisher and -ExpectedPackageFamilyName together.'
}
if ($HistoricalRoaming -and
    ($ExpectedPublisher -cne 'CN=xiaotwu' -or $ExpectedPackageFamilyName -cne 'xiaotwu.Muses_r58mek0329vyw')) {
    throw 'Historical Roaming verification requires the original test publisher and package family.'
}
$packageName = 'xiaotwu.Muses'
$packageFamily = $ExpectedPackageFamilyName
$publisher = $ExpectedPublisher
$appId = 'App'
$appExecutable = 'Muses.exe'

function Read-TestPackage([string]$PackagePath, [switch]$RequireMedia) {
    $resolvedPath = (Resolve-Path -LiteralPath $PackagePath).ProviderPath
    if ([IO.Path]::GetExtension($resolvedPath) -ne '.msix') { throw "Expected an MSIX file: $resolvedPath" }
    $zip = [IO.Compression.ZipFile]::OpenRead($resolvedPath)
    try {
        $entry = $zip.GetEntry('AppxManifest.xml')
        if (-not $entry) { throw "Missing AppxManifest.xml in $resolvedPath" }
        $reader = [IO.StreamReader]::new($entry.Open())
        try { [xml]$manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
        $identity = $manifest.Package.Identity
        $application = $manifest.Package.Applications.Application
        if ($identity.Name -cne $packageName -or $identity.Publisher -cne $publisher -or
            $identity.ProcessorArchitecture -cne 'x64' -or $application.Id -cne $appId -or
            $application.Executable -cne $appExecutable) {
            throw "Unexpected Muses package identity or application in $resolvedPath"
        }
        if ($RequireMedia) {
            foreach ($file in @('mpv.exe', 'yt-dlp.exe', 'deno.exe')) {
                if (-not $zip.GetEntry("MediaTools/$file")) {
                    throw "Missing packaged media tool $file in $resolvedPath."
                }
            }
        }
        return [pscustomobject]@{ Path = $resolvedPath; Version = [version]$identity.Version }
    } finally {
        $zip.Dispose()
    }
}

$candidate = Read-TestPackage $Package -RequireMedia:([bool]$MediaSmokeVideoId)
$first = $candidate
if ($PreviousPackage) {
    $first = Read-TestPackage $PreviousPackage
    if ($first.Version -ge $candidate.Version) { throw 'The previous MSIX must be older than the candidate.' }
}
if ((Get-AppxPackage -Name $packageName) -or (Get-Process -Name Muses -ErrorAction SilentlyContinue)) {
    throw 'Use a clean test user without an existing Muses package or running app.'
}
if ($HistoricalRoaming -and -not $PreviousPackage) {
    throw 'Historical Roaming verification requires the previous signed 6fabd52 package.'
}
if ($HistoricalFixture) {
    if (-not $HistoricalRoaming) { throw '-HistoricalFixture requires -HistoricalRoaming.' }
    $HistoricalFixture = (Resolve-Path -LiteralPath $HistoricalFixture).ProviderPath
    if (-not (Test-Path -LiteralPath $HistoricalFixture -PathType Leaf) -or
        (Test-Path -LiteralPath "$HistoricalFixture-wal") -or (Test-Path -LiteralPath "$HistoricalFixture-shm")) {
        throw 'Provide a standalone historical SQLite fixture without journal sidecars.'
    }
}
& $PythonExecutable -c 'import sqlite3'
if ($LASTEXITCODE -ne 0) { throw 'A Python 3 interpreter with sqlite3 is required. Pass -PythonExecutable if needed.' }

$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
$workDir = Join-Path $tempRoot "muses-msix-test-$([Guid]::NewGuid().ToString('N'))"
$dataDir = Join-Path $workDir 'data'
$cacheDir = Join-Path $workDir 'cache'
$playbackDataDir = Join-Path $workDir 'playback-data'
$pidFile = Join-Path $workDir 'app.pid'
$launchError = Join-Path $workDir 'launch-error.txt'
$exitFile = Join-Path $workDir 'app-exit.txt'
$installStarted = $false
$profileRoots = [Collections.Generic.List[string]]::new()
$fixtureOwner = [Guid]::NewGuid().ToString('N')
$ownerMarker = '.muses-msix-fixture'
$roamingRoot = [Environment]::GetFolderPath('ApplicationData')
$localRoot = [Environment]::GetFolderPath('LocalApplicationData')
$roamingFixture = Join-Path $roamingRoot 'Muses'
$localFixture = Join-Path $localRoot 'Muses'
$packageState = Join-Path (Join-Path $localRoot 'Packages') $packageFamily

if ($HistoricalRoaming) {
    foreach ($path in @($roamingFixture, $localFixture, $packageState)) {
        if (Get-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue) {
            throw 'Use a clean test user without existing default or package-private Muses data.'
        }
    }
    $credentials = & cmdkey.exe /list
    if ($LASTEXITCODE -ne 0 -or ($credentials -match 'Muses/YouTubeOAuth/')) {
        throw 'Use a clean test user without Muses Credential Manager entries.'
    }
    $dataDir = $roamingFixture
}

function Get-ProfileEntries([string]$Root) {
    $pending = [Collections.Generic.Stack[string]]::new()
    $pending.Push($Root)
    while ($pending.Count -gt 0) {
        foreach ($entry in Get-ChildItem -LiteralPath ($pending.Pop()) -Force) {
            $entry
            if ($entry.PSIsContainer -and -not ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                $pending.Push($entry.FullName)
            }
        }
    }
}

function New-ProfileFixture([string]$Path) {
    New-Item -ItemType Directory -Path $Path | Out-Null
    [IO.File]::WriteAllText((Join-Path $Path $ownerMarker), $fixtureOwner)
    $profileRoots.Add($Path)
}

function Assert-PhysicalHistoricalDatabase {
    $fileName = 'muses-youtube-native.sqlite'
    $database = Join-Path $roamingFixture $fileName
    if (-not (Test-Path -LiteralPath $database -PathType Leaf)) {
        throw 'Expected the physical SQLite fixture in %APPDATA%\Muses.'
    }
    $privateRoot = Get-Item -LiteralPath $packageState -Force -ErrorAction SilentlyContinue
    if ($privateRoot -and ($privateRoot.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'Cannot inspect a linked package-private state directory.'
    }
    if ($privateRoot -and (Get-ProfileEntries $packageState | Where-Object Name -eq $fileName)) {
        throw 'The package created a private SQLite main database instead of exclusively using the physical fixture.'
    }
    Write-Host 'Verified %APPDATA%\Muses\muses-youtube-native.sqlite and no package-private SQLite main database.'
}

function Invoke-Fixture([string]$Action) {
    & $PythonExecutable (Join-Path $PSScriptRoot 'package-smoke.py') $Action $dataDir
    if ($LASTEXITCODE -ne 0) { throw "SQLite fixture check failed during $Action." }
}

function Install-TestPackage($Release) {
    # Deployment can still hold the test package briefly after its app exits.
    for ($attempt = 1; $attempt -le 6; $attempt++) {
        try {
            Add-AppxPackage -Path $Release.Path
            break
        } catch {
            if ($attempt -eq 6 -or $_.Exception.Message -notmatch '0x80073D02') { throw }
            Start-Sleep -Seconds 1
        }
    }
    $installed = Get-AppxPackage -Name $packageName
    if (-not $installed -or $installed.Version -ne $Release.Version -or $installed.Architecture -ne 'X64' -or
        $installed.PackageFamilyName -cne $packageFamily) {
        throw "Package registration does not match $($Release.Path)."
    }
    return $installed
}

function Invoke-InstalledApp($Installed, [string]$PlaybackMode = '') {
    $executable = Join-Path $Installed.InstallLocation $appExecutable
    foreach ($file in @($pidFile, $launchError, $exitFile, "$exitFile.tmp")) {
        if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file }
    }

    # Start inside the registered package context with isolated playback data and no developer PATH.
    $launcher = @'
$ErrorActionPreference = 'Stop'
try {
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = '<executable>'
    $start.WorkingDirectory = '<workdir>'
    $start.UseShellExecute = $false
    $start.Arguments = '<arguments>'
    if ('<historical>' -eq 'True') {
        $start.EnvironmentVariables.Remove('MUSES_DATA_HOME')
    } else {
        $start.EnvironmentVariables['MUSES_DATA_HOME'] = '<data>'
    }
    if ('<historical>' -eq 'True' -or '<playback-mode>') {
        $start.EnvironmentVariables.Remove('MUSES_CACHE_HOME')
    } else {
        $start.EnvironmentVariables['MUSES_CACHE_HOME'] = '<cache>'
    }
    if ('<isolated-media>' -eq 'True') {
        $start.EnvironmentVariables['PATH'] = "$env:SystemRoot\System32;$env:SystemRoot"
        foreach ($name in @('MUSES_MPV_PATH', 'MUSES_YTDLP_PATH', 'MUSES_DENO_PATH')) {
            $start.EnvironmentVariables.Remove($name)
        }
    }
    $process = [Diagnostics.Process]::Start($start)
    try {
        [IO.File]::WriteAllText('<pid>', [string]$process.Id)
        if ('<playback-mode>') {
            $process.WaitForExit()
            [IO.File]::WriteAllText('<exit>.tmp', [string]$process.ExitCode)
            [IO.File]::Move('<exit>.tmp', '<exit>')
        }
    } finally {
        $process.Dispose()
    }
} catch {
    [IO.File]::WriteAllText('<error>', $_.Exception.ToString())
    exit 1
}
'@
    $values = @{
        '<executable>' = $executable
        '<workdir>' = $workDir
        '<data>' = $(if ($PlaybackMode) { $playbackDataDir } else { $dataDir })
        '<historical>' = [string]([bool]$HistoricalRoaming -and -not $PlaybackMode)
        '<cache>' = $cacheDir
        '<arguments>' = $(if ($PlaybackMode) { "--verify-package-playback $PlaybackMode $MediaSmokeVideoId" } else { '' })
        '<playback-mode>' = $PlaybackMode
        '<isolated-media>' = [string][bool]$MediaSmokeVideoId
        '<pid>' = $pidFile
        '<error>' = $launchError
        '<exit>' = $exitFile
    }
    foreach ($entry in $values.GetEnumerator()) {
        $launcher = $launcher.Replace($entry.Key, $entry.Value.Replace("'", "''"))
    }
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($launcher))
    $command = Join-Path $PSHOME 'powershell.exe'
    Invoke-CommandInDesktopPackage -AppId $appId -PackageFamilyName $Installed.PackageFamilyName `
        -Command $command -Args "-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand $encoded" `
        -PreventBreakaway | Out-Null

    $deadline = [DateTime]::UtcNow.AddSeconds($(if ($PlaybackMode) { 75 } else { 45 }))
    $app = $null
    try {
        while ([DateTime]::UtcNow -lt $deadline) {
            if (Test-Path -LiteralPath $launchError) { throw (Get-Content -LiteralPath $launchError -Raw) }
            if ($PlaybackMode -and (Test-Path -LiteralPath $exitFile)) {
                $exitCode = Get-Content -LiteralPath $exitFile -Raw
                $report = Join-Path $playbackDataDir "playback-smoke-$PlaybackMode.json"
                if (Test-Path -LiteralPath $report -PathType Leaf) {
                    Write-Host (Get-Content -LiteralPath $report -Raw)
                } else {
                    throw "Packaged playback $PlaybackMode produced no report (exit $exitCode)."
                }
                if ($exitCode -ne '0') { throw "Packaged playback $PlaybackMode failed (exit $exitCode)." }
                return
            }
            if (-not $app -and (Test-Path -LiteralPath $pidFile)) {
                $appPid = 0
                if ([int]::TryParse((Get-Content -LiteralPath $pidFile -Raw), [ref]$appPid)) {
                    try {
                        $app = [Diagnostics.Process]::GetProcessById($appPid)
                    } catch [ArgumentException] {
                        if (-not $PlaybackMode) { throw }
                        # The launcher still records the exit code when the process finishes before polling.
                        Start-Sleep -Milliseconds 250
                        continue
                    }
                    if ($app.ProcessName -ne 'Muses') {
                        $app.Dispose()
                        $app = $null
                        throw 'The package launch did not start a Muses process.'
                    }
                    # Keep an open handle so ExitCode remains available after the process exits.
                    $null = $app.Handle
                }
            }
            if ($app -and -not $PlaybackMode) {
                $app.Refresh()
                if ($app.HasExited) { throw "Muses exited before opening its window (code $($app.ExitCode))." }
                if ($app.MainWindowHandle -ne [IntPtr]::Zero -and $app.MainWindowTitle -eq 'Muses') {
                    Write-Host "Opened the Muses desktop window ($($app.Id))."
                    if (-not $app.CloseMainWindow() -or -not $app.WaitForExit(15000)) {
                        throw 'Muses did not close after the desktop exit request.'
                    }
                    if ($app.ExitCode -ne 0) { throw "Muses exited with code $($app.ExitCode)." }
                    return
                }
            }
            Start-Sleep -Milliseconds 250
        }
        if ($PlaybackMode) { throw "Packaged playback $PlaybackMode did not exit within 75 seconds." }
        throw 'No visible Muses window appeared within 45 seconds.'
    } finally {
        if ($app) {
            try {
                if (-not $app.HasExited) {
                    $app.Kill()
                    $app.WaitForExit(5000) | Out-Null
                }
            } finally {
                $app.Dispose()
            }
        }
    }
}

try {
    New-Item -ItemType Directory -Path $workDir, $cacheDir | Out-Null
    if ($HistoricalRoaming) {
        New-ProfileFixture $roamingFixture
        New-ProfileFixture $localFixture
        if ($HistoricalFixture) {
            Copy-Item -LiteralPath $HistoricalFixture -Destination (Join-Path $dataDir 'muses-youtube-native.sqlite')
        } else {
            Invoke-Fixture 'prepare-historical-roaming'
        }
    } else {
        Invoke-Fixture 'prepare'
    }
    $installStarted = $true
    $installed = Install-TestPackage $first
    Invoke-InstalledApp $installed
    if ($HistoricalRoaming) {
        Assert-PhysicalHistoricalDatabase
        Invoke-Fixture 'verify-historical-baseline'
    } else {
        Invoke-Fixture 'seed'
    }

    if ($PreviousPackage) {
        $familyName = $installed.PackageFamilyName
        $installed = Install-TestPackage $candidate
        if ($installed.PackageFamilyName -cne $familyName) { throw 'The upgrade changed the package family.' }
        Invoke-Fixture 'verify-fixture'
    }
    Invoke-InstalledApp $installed
    $verifyAction = 'verify-restored'
    if ($HistoricalRoaming) {
        Assert-PhysicalHistoricalDatabase
        Invoke-Fixture 'verify-historical-roaming-rearm'
        Invoke-InstalledApp $installed
        Assert-PhysicalHistoricalDatabase
        $verifyAction = 'verify-historical-roaming'
    }
    Invoke-Fixture $verifyAction

    if ($MediaSmokeVideoId) {
        Invoke-InstalledApp $installed 'start'
        Invoke-InstalledApp $installed 'resume'
        Write-Host 'Verified packaged first playback and relaunch resume with a system-only PATH.'
    }

    Remove-AppxPackage -Package $installed.PackageFullName
    if (Get-AppxPackage -Name $packageName) { throw 'Muses remains registered after uninstall.' }
    $installStarted = $false
    Invoke-Fixture $verifyAction
    if ($HistoricalRoaming -and (Get-Item -LiteralPath $packageState -Force -ErrorAction SilentlyContinue)) {
        throw 'The package-private state directory remains after uninstall.'
    }
    Write-Host "Muses $($candidate.Version) installed, opened, restored the SQLite fixture and uninstalled."
} finally {
    try {
        if ($installStarted) {
            Get-AppxPackage -Name $packageName | ForEach-Object { Remove-AppxPackage -Package $_.PackageFullName }
        }
    } finally {
        foreach ($path in $profileRoots) {
            $directory = Get-Item -LiteralPath $path -Force
            $marker = Join-Path $path $ownerMarker
            if (($directory.Attributes -band [IO.FileAttributes]::ReparsePoint) -or
                $directory.FullName -cne $path -or $path -notin @($roamingFixture, $localFixture) -or
                (Get-Content -LiteralPath $marker -Raw) -cne $fixtureOwner -or
                (Get-ProfileEntries $path | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint })) {
                throw "Refusing to remove an unexpected profile fixture directory."
            }
            Remove-Item -LiteralPath $path -Recurse -Force
        }
        if (Test-Path -LiteralPath $workDir) {
            $resolvedWorkDir = (Resolve-Path -LiteralPath $workDir).ProviderPath
            if ((Split-Path -Parent $resolvedWorkDir) -ne $tempRoot -or
                (Split-Path -Leaf $resolvedWorkDir) -notmatch '^muses-msix-test-[0-9a-f]{32}$') {
                throw "Refusing to remove unexpected work directory $resolvedWorkDir."
            }
            Remove-Item -LiteralPath $resolvedWorkDir -Recurse -Force
        }
    }
}
