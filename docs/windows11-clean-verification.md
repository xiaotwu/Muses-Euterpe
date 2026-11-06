# Clean Windows 11 package verification

Use Windows Sandbox or a fresh Windows 11 x64 desktop guest with networking and an audio output. Start each run from an empty guest. The script installs directly from the signed MSIX and runs the app in its registered package context.

## Prepare the inputs

On the build machine, use a checkout containing the pre-port commit and two valid signed MSIX files. Use the actual package built from pre-port commit **6fabd52** as the historical baseline.

    .\scripts\prepare-windows11-clean-test.ps1 -Package $candidate -PreviousPackage $prePort -OutputDirectory .\artifacts\windows11-clean-test -IncludeTestCertificates -PrepareSandbox

The staging script verifies the packages, prepares a synthetic SQLite fixture from the pinned pre-port schema, and includes a hash-verified embeddable Python 3.13 runtime for the test harness. It exports only the public certificates of self-signed input packages. It leaves the build machine's certificate store and PATH alone. [Python 3.13.17 downloads](https://www.python.org/downloads/release/python-31317/)

## Run in the guest

With Windows Sandbox enabled, open the generated **windows11-clean-test.wsb**. The input folder is read-only. The sibling **windows11-clean-test-results** folder receives the transcript. [Enable Windows Sandbox](https://learn.microsoft.com/windows/security/application-security/application-isolation/windows-sandbox/windows-sandbox-install)

For a separate VM, omit the staging script's **-PrepareSandbox** switch, transfer the staged folder, and run this from Windows PowerShell inside the guest. For a test certificate, open that session as administrator in the guest.

    .\test-windows11-clean.ps1 -DisposableGuest -TrustTestCertificates -MediaSmokeVideoId dQw4w9WgXcQ

The runner checks the transferred file hashes before use. It installs the candidate, verifies a real first playback and restart resume with only system paths, and uninstalls it. Then it installs the historical package against the staged Roaming AppData fixture, upgrades to the candidate, verifies the preserved data, and uninstalls again. Certificates added by the runner are removed at exit.

A passing transcript includes the Windows build and both package hashes, package registration and launch, playback checkpoints, and historical data verification. Close the Sandbox when the transcript is complete. Keep the transcript for review and remove the staged input, result folder and configuration when they are no longer needed.

## Production family checks

Use two publicly trusted packages with the same production publisher and package family. On a separate clean guest, run **test-msix.ps1** with **-PreviousPackage**, **-ExpectedPublisher** and **-ExpectedPackageFamilyName** together. Add **-MediaSmokeVideoId** to exercise live playback. Use the historical Roaming test separately for the original development package family.
