# Keyina Full-Project Bug Hunt Results

Date: 2026-08-05
Base revision: `f607022`
Execution mode: isolated managed Git worktree
Release version verified: `0.1.9`

## Executive Summary

The audit covered native Telex behavior, resident hook/runtime lifecycle, managed configuration and services, Settings UI/UX, accessibility, security/privacy, resource gates, build/test isolation, benchmarks, packaging, and release verification.

Confirmed defects were fixed in five areas:

1. The persisted `Theme` configuration had no complete Settings/runtime path.
2. There was no automated guard against future persisted-field-to-UI omissions.
3. Settings navigation exposed selection visually but not semantically to accessibility clients.
4. Native resource gates produced misleading failures when an installed resident already owned the desktop hook/mutex.
5. The native test process intermittently terminated in `ole32.dll` with `0xC000041D`, consistent with unstable OLE apartment lifetime across clipboard tests.
6. Release scripts treated the new, explicit resource-test blocked state as an unconditional release failure.

The user's two existing `russt` regression tests were preserved and already passed without an engine change, confirming that no word-specific Telex patch was required.

## Confirmed Fixes

### Persisted theme is now functional

- Added deterministic System/Light/Dark theme resolution.
- Windows High Contrast always overrides the stored preference.
- Added `SettingsSnapshot.Theme` and `SettingsActions.SetTheme`.
- Loaded, imported, applied, and atomically persisted the selected theme in `KeyinaApplicationContext`.
- Added an accessible, keyboard-reachable Settings selector with `Theo Windows`, `Sáng`, and `Tối` choices.
- Avoided a WinForms handle-order bug by using static selector items rather than a data source that materialized only after handle creation.
- Added focused resolution, persistence, UI binding, and accessibility tests.

### Configuration/UI parity is guarded

Added `SettingsContractParityTests.cs`, which inventories every public `KeyinaConfiguration` property and requires every user-facing field to have:

- a `SettingsSnapshot` representation;
- a `SettingsActions` mutation path;
- a concrete Settings UI owner.

Only persistence metadata (`SchemaVersion`) and first-run lifecycle state (`FirstRunCompleted`) are intentionally excluded. The guard currently covers 17 user-facing configuration fields.

### Settings navigation accessibility is semantic

- Navigation items now expose `AccessibleRole.PageTab`.
- Selected state is represented in accessible text rather than by color/indicator alone.
- Keyboard arrows, Home/End, selection, and focus-transfer behavior remain intact.
- A synchronous accessibility notification was deliberately removed after regression testing showed it could re-enter WinForms focus handling and prevent page focus transfer.

### Native resource tests distinguish blocked, contaminated, and failed states

Added a pure resource-test classifier and focused native tests for:

- pass;
- retry after physical-input contamination;
- blocked by an existing installed resident;
- clean resource-budget failure;
- runtime startup failure.

The native resident now checks the production single-instance mutex before launching resource children. When another resident is active it emits exactly:

```json
{"error":"resource_self_test_blocked_by_existing_resident"}
```

and returns exit code `77`.

CTest marks exit `77` as `Skipped` for the two resource gates. Desktop-sensitive native tests share a CTest resource lock to prevent concurrent ownership of the keyboard/desktop test surface.

### Native OLE test lifetime is stable

The full native suite intermittently exited with Windows status `0xC000041D`; Windows Error Reporting identified `ole32.dll` in one occurrence. Direct unit runs were green, while the failure appeared after integration processes had run.

The native test executable now:

- initializes one OLE apartment for the full test-process lifetime;
- runs all clipboard/OLE and native tests inside that lifetime;
- uninitializes OLE only after all registered tests and COM objects have been destroyed;
- prints and flushes a `[RUN]` marker before each native case for exact future crash localization.

After the change, the complete native CTest suite passed eight consecutive repetitions without recurrence.

### Release scripts accept only a verified blocked state

Both `build-release.ps1` and `verify-release.ps1` now allow exit codes `0` and `77` only for the two resource self-tests. Exit `77` is accepted only when stdout exactly matches the expected blocked JSON marker. Any other output or exit code still fails the release.

PowerShell syntax parsing and the release-script contract test cover this behavior.

## Verification Evidence

### Managed Debug

Commands:

```powershell
dotnet build Keyina.slnx -c Debug
dotnet run --project apps/host/Keyina.Host.Tests/Keyina.Host.Tests.csproj -c Debug --no-build
dotnet run --project apps/host/Keyina.Host.Tests/Keyina.Host.Tests.csproj -c Debug --no-build -- --interactive
```

Results:

- build: 0 warnings, 0 errors;
- non-interactive tests: 339/339 passed;
- interactive/UI/desktop tests: 382/382 passed.

The interactive lane includes Settings responsiveness, screenshots, navigation/focus, accessibility contracts, native hook integration, clipboard behavior, overlays, and the new configuration/UI parity guard.

### Managed Release

Commands:

```powershell
dotnet build Keyina.slnx -c Release
dotnet run --project apps/host/Keyina.Host.Tests/Keyina.Host.Tests.csproj -c Release --no-build
```

Results:

- build: 0 warnings, 0 errors;
- tests: 339/339 passed.

### Native Debug

Commands:

```powershell
cmake --preset windows-msvc-debug
cmake --build --preset windows-msvc-debug
ctest --preset windows-msvc-debug --output-on-failure
ctest --preset windows-msvc-debug --repeat until-fail:8 --output-on-failure
```

Results:

- all deterministic native tests passed;
- complete suite passed eight consecutive repetitions;
- `keyina.windows.input_resource` and `keyina.windows.input_tray_resource` were explicitly skipped because the installed resident was active;
- no native OLE crash recurred.

### Native Release

Commands:

```powershell
cmake --preset windows-msvc-release
cmake --build --preset windows-msvc-release
ctest --preset windows-msvc-release --output-on-failure
```

Results:

- build passed;
- all deterministic tests passed;
- the two resource gates were explicitly skipped for the same installed-resident precondition.

### Product self-tests

Commands:

```powershell
dotnet run --project apps/host/Keyina.Host/Keyina.Host.csproj -c Release --no-build -- --self-test
dotnet run --project apps/host/Keyina.Host/Keyina.Host.csproj -c Release --no-build -- --speech-self-test
dotnet run --project apps/host/Keyina.Host/Keyina.Host.csproj -c Release --no-build -- --hotkey-self-test
dotnet run --project apps/host/Keyina.Host/Keyina.Host.csproj -c Release --no-build -- --resource-self-test
```

Results:

- host self-test: `Keyina 0.1.9`;
- speech self-test: passed;
- hotkey self-test: passed;
- host resource self-test: passed;
- private memory: 10,211,328 bytes;
- private-memory delta: 958,464 bytes;
- measurement contamination: false;
- typing hook running: true.

### Vectors and performance

Commands:

```powershell
python tools/check_vectors.py
python tools/test_compare_benchmark.py
build/windows-msvc-release/benchmarks/Release/keyina_bench.exe
dotnet run --project apps/host/Keyina.Host.Benchmarks/Keyina.Host.Benchmarks.csproj -c Release --no-build
```

Results:

- 283 golden Telex vectors validated;
- benchmark comparator tests: 7/7 passed;
- every native benchmark case passed its budget;
- native hot-path allocation count remained 0 allocations per operation;
- managed snippet benchmark completed across 10, 100, 1,000, and 10,000 definition scales.

Representative native Release measurements:

- ASCII pass-through median: 100 ns;
- tone update median: 200 ns;
- complete `tieengs` word median: 1,600 ns;
- delayed `truowcs` modifier median: 2,500 ns;
- invalid-boundary restoration median: 21,600 ns.

### Security and privacy

Commands/checks:

```powershell
dotnet list Keyina.slnx package --vulnerable --include-transitive
```

plus repository scans for hardcoded credentials, certificate-validation bypasses, unsafe public HTTP defaults, empty exception handlers, debug/content logging, and unfinished production placeholders.

Results:

- no vulnerable direct or transitive NuGet packages reported;
- no hardcoded production secret found;
- no certificate-validation bypass found;
- public plain-HTTP endpoints remain rejected; local HTTP requires explicit local-endpoint opt-in;
- no empty exception handler found;
- privacy tests for source text, transcript text, credentials, clipboard formats, and diagnostics passed;
- the remaining `NotSupportedException` is an intentional async event accessor contract in the clipboard selection adapter.

### Portable release and independent verification

Commands:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/windows/build-release.ps1 -SkipBuildTests -SkipInstaller
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/windows/verify-release.ps1 -ArtifactDirectory artifacts/release/0.1.9
```

Results:

- portable ZIP created;
- SHA-256 checksum file created;
- release manifest created;
- published host/native self-tests passed;
- profile reload passed;
- both resident resource probes reported the verified blocked state;
- independent release verifier passed for version 0.1.9;
- artifacts were unsigned, as expected because signing was not requested.

## Blocked or Manual-Only Evidence

### Installer lifecycle

The full installer lifecycle script correctly refused to run while the installed `KeyinaInput.exe --background` process was active. The process was not terminated because doing so would alter the user's running environment without authorization.

The installer build itself reached successful Inno Setup compilation before lifecycle verification stopped at this precondition. A clean install/upgrade/uninstall lifecycle run still requires explicit authorization to stop the installed resident first.

### Native resident resource budgets without an existing resident

The resident resource gates now report the environmental precondition accurately, but their clean budget measurements were not rerun because the installed production resident remained active. The managed host resource probe did run cleanly and passed.

### Linux ASan/UBSan

WSL Ubuntu was available, but the distribution did not contain CMake or Clang. The sanitizer lane was therefore blocked by missing toolchain rather than a project failure.

## Files Added

- `apps/host/Keyina.Host.Tests/FluentThemeTests.cs`
- `apps/host/Keyina.Host.Tests/SettingsContractParityTests.cs`
- `platform/windows/input/include/keyina/windows/resource_self_test_policy.h`
- `platform/windows/input/resource_self_test_policy.cpp`
- `tests/windows/resource_self_test_policy_test.cpp`

## Existing User Work Preserved

The following pre-existing regression tests were preserved in intent and passed:

- `latin_word_russt_preserves_repeated_s`;
- `resident_input_controller_preserves_double_s_in_russt`.

No word-specific engine exception was added.

## Repository State

- Work was performed in an isolated worktree based on `f607022`.
- No commit, merge, push, publish, signing, credential mutation, installed-process termination, or production installation was performed.
- `git diff --check` passed.
- Generated release/build artifacts were not included in the source diff.
