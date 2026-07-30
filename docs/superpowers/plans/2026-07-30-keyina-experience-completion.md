# Keyina Experience Completion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete Keyina's missing daily-use UX with configurable shortcuts, setup guidance, safe portability, application exclusions, reversible translation, provider fallback, and accessibility/DPI hardening.

**Architecture:** Extend the existing additive configuration model and resident runtime rather than adding new hooks or services. Each capability is isolated behind a small core contract, a Windows adapter where required, runtime orchestration, Settings UI, and focused tests. Runtime changes are transactional and optional feature failures must fail open without disabling Vietnamese typing.

**Tech Stack:** .NET 10, Windows Forms, Win32 `RegisterHotKey`, Windows Credential Manager, existing Keyina host/core/windows projects, deterministic custom test runner.

## Global Constraints

- Do not install a second low-level keyboard hook.
- Keep schema-one configuration files backward compatible through additive defaults.
- Never serialize or export credentials.
- Never log typed, selected, translated, or dictated content.
- Preserve existing DeepL token protection and focus guards.
- Keep Vietnamese typing functional when any optional feature fails.
- All UI copy is Vietnamese-first and keyboard accessible.

---

### Task 1: Configurable shortcut contracts and validation

**Files:**
- Create: `apps/host/Keyina.Host.Core/Hotkeys/HotkeyPreferences.cs`
- Modify: `apps/host/Keyina.Host.Core/Hotkeys/HotkeyChord.cs`
- Modify: `apps/host/Keyina.Host.Core/Hotkeys/HotkeyCommand.cs`
- Modify: `apps/host/Keyina.Host.Core/Configuration/KeyinaConfiguration.cs`
- Test: `apps/host/Keyina.Host.Tests/HotkeyPreferencesTests.cs`
- Test: `apps/host/Keyina.Host.Tests/ConfigurationStoreTests.cs`

**Interfaces:**
- Produces: `HotkeyGestureKind`, `HotkeyPreference`, `HotkeyPreferences.Default`, `HotkeyText.Format`, `HotkeyText.TryParse`, `HotkeyPreferences.Validate()`.
- Configuration property: `KeyinaConfiguration.Hotkeys` with default fallback.

- [ ] **Step 1:** Add tests proving default bindings, parsing/formatting, duplicate rejection, Windows-key rejection, plain-letter rejection, and legacy configuration defaults.
- [ ] **Step 2:** Run `Keyina.Host.Tests.exe "hotkey preferences" "schema one configuration"` and confirm the new tests fail.
- [ ] **Step 3:** Implement the contracts and additive configuration property with deterministic validation.
- [ ] **Step 4:** Run the focused tests and confirm they pass.
- [ ] **Step 5:** Commit `feat(hotkeys): add configurable shortcut preferences`.

### Task 2: Transactional runtime shortcut application

**Files:**
- Modify: `apps/host/Keyina.Host.Windows/Hotkeys/RegisteredHotkeyManager.cs`
- Modify: `apps/host/Keyina.Host.Windows/Hotkeys/ModifierKeyboardHook.cs`
- Modify: `apps/host/Keyina.Host.Core/Hotkeys/ModifierToggleStateMachine.cs`
- Modify: `apps/host/Keyina.Host/Runtime/KeyinaApplicationContext.cs`
- Modify: `apps/host/Keyina.Host/UI/SettingsModels.cs`
- Test: `apps/host/Keyina.Host.Tests/RegisteredHotkeyManagerTests.cs`
- Test: `apps/host/Keyina.Host.Tests/HotkeyStateMachineTests.cs`
- Test: `apps/host/Keyina.Host.Tests/KeyinaApplicationContextTests.cs`

**Interfaces:**
- Produces: `RegisteredHotkeyManager.TryReplaceAll(...)`, configurable modifier/hold hook bindings, `SettingsActions.SetHotkey`, `SettingsActions.ResetHotkey`, `SettingsActions.ResetAllHotkeys`.

- [ ] **Step 1:** Add tests for complete-set replacement, rollback after registration conflict, custom push-to-talk release, custom modifier gesture, persistence only after success, and reset behavior.
- [ ] **Step 2:** Run the focused tests and confirm failure for missing APIs.
- [ ] **Step 3:** Implement transactional registration and configurable shared-hook behavior.
- [ ] **Step 4:** Route tray display strings and runtime bindings through `configuration.Hotkeys`.
- [ ] **Step 5:** Run focused tests and the live hook tests.
- [ ] **Step 6:** Commit `feat(hotkeys): apply custom shortcuts transactionally`.

### Task 3: Shortcut capture and Settings UX

**Files:**
- Create: `apps/host/Keyina.Host/UI/HotkeyCaptureDialog.cs`
- Modify: `apps/host/Keyina.Host/UI/SettingsForm.cs`
- Modify: `apps/host/Keyina.Host/UI/SettingsModels.cs`
- Modify: `apps/host/Keyina.Host/UI/SettingsScreenshotRenderer.cs`
- Test: `apps/host/Keyina.Host.Tests/SettingsFormTests.cs`
- Test: `apps/host/Keyina.Host.Tests/HotkeyCaptureDialogTests.cs`

**Interfaces:**
- Consumes: `HotkeyText`, runtime settings actions, snapshot shortcut statuses.
- Produces: editable shortcut rows, capture dialog, per-row restore, restore-all.

- [ ] **Step 1:** Add tests for accessible controls, capture/cancel, invalid and duplicate inline errors, restore actions, and snapshot display.
- [ ] **Step 2:** Run focused UI tests and confirm failure.
- [ ] **Step 3:** Implement the capture dialog and replace static shortcut rows with editable rows.
- [ ] **Step 4:** Extend screenshot gallery coverage and render the Hotkeys page.
- [ ] **Step 5:** Run UI tests and screenshot generation.
- [ ] **Step 6:** Commit `feat(ui): add accessible shortcut editor`.

### Task 4: First-run setup and safe settings portability

**Files:**
- Create: `apps/host/Keyina.Host.Core/Configuration/PortableSettingsDocument.cs`
- Create: `apps/host/Keyina.Host/Configuration/PortableSettingsService.cs`
- Create: `apps/host/Keyina.Host/UI/FirstRunForm.cs`
- Modify: `apps/host/Keyina.Host.Core/Configuration/KeyinaConfiguration.cs`
- Modify: `apps/host/Keyina.Host/Runtime/KeyinaApplicationContext.cs`
- Modify: `apps/host/Keyina.Host/UI/SettingsForm.cs`
- Modify: `apps/host/Keyina.Host/UI/SettingsModels.cs`
- Test: `apps/host/Keyina.Host.Tests/PortableSettingsServiceTests.cs`
- Test: `apps/host/Keyina.Host.Tests/FirstRunFormTests.cs`
- Test: `apps/host/Keyina.Host.Tests/KeyinaApplicationContextTests.cs`

**Interfaces:**
- Produces: versioned export/import without credentials, `FirstRunCompleted`, setup navigation actions.

- [ ] **Step 1:** Add tests proving credentials are absent, invalid imports do not replace current settings, valid imports apply transactionally, and first run only appears for a missing configuration file.
- [ ] **Step 2:** Run focused tests and confirm failure.
- [ ] **Step 3:** Implement portable document/service and first-run UI.
- [ ] **Step 4:** Add Settings import/export actions and privacy copy.
- [ ] **Step 5:** Run focused tests and screenshot generation.
- [ ] **Step 6:** Commit `feat(settings): add onboarding and safe portability`.

### Task 5: Per-application exclusions

**Files:**
- Create: `apps/host/Keyina.Host.Core/Applications/ApplicationPreferences.cs`
- Create: `apps/host/Keyina.Host.Windows/Applications/ForegroundApplicationProbe.cs`
- Modify: `apps/host/Keyina.Host.Core/Configuration/KeyinaConfiguration.cs`
- Modify: `apps/host/Keyina.Host/Runtime/KeyinaApplicationContext.cs`
- Modify: `apps/host/Keyina.Host.Windows/Typing/VietnameseKeyboardHook.cs`
- Modify: `apps/host/Keyina.Host/UI/SettingsForm.cs`
- Modify: `apps/host/Keyina.Host/UI/SettingsModels.cs`
- Test: `apps/host/Keyina.Host.Tests/ApplicationPreferencesTests.cs`
- Test: `apps/host/Keyina.Host.Tests/KeyinaApplicationContextTests.cs`
- Test: `apps/host/Keyina.Host.Tests/SettingsFormTests.cs`

**Interfaces:**
- Produces: normalized executable-name rules and runtime checks for typing, speech, translation, and visual feedback.

- [ ] **Step 1:** Add tests for normalization, duplicate/path/wildcard rejection, case-insensitive matching, and each runtime exclusion.
- [ ] **Step 2:** Run focused tests and confirm failure.
- [ ] **Step 3:** Implement core rules and Windows foreground executable probe.
- [ ] **Step 4:** Apply exclusions without weakening secure-field bypass.
- [ ] **Step 5:** Add bounded Settings list editors and tests.
- [ ] **Step 6:** Commit `feat(apps): add per-application exclusions`.

### Task 6: Reversible translation

**Files:**
- Create: `apps/host/Keyina.Host/Translation/TranslationUndoManager.cs`
- Modify: `apps/host/Keyina.Host.Core/Hotkeys/HotkeyCommand.cs`
- Modify: `apps/host/Keyina.Host.Core/Hotkeys/HotkeyPreferences.cs`
- Modify: `apps/host/Keyina.Host/Translation/ClipboardSelectionAccessor.cs`
- Modify: `apps/host/Keyina.Host/Translation/TranslationCoordinator.cs`
- Modify: `apps/host/Keyina.Host/Runtime/KeyinaApplicationContext.cs`
- Modify: `apps/host/Keyina.Host/UI/SettingsForm.cs`
- Test: `apps/host/Keyina.Host.Tests/TranslationUndoManagerTests.cs`
- Test: `apps/host/Keyina.Host.Tests/TranslationCoordinatorTests.cs`
- Test: `apps/host/Keyina.Host.Tests/KeyinaApplicationContextTests.cs`

**Interfaces:**
- Produces: `HotkeyCommand.UndoTranslation`, default `Ctrl+Alt+Z`, one-shot expiring undo entry.

- [ ] **Step 1:** Add tests for successful undo, one-shot behavior, expiration, focus mismatch, replacement by newer translation, and no content logging.
- [ ] **Step 2:** Run focused tests and confirm failure.
- [ ] **Step 3:** Implement undo manager and selection restoration contract.
- [ ] **Step 4:** Wire runtime, tray, Settings display, and feedback.
- [ ] **Step 5:** Run focused tests and live focus-guard tests.
- [ ] **Step 6:** Commit `feat(translation): add safe one-shot undo`.

### Task 7: Provider fallback and final UX hardening

**Files:**
- Create: `apps/host/Keyina.Host.Core/Translation/TranslationProviderPreferences.cs`
- Create: `apps/host/Keyina.Host/Translation/LibreTranslateProvider.cs`
- Create: `apps/host/Keyina.Host/Translation/FallbackTranslationProvider.cs`
- Create: `apps/host/Keyina.Host.Windows/Networking/SafeEndpointValidator.cs`
- Modify: `apps/host/Keyina.Host.Core/Configuration/KeyinaConfiguration.cs`
- Modify: `apps/host/Keyina.Host/Runtime/KeyinaApplicationContext.cs`
- Modify: `apps/host/Keyina.Host/UI/SettingsForm.cs`
- Modify: `apps/host/Keyina.Host/UI/SettingsModels.cs`
- Modify: `docs/translation.md`
- Test: `apps/host/Keyina.Host.Tests/FallbackTranslationProviderTests.cs`
- Test: `apps/host/Keyina.Host.Tests/LibreTranslateProviderTests.cs`
- Test: `apps/host/Keyina.Host.Tests/SafeEndpointValidatorTests.cs`
- Test: `apps/host/Keyina.Host.Tests/SettingsFormTests.cs`

**Interfaces:**
- Produces: ordered provider preferences, guarded LibreTranslate-compatible fallback, complete error/disabled/loading UX.

- [ ] **Step 1:** Add tests proving fallback only on unavailable/rate/quota failures, no fallback on auth/token corruption, HTTPS/private-address rules, bounds, and secret isolation.
- [ ] **Step 2:** Run focused tests and confirm failure.
- [ ] **Step 3:** Implement endpoint validator, LibreTranslate provider, and fallback router.
- [ ] **Step 4:** Add provider Settings UI, accessibility descriptions, and recovery-oriented errors.
- [ ] **Step 5:** Run the full Debug build and host suite.
- [ ] **Step 6:** Run Release build, resource self-test, benchmarks, repeated live-hook tests, screenshot gallery, `git diff --check`, and secret-shape scan.
- [ ] **Step 7:** Commit `feat: complete configurable Keyina desktop experience`.
