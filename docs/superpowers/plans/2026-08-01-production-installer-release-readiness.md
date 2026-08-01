# Production Installer and Release Readiness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Produce and verify a production-grade per-user Windows installer for Keyina 0.1.6, while preserving user data, cleaning startup artifacts on uninstall, and keeping release artifacts reproducible.

**Architecture:** Keep the existing Inno Setup and PowerShell release pipeline. Strengthen the installer contract, add an isolated install/upgrade/uninstall lifecycle verifier, integrate it into release verification, and validate the final installer from the actual `F:\Keyina` checkout. No TSF registration is added to the default installation path.

**Tech Stack:** Inno Setup 6/7, PowerShell 5.1+, .NET 10, CMake/MSVC, CTest, GitHub Actions Windows runner.

## Global Constraints

- Work directly on `F:\Keyina`; do not create another worktree.
- Preserve all existing uncommitted core typing changes.
- Installer is per-user and must not require administrator privileges.
- Default install directory remains `{localappdata}\Programs\Keyina`.
- User data under `%LOCALAPPDATA%\Keyina` and Windows Credential Manager must survive upgrade and uninstall.
- Silent install and silent upgrade must not open Settings or leave a resident process running.
- Interactive install may offer to launch Keyina after completion.
- Uninstall must stop Keyina, remove installed files, Start Menu/Desktop shortcuts, the `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Keyina` value, and the legacy Startup shortcut.
- Release output must include portable ZIP, installer EXE, SHA-256 checksums, and release manifest.
- Do not commit or push without explicit user authorization.

---

### Task 1: Lock the installer contract with failing tests

**Files:**
- Modify: `apps/host/Keyina.Host.Tests/WindowsPublishContractTests.cs`
- Test: `apps/host/Keyina.Host.Tests/WindowsPublishContractTests.cs`

**Interfaces:**
- Consumes: `installer/Keyina.iss`, `scripts/windows/build-release.ps1`, `scripts/windows/verify-release.ps1`.
- Produces: contract tests for silent behavior, startup cleanup, preserved user data, lifecycle verification, and release artifact metadata.

- [ ] Add tests that require the installer to use `RunOnceId`, `skipifsilent`, `UninstallRun`, startup registry cleanup, legacy Startup shortcut cleanup, and an explicit preserved-data contract.
- [ ] Add tests that require `build-release.ps1` and `verify-release.ps1` to invoke `test-installer.ps1` when an installer is present.
- [ ] Run the focused tests and confirm they fail only because the new contract is not implemented.

Run:

```powershell
cmd.exe /c dotnet run --project apps/host/Keyina.Host.Tests/Keyina.Host.Tests.csproj -c Release -- "Windows installer" "installer lifecycle"
```

Expected: failing assertions naming the missing installer lifecycle and uninstall-cleanup tokens.

### Task 2: Harden the Inno Setup installer

**Files:**
- Modify: `installer/Keyina.iss`
- Test: `apps/host/Keyina.Host.Tests/WindowsPublishContractTests.cs`

**Interfaces:**
- Produces: deterministic per-user install, silent-safe launch behavior, process shutdown, startup cleanup, and upgrade-safe file replacement.

- [ ] Add `[UninstallRun]` entries that ask `KeyinaInput.exe` to exit before file removal and remove the HKCU Run value without requiring elevation.
- [ ] Keep `%LOCALAPPDATA%\Keyina` outside `[UninstallDelete]`.
- [ ] Add explicit `[Registry]` delete entries for current and legacy startup values.
- [ ] Add `RunOnceId` values so upgrade does not duplicate post-install actions.
- [ ] Mark all post-install launch entries `skipifsilent`; keep Settings launch interactive-only.
- [ ] Add an installer parameter that suppresses resident launch during lifecycle testing without changing normal interactive behavior.
- [ ] Re-run focused contract tests and confirm they pass.

### Task 3: Add isolated installer lifecycle verification

**Files:**
- Create: `scripts/windows/test-installer.ps1`
- Modify: `apps/host/Keyina.Host.Tests/WindowsPublishContractTests.cs`

**Interfaces:**
- Consumes: `-InstallerPath`, `-Version`, optional `-KeepSandbox`.
- Produces: isolated install root, temporary Start Menu/Desktop redirection where supported, install/upgrade/uninstall evidence, and nonzero exit on contract violation.

- [ ] Validate arguments and reject missing, non-EXE, relative, or version-mismatched installer paths.
- [ ] Create a unique sandbox below `%TEMP%\KeyinaInstallerTests`.
- [ ] Run silent install with `/CURRENTUSER`, `/DIR=<sandbox>`, `/NORESTART`, `/SUPPRESSMSGBOXES`, and the lifecycle-test launch suppression parameter.
- [ ] Verify required installed files and `Keyina.Host.exe --version`.
- [ ] Run deterministic managed/native self-tests that do not require foreground acquisition.
- [ ] Write a sentinel user settings file outside the install directory and prove it survives install, upgrade, and uninstall.
- [ ] Run the same installer again to verify idempotent upgrade.
- [ ] Run the generated uninstaller silently.
- [ ] Verify the install directory, resident process, startup value, and installer-created shortcuts are gone while the sentinel remains.
- [ ] Clean the sandbox in `finally`, unless `-KeepSandbox` is supplied.

### Task 4: Integrate lifecycle verification into release build and verification

**Files:**
- Modify: `scripts/windows/build-release.ps1`
- Modify: `scripts/windows/verify-release.ps1`
- Modify: `.github/workflows/release.yml`
- Test: `apps/host/Keyina.Host.Tests/WindowsPublishContractTests.cs`

**Interfaces:**
- Produces: release candidate build that compiles and lifecycle-tests the installer; independent verification repeats checksum/version/self-test/lifecycle checks.

- [ ] Invoke `test-installer.ps1` after installer compilation when verification is enabled.
- [ ] In `verify-release.ps1`, locate the installer from the manifest and run the lifecycle verifier.
- [ ] Add manifest fields for installer type, install scope, lifecycle verification, and preserved user-data directory.
- [ ] Ensure CI installs Inno Setup before release verification and uploads only verified artifacts.
- [ ] Re-run focused contract tests.

### Task 5: Improve release artifact verification

**Files:**
- Modify: `scripts/windows/verify-release.ps1`
- Modify: `scripts/windows/build-release.ps1`
- Test: `apps/host/Keyina.Host.Tests/WindowsPublishContractTests.cs`

**Interfaces:**
- Produces: one-to-one checksum/manifest validation and rejection of duplicate or unexpected artifact names.

- [ ] Require every manifest artifact to exist exactly once and match bytes/SHA-256.
- [ ] Require every checksum entry to map to exactly one manifest artifact.
- [ ] Validate manifest schema, version, runtime identifier, install scope, and signed-state consistency.
- [ ] Verify the installer file version and product metadata where Windows exposes them.
- [ ] Keep signature verification mandatory only when `signed=true`.

### Task 6: Build and run the real installer lifecycle

**Files:**
- Generated: `artifacts/release/0.1.6/installer/Keyina-Setup-0.1.6-x64.exe`
- Generated: `artifacts/release/0.1.6/Keyina-0.1.6-win-x64.zip`
- Generated: `artifacts/release/0.1.6/SHA256SUMS.txt`
- Generated: `artifacts/release/0.1.6/release-manifest.json`

- [ ] Confirm Inno Setup compiler availability.
- [ ] Stop only the currently published Keyina resident before replacing release artifacts.
- [ ] Run the full managed and native deterministic gates.
- [ ] Run `build-release.ps1 -Version 0.1.6`.
- [ ] Run `verify-release.ps1 -Version 0.1.6` independently.
- [ ] Inspect installer file metadata, artifact sizes, hashes, and manifest.
- [ ] Start exactly one resident from the final published/release bundle and verify path, process count, working set, private bytes, handles, and responsiveness.

### Task 7: Final repository and workspace hygiene

**Files:**
- No source changes expected.

- [ ] Restore only generated RID lockfile noise.
- [ ] Run `git diff --check`.
- [ ] Confirm `git worktree list` contains only `F:/Keyina` on `main`.
- [ ] Confirm `C:\Users\KanzuWakazaki\.devspace\profiles\Keyina\session-1\worktrees` is empty.
- [ ] Confirm `git stash list` is empty.
- [ ] Report all remaining intentional uncommitted files and generated release artifacts.
