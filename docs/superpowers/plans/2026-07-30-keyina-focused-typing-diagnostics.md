# Focused Typing Diagnostics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a target-scoped typing sandbox that captures exact physical, engine, and visible-output evidence only for the dedicated Diagnostics text box.

**Architecture:** A bounded static `TypingDiagnosticTrace` in `Keyina.Host.Windows` accepts records only when the current focused HWND equals the UI-activated target. The resident hook supplies physical and engine events, while `SettingsForm` supplies WinForms output events and renders filtered snapshots on a UI timer.

**Tech Stack:** .NET 10, C# 12, WinForms, existing Keyina custom test runner.

## Global Constraints

- Capture exact keys and text only for `typingDiagnosticInput` while it owns focus.
- Do not change the privacy contract of `TypingTraceBuffer` or typing-latency telemetry.
- Keep normal typing overhead to one inactive enabled-state check.
- Bound memory and clear sensitive trace data when the settings form closes.
- Do not modify clipboard, speech, translation, versioning, or unrelated engine behavior.

---

### Task 1: Target-scoped trace model

**Files:**
- Create: `apps/host/Keyina.Host.Windows/Typing/TypingDiagnosticTrace.cs`
- Create: `apps/host/Keyina.Host.Tests/TypingDiagnosticTraceTests.cs`

**Interfaces:**
- Produces: `TypingDiagnosticTrace.Activate(nint)`, `Deactivate(nint)`, `Clear()`, `ClearAndDisable()`, `RecordPhysical(...)`, `RecordEngine(...)`, `RecordOutput(...)`, `Snapshot(...)`, and `FormatSnapshot(...)`.

- [x] Write tests proving inactive and wrong-target records are rejected.
- [x] Run the focused tests and confirm they fail because `TypingDiagnosticTrace` does not exist.
- [x] Implement the bounded trace and exact HWND gate.
- [x] Run focused tests and confirm they pass.
- [x] Add tests for repeated key-down classification, filtering, and secure clear.
- [x] Implement only the behavior required by those tests and keep the suite green.

### Task 2: Resident-hook instrumentation

**Files:**
- Modify: `apps/host/Keyina.Host.Windows/Typing/VietnameseKeyboardHook.cs`
- Modify: `apps/host/Keyina.Host.Tests/VietnameseKeyboardHookTests.cs`

**Interfaces:**
- Consumes: `TypingDiagnosticTrace` from Task 1.
- Produces: physical key down/up and engine-decision records with the current `VietnameseTypingContext.FocusWindow`.

- [x] Add failing tests showing a matching target receives physical and transform events while a different focused HWND receives none.
- [x] Extend `VietnameseKeyboardEvent` with an optional scan-code value and populate it from `LowLevelKeyboardInput`.
- [x] Record physical events only while target tracing is active.
- [x] Record bypass, literal pass-through, transform, and injection-failure decisions without changing consume/fail-open behavior.
- [x] Run the hook tests and then all typing trace tests.

### Task 3: Diagnostics sandbox UI

**Files:**
- Modify: `apps/host/Keyina.Host/UI/SettingsForm.cs`
- Modify: `apps/host/Keyina.Host.Tests/SettingsFormTests.cs`

**Interfaces:**
- Consumes: `TypingDiagnosticTrace` from Task 1.
- Produces: controls `typingDiagnosticInput`, `typingDiagnosticStatus`, `typingDiagnosticFilter`, `typingDiagnosticLog`, `clearTypingDiagnostic`, `copyTypingDiagnostic`, and `exportTypingDiagnostic`.

- [x] Add a failing SettingsForm test for the complete card, explicit privacy copy, and read-only bounded log.
- [x] Add failing interaction tests for focus activation, leave/pause retention, output-event recording, clear, and filter rendering.
- [x] Build the Diagnostics card using the existing Fluent card, input-frame, label, and button patterns.
- [x] Attach `KeyDown`, `KeyPress`, `KeyUp`, `TextChanged`, `Enter`, and `Leave` handlers.
- [x] Refresh snapshots through a WinForms timer, and dispose the timer plus clear the trace in form cleanup.
- [x] Implement copy/export with UTF-8 and the existing safe error presentation.
- [x] Run the focused SettingsForm tests.

### Task 4: Verification and integration

**Files:**
- Modify if needed: `docs/superpowers/specs/2026-07-30-keyina-focused-typing-diagnostics-design.md`
- Modify if needed: `docs/superpowers/plans/2026-07-30-keyina-focused-typing-diagnostics.md`

- [x] Run `dotnet run --project apps/host/Keyina.Host.Tests/Keyina.Host.Tests.csproj -c Release` and require all tests to pass.
- [x] Run `dotnet build Keyina.slnx -c Release --no-restore` and require success.
- [x] Run the Impeccable detector once over the changed UI target.
- [x] Inspect `git diff --check`, `git status --short`, and the final scoped diff for privacy leaks or unrelated edits.
- [ ] Commit the isolated branch with a focused feature commit, then integrate it into the clean source checkout without overwriting unrelated work.
