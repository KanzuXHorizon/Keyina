# Keyina Full-Project Bug Hunt Design

Date: 2026-08-05
Status: Approved by the user's explicit request to audit and fix the complete project

## 1. Goal

Audit Keyina end to end and fix confirmed defects, omissions, contract mismatches, regressions, and production-readiness gaps without broad speculative rewrites. The final state must be internally consistent across native engine, Windows resident runtime, managed host, configuration, UI/UX, tests, packaging, documentation, security, and performance evidence.

## 2. Current Evidence

The initial read-only baseline established:

- The repository is on `main` at `f607022` with two user-authored, uncommitted regression tests for preserving repeated `s` in `russt`, plus an untracked benchmark result. These changes must be preserved.
- Native Debug builds successfully.
- The native unit executable passes all 234 tests when run directly, including both uncommitted `russt` regressions.
- Managed Debug builds with zero warnings and all 336 host tests pass.
- A full native CTest run exposed two environment-sensitive failures:
  - `keyina.windows.input_tray_resource` exceeded the 10 MiB resource budget while desktop input contaminated the measurement.
  - `keyina.unit` once exited with `0xC000041D`, but then passed five consecutive isolated CTest repetitions, indicating a transient suite-interaction or desktop-environment problem rather than a deterministic engine failure.
- An installed production copy of `KeyinaInput.exe --background` was running during the tests from the current user's local Programs directory. This creates a second keyboard hook and contaminates resource and desktop-sensitive verification.
- `KeyinaConfiguration.Theme` is persisted and covered by configuration/import tests but has no production UI/runtime usage. Settings always follow the Windows system theme, so the persisted `System/Light/Dark` contract is orphaned. This is a confirmed config-to-UI parity defect analogous to a stored field that has no usable interface.

## 3. Selected Approach

Use an evidence-first, vertical-slice audit.

For each subsystem:

1. Inventory public and persisted contracts.
2. Trace every field and state through load, validation, runtime application, UI presentation, save, export/import, and tests.
3. Reproduce an observable failure or prove a missing path.
4. Add the smallest focused regression check.
5. Apply the smallest root-cause fix.
6. Verify adjacent paths before moving on.

This approach is preferred over a test-only sweep because existing tests can pass while a field or UI path is absent. It is preferred over a rewrite because Keyina already has strong architecture and broad test coverage; large refactors would raise hook, latency, accessibility, and release risk without evidence.

## 4. Audit Matrix

### 4.1 Native engine and typing correctness

- Telex transformations, repeated-key escape, mixed Vietnamese/Latin text, Unicode boundaries, invalid-word restoration, Backspace behavior, quick Telex, standalone `w`, tone placement, and context guard.
- Corpus and property/invariant coverage for structurally similar Latin words, not word-specific exceptions.
- Cross-check native engine behavior with resident-controller and managed fallback paths.

### 4.2 Resident Windows runtime

- Hook lifecycle, duplicate resident interactions, injected-event markers, key-up suppression, pointer observation, focus transitions, fail-open behavior, profile reload, tray lifecycle, and process shutdown.
- Resource probes must detect and report desktop contamination, existing installed residents, duplicate hooks, and unstable baselines instead of producing misleading failures.
- Low-level hook callback remains bounded and performs no file, network, UI, or process-launch work.

### 4.3 Managed host and optional services

- Speech, translation, clipboard replacement, credentials, snippets, hotkeys, startup registration, application exclusions, IPC, cancellation, focus locking, and provider failure mapping.
- Verify network and credential paths remain opt-in and fail secure.

### 4.4 Configuration and data-contract parity

Create an explicit parity inventory for:

- `KeyinaConfiguration` and nested preference records.
- `SettingsSnapshot` and `SettingsActions`.
- Runtime profile codecs and native decoders.
- Portable export/import.
- Settings controls, labels, validation, and save actions.

Every persisted user-facing field must be either:

- exposed and functional in the UI,
- intentionally internal with documented ownership, or
- removed through a versioned migration.

The current `Theme` orphan must be resolved by wiring System/Light/Dark selection through snapshot, actions, runtime persistence, and all relevant windows while preserving forced high-contrast behavior.

### 4.5 UI/UX and accessibility

Audit all WinForms surfaces:

- Settings, first run, translation preview, dictation overlay, snippet overlay, snippet editor, hotkey capture, tray state, empty/error/loading/success states.
- Narrow, compact, expanded, high DPI, dynamic DPI, high contrast, keyboard-only navigation, focus order, accessible names/descriptions, disabled controls, overflow, long localized strings, and reduced-motion behavior.
- UI Automation must expose meaningful names, roles, values, states, and focus transitions for custom Fluent controls.
- Visual changes must preserve the existing Fluent design language and task-first information hierarchy.

### 4.6 Security and privacy

- Hardcoded secrets, permissive endpoint defaults, insecure local HTTP opt-in, credential persistence, clipboard privacy formats, transcript/selected-text logging, temporary files, IPC access, executable snippet validation, path handling, and update/release trust boundaries.
- Missing credentials and optional services must fail closed without disabling ordinary Vietnamese typing.
- No production path may log or serialize secrets, speech transcripts, source selection text, or translated content unintentionally.

### 4.7 Build, tests, CI, packaging, and release

- Debug and Release native/managed builds.
- Native CTest, managed custom test runner, Linux ASan/UBSan, vectors, benchmark comparator, resource probes, self-tests, deterministic assets, installer and portable verification.
- Test isolation for installed resident processes and desktop-interactive lanes.
- CI assertions must distinguish deterministic defects from contaminated desktop measurements.
- Documentation, screenshots, manifest, version, and release scripts must match actual product behavior.

### 4.8 Performance and resource behavior

- Hook callback latency, allocation budgets, resident memory, threads, handles, overlay activation, tray initialization, burst typing, and managed startup/resource probes.
- Use private working set/private memory and stable before/after deltas; treat total working set as environment-sensitive supporting evidence rather than a standalone invariant.
- Record contamination and duplicate-process state explicitly.

## 5. Architecture Boundaries

- `core/` remains the platform-independent source of truth for Vietnamese composition.
- `platform/windows/input/` owns resident hook delivery, tray, overlays, native profile loading, and self-tests.
- `apps/host/Keyina.Host.Core/` owns validated product contracts and pure domain behavior.
- `apps/host/Keyina.Host.Windows/` owns Windows adapters and secure OS integration.
- `apps/host/Keyina.Host/` owns orchestration and UI.
- UI controls may present and validate state but must not duplicate business rules already owned by core records/services.
- Tests must target the earliest incorrect boundary and avoid sleeps when an observable state can be awaited.

## 6. Error Handling

- Ordinary typing failures reset owned state and pass literal physical input through.
- Optional-service failures produce stable, content-free error codes and never disable the core input path.
- Configuration errors preserve the previous valid snapshot and identify the invalid field without exposing secrets.
- Resource and desktop tests report `contaminated`, `blocked_by_existing_resident`, `unstable_baseline`, or deterministic `failed` states distinctly.
- UI actions provide local validation, retain user input on recoverable errors, and restore predictable focus after success or cancellation.

## 7. Testing Strategy

### Focused regression tests

- Preserve the two uncommitted `russt` tests and verify whether an engine change is required.
- Add configuration/UI parity tests for theme and any additional orphaned fields found.
- Add deterministic resource-test precondition/lifecycle tests for an existing resident and contaminated desktop input.
- Add UI Automation/accessibility assertions for custom controls and theme selection.

### Full verification

- Fresh native Debug and Release configure/build/CTest.
- Managed Debug and Release build and all host tests.
- Native unit repetition and ordered full-suite repetition to detect flakiness.
- Linux sanitizer lane where available.
- Host/native self-tests, vectors, benchmark comparator, native and managed benchmarks.
- `git diff --check`, secret scan, artifact scan, final diff review, and documentation consistency review.

### Manual/runtime evidence

- Run desktop-sensitive tests only after detecting or explicitly stopping an installed resident; never terminate a user process silently.
- Verify representative browsers, editors, terminals, standard Win32 controls, password/elevated contexts, focus changes, rapid typing, Backspace, snippets, overlays, and optional-service states.
- Capture matched screenshots and accessibility-tree evidence when the environment supports it.

## 8. Acceptance Criteria

- Every confirmed defect has a failing regression or equivalent observable pre-fix check and a passing post-fix check.
- All persisted user-facing configuration fields have an intentional, tested runtime/UI path.
- Theme selection works for System, Light, and Dark; Windows high contrast always takes precedence.
- No deterministic native or managed test failures remain in clean Debug and Release runs.
- Desktop/resource tests identify contamination and existing residents without false claims.
- No secrets or private user content are committed, logged, exported, or included in diagnostics.
- Existing typing latency, allocation, memory, thread, and handle budgets remain satisfied or are revised only with documented measurement evidence.
- UI remains keyboard accessible, responsive, high-DPI safe, high-contrast safe, and free of missing primary states.
- Final documentation and release gates accurately describe the implemented product and remaining external/manual constraints.

## 9. Non-Goals

- Rewriting WinForms, replacing the native hook architecture, adding new cloud providers, adding speculative features, or redesigning unrelated visual identity.
- Word-specific dictionary patches when a structural typing rule is required.
- Silently killing the user's installed resident, modifying production credentials, publishing releases, or committing/pushing without explicit authorization.

## 10. Execution Order

1. Preserve and classify existing uncommitted work.
2. Build a contract-parity inventory and add missing parity tests.
3. Fix confirmed orphaned configuration/UI paths, beginning with theme.
4. Isolate and harden resource/desktop test preconditions and lifecycle.
5. Run focused native and managed regression suites.
6. Audit security/privacy and error paths.
7. Audit UI accessibility/responsiveness and fix confirmed gaps.
8. Run full Debug/Release/sanitizer/performance/release verification.
9. Inspect final diff, update evidence documentation, and report residual manual-only risks.
