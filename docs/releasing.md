# Building and releasing Keyina for Windows

This document describes the reproducible local release path for the x64 Windows host, portable archive, installer, checksums, and optional Authenticode signing.

## Release outputs

A release build creates the following directory:

```text
artifacts/release/<version>/
├─ win-x64/                         self-contained published application
├─ installer/
│  └─ Keyina-Setup-<version>-x64.exe
├─ Keyina-<version>-win-x64.zip     portable application
├─ SHA256SUMS.txt
└─ release-manifest.json
```

The installer uses a stable Inno Setup `AppId`. Installing a newer version upgrades the existing per-user installation in place and preserves configuration and credentials because they live outside the installation directory.

- Application files: `%LOCALAPPDATA%\Programs\Keyina`
- Configuration: `%LOCALAPPDATA%\Keyina\settings.json`
- Credentials: Windows Credential Manager
- Startup entry: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Keyina`

Uninstalling Keyina removes installed binaries and shortcuts. User configuration and cloud credentials are intentionally preserved so an upgrade or reinstall does not destroy preferences. They can be removed manually from Keyina settings or Windows Credential Manager when a full reset is required.

## Prerequisites

- Windows 10 2004 or newer, x64.
- Visual Studio 2022 C++ build tools.
- CMake and CTest on `PATH`.
- .NET SDK selected by `global.json`.
- Python 3.
- Inno Setup 6 or 7. Install it per-user with:

```powershell
winget install --id JRSoftware.InnoSetup -e -s winget --silent `
  --accept-package-agreements --accept-source-agreements
```

## Build an unsigned release

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File scripts/windows/build-release.ps1
```

The default version comes from `KeyinaVersion` in `Directory.Build.props`. To build another version without editing the repository:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File scripts/windows/build-release.ps1 `
  -Version 0.1.1
```

The script runs native Debug-independent Release tests, managed Release tests, host self-tests, resource checks, benchmarks, self-contained publish, portable ZIP creation, installer compilation, SHA-256 generation, and manifest creation.

Use `-SkipVerification` only while debugging the packaging scripts. It must not be used for an actual release.

## Verify a built release

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File scripts/windows/verify-release.ps1
```

The verification script checks every published checksum, confirms the version reported by the packaged host, reruns packaged self-tests, and verifies Authenticode signatures when the manifest declares a signed release.

## Authenticode signing

Public releases should use a certificate that chains to a trusted certificate authority, Azure Artifact Signing, or a managed open-source signing service such as SignPath Foundation. A self-signed certificate is suitable only for development or managed internal deployment.

Keyina never stores a signing certificate or password in the repository. Configure one of these methods in the process environment.

### Certificate in Windows certificate store

```powershell
$env:KEYINA_SIGN_CERT_THUMBPRINT = '<certificate thumbprint>'
$env:KEYINA_SIGN_CERT_STORE = 'CurrentUser' # or LocalMachine
$env:KEYINA_SIGN_TIMESTAMP_URL = 'http://timestamp.digicert.com'

powershell -NoProfile -ExecutionPolicy Bypass `
  -File scripts/windows/build-release.ps1 `
  -Sign -RequireSignature
```

### PFX file

```powershell
$env:KEYINA_SIGN_PFX_PATH = 'C:\secure\keyina-code-signing.pfx'
$env:KEYINA_SIGN_PFX_PASSWORD = '<password supplied by secret storage>'
$env:KEYINA_SIGN_TIMESTAMP_URL = 'http://timestamp.digicert.com'

powershell -NoProfile -ExecutionPolicy Bypass `
  -File scripts/windows/build-release.ps1 `
  -Sign -RequireSignature
```

The signing helper signs Keyina-owned PE files with SHA-256 and an RFC 3161 timestamp. Inno Setup invokes the same helper for Setup and the uninstaller. Every signature is verified before the release finishes.

Do not place a PFX file, private key, password, or signing service token inside the repository or artifact directory.

## Install and upgrade

Interactive installation:

```powershell
.\artifacts\release\0.1.6\installer\Keyina-Setup-0.1.6-x64.exe
```

Silent current-user installation:

```powershell
.\Keyina-Setup-0.1.6-x64.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
```

To update Keyina, build a higher version and run its installer. The stable `AppId` makes Inno Setup reuse the existing install directory and uninstall registration. Close Keyina when Setup asks so resident hooks and binaries can be replaced safely.

## Run without installing

Extract the portable ZIP and run:

```powershell
.\Keyina.Host.exe --show-settings
```

Useful release checks:

```powershell
.\Keyina.Host.exe --version
.\Keyina.Host.exe --self-test
.\Keyina.Host.exe --speech-self-test
.\Keyina.Host.exe --hotkey-self-test
.\Keyina.Host.exe --resource-self-test
```

## Version update checklist

1. Update `KeyinaVersion` in `Directory.Build.props` for a normal source release.
2. Update `CHANGELOG.md`.
3. Run `scripts/windows/build-release.ps1` without `-SkipVerification`.
4. Run `scripts/windows/verify-release.ps1` against the generated directory.
5. Inspect `release-manifest.json` and `SHA256SUMS.txt`.
6. Test install, launch, upgrade over the previous version, and uninstall on a clean Windows VM.
7. Publish the installer, portable ZIP, checksum file, and manifest together.
8. Keep the same signing identity across releases whenever possible.

## GitHub release automation

The **Windows release candidate** workflow (`.github/workflows/release.yml`) is manual and calls the same local scripts. By default it builds an unsigned candidate and uploads it for 14 days without publishing a GitHub Release.

For a signed candidate, create repository secrets `KEYINA_SIGN_PFX_BASE64` and `KEYINA_SIGN_PFX_PASSWORD`, then run the workflow with **Require Authenticode signing** enabled. The workflow decodes the PFX only into the runner's temporary directory, invokes `-Sign -RequireSignature`, verifies the release, uploads the artifact, and removes the temporary file in an `always()` cleanup step.

A public release should never be created from the unsigned workflow output. Prefer a managed signing service over a long-lived exportable PFX when one is available.
