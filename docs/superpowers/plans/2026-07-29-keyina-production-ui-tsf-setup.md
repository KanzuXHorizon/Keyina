# Keyina Production UI and TSF Setup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver a polished Windows-native settings experience that reports truthful readiness and gives the user a real path to build, register, verify, and test the Keyina TSF input method.

**Architecture:** Keep WinForms and the existing resident host. Introduce a small immutable readiness model and Windows TSF setup service, then drive Overview, Typing, Diagnostics, and tray state from the same snapshot. Keep privileged registration explicit and isolate it behind a service boundary so tests use fakes.

**Tech Stack:** .NET 10, Windows Forms, C# records, Windows registry/process APIs, native x64 TSF DLL, existing host test runner.

## Global Constraints

- Ordinary typing remains offline and independent of speech.
- The UI must not report Ready until required TSF checks pass.
- Do not silently change unrelated Windows language profiles.
- Keep all changes compatible with the existing WinForms and native TSF architecture.
- Preserve unrelated working-tree changes.

---

### Task 1: Readiness model and mapping

**Files:**
- Create: `apps/host/Keyina.Host/UI/ReadinessModels.cs`
- Modify: `apps/host/Keyina.Host/UI/SettingsModels.cs`
- Test: `apps/host/Keyina.Host.Tests/SettingsFormTests.cs`

**Interfaces:**
- Produces: `KeyinaReadiness`, `KeyinaHealthSnapshot`, `TsfSetupState`, and deterministic readiness mapping.

- [ ] Add tests for Ready, NeedsSetup, NeedsAttention, and Unavailable mapping.
- [ ] Run focused host tests and confirm they fail before implementation.
- [ ] Implement immutable health/readiness records and mapping.
- [ ] Run focused tests and confirm they pass.

### Task 2: TSF health and setup service

**Files:**
- Create: `apps/host/Keyina.Host/Runtime/TsfSetupService.cs`
- Create: `apps/host/Keyina.Host/Runtime/TsfSetupModels.cs`
- Test: `apps/host/Keyina.Host.Tests/TsfSetupServiceTests.cs`

**Interfaces:**
- Produces: `ITsfSetupService.CheckAsync`, `RegisterAsync`, `OpenLanguageSettings`, and structured setup results.

- [ ] Add tests for missing DLL, present DLL, registration cancellation, and command construction.
- [ ] Run focused tests and confirm they fail.
- [ ] Implement non-blocking checks, explicit elevated registration, and Windows language-settings launch.
- [ ] Run focused tests and confirm they pass.

### Task 3: Production settings shell redesign

**Files:**
- Rewrite: `apps/host/Keyina.Host/UI/SettingsForm.cs`
- Modify: `apps/host/Keyina.Host/UI/SettingsScreenshotRenderer.cs`
- Test: `apps/host/Keyina.Host.Tests/SettingsFormTests.cs`

**Interfaces:**
- Consumes: `SettingsSnapshot`, readiness model, and `SettingsActions`.
- Produces: responsive Overview, Typing, Speech, Hotkeys, Snippets, and Diagnostics pages.

- [ ] Add assertions for minimum size, navigation, readiness CTA, typing test controls, accessible names, and no fixed clipped overview layout.
- [ ] Run focused tests and confirm failure.
- [ ] Implement the redesigned shell using docked/table/flow layouts, Vietnamese-first copy, consistent cards, and keyboard navigation.
- [ ] Add a real focused typing field and pass/fail evaluator without counting direct-engine checks as end-to-end readiness.
- [ ] Run focused tests and screenshot renderer checks.

### Task 4: Runtime integration and truthful tray state

**Files:**
- Modify: `apps/host/Keyina.Host/Runtime/KeyinaApplicationContext.cs`
- Modify: `apps/host/Keyina.Host/Runtime/KeyinaRuntimeOptions.cs`
- Modify: `apps/host/Keyina.Host/Program.cs`
- Test: `apps/host/Keyina.Host.Tests/KeyinaApplicationContextTests.cs`

**Interfaces:**
- Consumes: TSF setup service and health snapshot.
- Produces: shared settings/tray readiness state and setup/repair actions.

- [ ] Add tests proving tray/settings cannot claim Ready when TSF is unavailable.
- [ ] Implement async health refresh and action wiring.
- [ ] Ensure startup opens settings only when requested and process remains resident.
- [ ] Run runtime tests.

### Task 5: Full verification and runnable alpha

**Files:**
- Modify only as required by failing verification.

- [ ] Build `Keyina.slnx` in Debug with warnings as errors.
- [ ] Run all host tests.
- [ ] Build native Debug TSF and run native tests.
- [ ] Launch the host and confirm the Settings process remains alive.
- [ ] Inspect final Git diff for accidental scope and secrets.
- [ ] Report remaining manual/elevated registration gates honestly.
