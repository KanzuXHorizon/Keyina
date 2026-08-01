# Keyina Production Installer and Release Readiness Design

## Status

Approved design for the Windows release-readiness audit, production installer hardening, install/upgrade/uninstall lifecycle verification, and final worktree consolidation.

## Goal

Produce a verified Windows x64 release of Keyina that installs per-user without elevation, upgrades safely, preserves user data, removes only program-owned integration points during uninstall, passes correctness/performance/resource gates, and leaves all valid project changes consolidated on `main` with no stale worktrees.

## Scope

This slice covers:

- release-readiness auditing of the current repository and published bundle;
- hardening the existing Inno Setup installer instead of introducing MSI, MSIX, or a second packaging pipeline;
- deterministic installer contract tests;
- a real install → verify → upgrade → verify → uninstall → verify lifecycle harness;
- release artifact verification, checksums, and manifest consistency;
- resident startup, singleton, shortcut, registry-startup, and settings-companion behavior;
- preservation of configuration and credentials across upgrades and ordinary uninstall;
- resource, latency, and background-behavior verification from the installed bundle;
- consolidation on `main` and cleanup of all stale or session-owned worktrees after final verification.

This slice does not add TSF as the primary input backend, implement an automatic updater service, add machine-wide installation, or introduce background polling.

## Existing Architecture to Preserve

Keyina already has:

- `installer/Keyina.iss` for Inno Setup packaging;
- `scripts/windows/build-release.ps1` for native/managed build, self-tests, publishing, installer compilation, checksums, and manifest generation;
- `scripts/windows/verify-release.ps1` for artifact and runtime verification;
- `.github/workflows/release.yml` for Windows release candidates;
- a per-user native resident, managed settings companion, startup registration under HKCU, and user configuration under `%LOCALAPPDATA%\Keyina`;
- a self-contained `win-x64` published bundle.

The implementation must strengthen these seams instead of creating parallel release scripts or package formats.

## Installer Product Contract

### Installation Scope

- Install per-user to `%LOCALAPPDATA%\Programs\Keyina`.
- Require no administrator privileges.
- Support x64-compatible Windows 10 version 2004 (`10.0.19041`) and newer.
- Keep a stable `AppId` so upgrades replace the existing installation.
- Install the self-contained managed host, native resident, engine DLL, assets, and documentation from the verified publish directory only.
- Reject installer compilation when required definitions or source files are missing.

### Interactive Installation

The wizard must:

- use the modern Inno Setup style;
- display Keyina product metadata, icon, license, publisher, support, and update URLs;
- offer an unchecked optional desktop shortcut;
- close running Keyina processes before replacing files;
- preserve the previous installation directory and selected tasks;
- optionally launch the resident and open Settings after a successful interactive install.

### Silent Installation

Silent and very-silent installation must:

- install or upgrade without displaying Settings;
- not leave an unexpected foreground window;
- avoid forcing a resident launch from deployment automation;
- return a meaningful non-zero exit code on failure.

The installed application may still start normally at the next user logon when startup is enabled in Keyina settings.

### Upgrade Behavior

An upgrade must:

- stop the currently installed resident before file replacement;
- keep `%LOCALAPPDATA%\Keyina\settings.json`, runtime profiles, and credentials untouched;
- keep the existing HKCU startup preference unless the user changes it in Keyina;
- preserve selected installer tasks such as the desktop shortcut;
- remove obsolete program files and the legacy Startup-folder shortcut;
- install the new version atomically enough that a failed upgrade does not silently claim success.

### Uninstall Behavior

Ordinary uninstall must:

- stop the installed resident and settings companion;
- remove program files and installer-created Start Menu/Desktop shortcuts;
- remove the Keyina HKCU Run entry owned by the product;
- remove the legacy Startup-folder shortcut;
- remove empty product directories under the installation root;
- leave `%LOCALAPPDATA%\Keyina` intact by default so user settings, profiles, and credentials survive reinstall;
- not remove unrelated registry values, files, or credentials.

A data-erasure option is outside this installer slice and must not be inferred from ordinary uninstall.

## Release Pipeline Contract

### Build

`scripts/windows/build-release.ps1` remains the only release build entry point. It must:

1. resolve one semantic version for native, managed, installer, portable archive, checksums, and manifest;
2. build native Release and managed Release with warnings treated as errors;
3. run deterministic native, managed, corpus, benchmark-comparison, and resource tests;
4. publish the self-contained `win-x64` bundle;
5. verify required files and reported version;
6. optionally Authenticode-sign project binaries and the installer;
7. compile the installer from the verified publish directory;
8. run installer lifecycle verification unless explicitly skipped for a documented local-development reason;
9. create the portable ZIP, SHA-256 checksums, and release manifest;
10. fail the release on any mismatch, missing artifact, lifecycle failure, or required signature failure.

### Verification

`scripts/windows/verify-release.ps1` must verify:

- manifest schema, product, version, runtime identifier, artifact list, sizes, and SHA-256 values;
- exact presence of the installer and portable archive declared by the manifest;
- published host and resident version/self-tests;
- required signatures when `signed=true`;
- installer lifecycle verification against an isolated test installation root;
- absence of leftover program files, owned startup entries, shortcuts, and test processes after uninstall;
- preservation of an isolated synthetic settings sentinel across install, upgrade, and ordinary uninstall.

## Installer Lifecycle Harness

Create a focused PowerShell harness under `scripts/windows/` that can be invoked by both build and verify scripts.

### Isolation

The harness must:

- use a unique temporary install directory, Start Menu group, desktop shortcut name, AppId suffix, startup value name, and configuration directory override for the test run;
- never read or modify the real `%LOCALAPPDATA%\Keyina` settings or Credential Manager entries;
- never reuse the production singleton, startup registry value, or shortcuts;
- terminate only processes whose executable path is inside the temporary installation directory;
- restore or delete every temporary registry/file artifact in `finally` cleanup.

If Inno compile-time overrides cannot safely isolate every owned integration point, add explicit installer definitions for lifecycle-test mode instead of testing against the real production identity.

### Lifecycle Steps

The harness must perform:

1. validate the installer file exists and has the expected version metadata;
2. create a synthetic configuration sentinel in the isolated data directory;
3. run silent install into the isolated directory;
4. verify required installed files and uninstall registration;
5. run installed managed and native deterministic self-tests that do not require stealing desktop foreground;
6. verify no unexpected resident remains after silent installation;
7. run a second silent install as an upgrade;
8. confirm version, files, task state, and configuration sentinel remain correct;
9. run silent uninstall;
10. verify installed files, uninstall registration, test shortcuts, test startup entry, and test processes are gone;
11. confirm the synthetic configuration sentinel remains after ordinary uninstall;
12. remove the isolated data directory during harness cleanup, not through the product uninstaller.

### Desktop-Interactive Tests

Tests requiring `SetForegroundWindow`, real keyboard injection, or a visible desktop are not valid lifecycle-harness gates. They remain in the native interactive test lane and must report environmental blockers explicitly instead of being converted into false installer failures.

## Correctness and Performance Audit

The release audit must examine evidence, not make broad rewrites.

Required checks:

- all tracked source has no release-impacting TODO/FIXME placeholders;
- native and managed builds have zero warnings and zero errors;
- the expanded Vietnamese corpus and long-stream/backspace tests pass;
- installer and release scripts have focused contract tests;
- native hot paths retain zero-allocation budgets;
- installed resident resource probes stay within existing private-memory, thread, handle, and idle-CPU budgets;
- no new background thread, polling loop, timer, telemetry, typed-content logging, or unbounded queue is introduced;
- Settings, overlays, snippets, translation, speech, and startup tests remain green;
- generated lockfile noise from RID-specific publish is removed before final diff review.

Defects discovered during the audit must be reduced to a failing test and fixed at the earliest incorrect layer. Unrelated refactors are excluded.

## Security and Privacy

- Installer and lifecycle logs must not contain typed text, clipboard contents, credentials, API keys, or private configuration values.
- Synthetic sentinel values must be non-secret and clearly test-only.
- Signing material remains outside the repository and must be removed from CI temporary storage in an `always()` cleanup step.
- The installer must not request elevation for the normal per-user path.
- Uninstall cleanup must use exact product-owned paths and registry value names.
- Configuration and credentials must not be exported into release artifacts.

## Tests

### Managed Contract Tests

Extend `WindowsPublishContractTests` or split a focused installer contract test file when clearer. Tests must assert:

- silent install does not auto-launch resident or Settings;
- interactive post-install launch remains available;
- uninstall cleanup removes the owned HKCU Run entry and legacy shortcut;
- test-mode compile definitions exist for isolated install/data/startup/shortcut identity;
- build and verify scripts invoke the lifecycle harness;
- release manifest includes installer lifecycle verification status or equivalent evidence;
- installer source references only the verified publish source directory.

### PowerShell Tests

Add a deterministic script test for argument validation, path isolation, manifest/installer mismatch handling, and cleanup behavior. Use existing repository PowerShell test conventions rather than adding a new framework.

### Full Gates

Before claiming completion:

- native Debug and Release tests;
- managed Release build and full test runner;
- Vietnamese vector checker;
- benchmark comparison and relevant application benchmarks;
- published host/resident deterministic self-tests;
- installer compilation;
- isolated install/upgrade/uninstall lifecycle test;
- release verification script;
- resource probes from installed/published binaries;
- `git diff --check`;
- final resident count/path check.

## Artifacts

The release output remains:

```text
artifacts/release/<version>/
├── win-x64/
├── installer/
│   └── Keyina-Setup-<version>-x64.exe
├── Keyina-<version>-win-x64.zip
├── SHA256SUMS.txt
└── release-manifest.json
```

The final response must provide the exact local installer path, SHA-256, file size, verification status, and whether it is signed. An unsigned local installer must be described honestly as unsigned.

## Main Consolidation and Worktree Cleanup

This is a mandatory release gate.

### Consolidation

- All valid changes from the current slice must exist in `F:\Keyina` on branch `main`.
- Existing valid work already integrated into `main` must remain intact.
- No commit, push, force operation, reset, or branch deletion is authorized unless the user explicitly requests it.
- Final verification must run on the actual `F:\Keyina` checkout, not only an isolated worktree.

### Cleanup Procedure

After successful main verification:

1. run `git worktree list --porcelain`;
2. classify every entry as main checkout, existing valid worktree, session-owned worktree, or stale registration;
3. compare status/content for every existing path before removal;
4. do not remove a worktree containing changes absent from `main`;
5. remove session-owned worktrees only after their valid content is present on `main`;
6. prune registrations whose paths no longer exist;
7. run `git worktree prune`;
8. run `git worktree list --porcelain` again and require only the main checkout unless an externally owned worktree with unique unmerged changes is explicitly reported and retained;
9. confirm no orphaned managed workspace directory remains for this project;
10. report the final worktree list and cleanup result.

The currently observed five detached entries are marked `prunable` because their gitdir paths no longer exist. They may be pruned after final verification. They must not be treated as containing recoverable working-tree files without checking the path state first.

## Acceptance Criteria

The slice is complete only when all of the following are true:

- a production installer exists at the expected release artifact path;
- the installer performs isolated silent install, upgrade, and uninstall successfully;
- silent install does not launch foreground UI or leave a resident process;
- interactive installation still offers launch/open-settings actions;
- upgrade preserves synthetic settings and user-facing installer choices;
- ordinary uninstall removes program-owned binaries, shortcuts, startup registration, and processes while preserving user data;
- release archive, installer, checksums, and manifest agree exactly;
- required build, correctness, benchmark, and resource gates pass;
- no generated lockfile noise or accidental artifacts remain in the source diff;
- the verified release changes are present on `main`;
- all stale and session-owned Keyina worktrees are cleaned, with final evidence showing only the main checkout or an explicitly retained external worktree with unique unmerged content;
- the installer path, size, SHA-256, signing state, tests, and any environmental limitations are reported accurately.
