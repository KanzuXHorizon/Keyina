# Keyina Hook Backend Implementation Plan

> **Current behavior note (2026-08-03):** This historical plan predates the literal Backspace policy. Native, managed, and optional TSF input paths now reset composition and pass physical Backspace through instead of reconstructing raw Telex state.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace TSF as Keyina's default typing path with a UniKey/EVKey-style resident keyboard-hook backend that requires no `Win + Space`, preserves the existing clean-room Telex engine, and safely falls back in incompatible contexts.

**Architecture:** Export the existing C++ `keyina::Engine` through a narrow C ABI DLL, consume it from the .NET Windows host, and run a low-level keyboard hook that suppresses only transformed key-down events. Apply edits with minimal Backspace plus Unicode `SendInput`, mark injected events with a private `dwExtraInfo` signature, reset on focus/navigation/selection-risk boundaries, and pass through secure/elevated or unsupported contexts. TSF remains buildable but is no longer required for readiness or ordinary typing.

**Tech Stack:** C++20, C ABI DLL, CMake/MSVC, .NET 10 WinForms, P/Invoke, `WH_KEYBOARD_LL`, `SendInput`, Windows UI Automation/password heuristics where available.

## Global Constraints

- Do not copy GPL source code from UniKey/OpenKey into the Apache-2.0 repository.
- Ordinary typing must remain offline and must not use clipboard replacement.
- Injected events must never be reprocessed by Keyina.
- Modifier shortcuts, secure input, password fields, elevated targets, games/raw-input contexts, and unknown failures must fail open to literal input.
- The default path must not require a Windows language profile or `Win + Space`.
- Existing 100 golden Telex vectors and native/host tests must remain green.

---

### Task 1: Native engine bridge

**Files:**
- Create: `platform/windows/hook/engine_bridge.cpp`
- Create: `platform/windows/hook/engine_bridge.def`
- Create: `platform/windows/hook/CMakeLists.txt`
- Modify: `CMakeLists.txt`
- Test: `tests/windows/hook_engine_bridge_test.cpp`

**Interfaces:**
- Produces: opaque `keyina_engine_handle`; `keyina_engine_create`, `keyina_engine_destroy`, `keyina_engine_reset`, `keyina_engine_process`, `keyina_engine_visible`.
- Uses UTF-32 input and UTF-16 output buffers with explicit capacities; never allocates across ABI boundaries.

- [ ] Write a failing native test that loads `KeyinaEngine.dll`, types `tieengs Vieetj`, and verifies minimal erase/insert edits and `tiếng Việt` output.
- [ ] Build and run the focused test to verify it fails because the DLL/exports do not exist.
- [ ] Implement the minimal C ABI bridge over `keyina::Engine`.
- [ ] Build and run the focused test until it passes.

### Task 2: Hook edit planner and injector

**Files:**
- Create: `apps/host/Keyina.Host.Windows/Typing/HookEdit.cs`
- Create: `apps/host/Keyina.Host.Windows/Typing/UnicodeInputInjector.cs`
- Test: `apps/host/Keyina.Host.Tests/HookEditTests.cs`
- Test: `apps/host/Keyina.Host.Tests/UnicodeInputInjectorTests.cs`

**Interfaces:**
- Produces: `HookEdit(int BackspaceCount, string InsertText, bool ConsumePhysicalKey)`.
- Produces: `IUnicodeInputInjector.Apply(HookEdit edit)` using `SendInput` and a stable private injection marker.

- [ ] Write failing tests for minimal Backspace planning, surrogate-pair Unicode emission, and injection marker handling.
- [ ] Run focused host tests and verify RED.
- [ ] Implement the planner and injector.
- [ ] Run focused tests and verify GREEN.

### Task 3: Resident Vietnamese keyboard hook

**Files:**
- Create: `apps/host/Keyina.Host.Windows/Typing/NativeEngineClient.cs`
- Create: `apps/host/Keyina.Host.Windows/Typing/VietnameseKeyboardHook.cs`
- Modify: `apps/host/Keyina.Host.Windows/Hotkeys/ModifierKeyboardHook.cs`
- Test: `apps/host/Keyina.Host.Tests/VietnameseKeyboardHookTests.cs`

**Interfaces:**
- Consumes: C ABI bridge and `IUnicodeInputInjector`.
- Produces: `Start`, `SetEnabled`, `Reset`, `Dispose`; hook callback returns whether to suppress the physical event.

- [ ] Write failing tests for `tieengs`, Backspace reconstruction, boundaries, modifier shortcuts, injected-event bypass, focus reset, and disabled mode.
- [ ] Run focused tests and verify RED.
- [ ] Implement one shared low-level hook dispatch path so hotkeys and Vietnamese typing do not install competing hooks.
- [ ] Run focused tests and verify GREEN.

### Task 4: Compatibility and safety boundaries

**Files:**
- Create: `apps/host/Keyina.Host.Windows/Typing/TypingContextGuard.cs`
- Test: `apps/host/Keyina.Host.Tests/TypingContextGuardTests.cs`

**Interfaces:**
- Produces: `TypingContextDecision` with `Allow`, `ResetAndPassThrough`, or `Blocked` plus reason code.

- [ ] Write failing tests for Windows Search, Notepad, Chrome, terminal, password controls, elevated processes, navigation keys, mouse/focus changes, and Ctrl/Alt/Win shortcuts.
- [ ] Implement fail-open context decisions and per-process overrides.
- [ ] Verify tests pass.

### Task 5: Runtime integration and TSF de-emphasis

**Files:**
- Modify: `apps/host/Keyina.Host/Runtime/KeyinaApplicationContext.cs`
- Modify: `apps/host/Keyina.Host/Runtime/KeyinaRuntimeOptions.cs`
- Modify: `apps/host/Keyina.Host/UI/SettingsModels.cs`
- Modify: `apps/host/Keyina.Host/UI/SettingsForm.cs`
- Test: `apps/host/Keyina.Host.Tests/KeyinaApplicationContextTests.cs`
- Test: `apps/host/Keyina.Host.Tests/SettingsFormTests.cs`

**Interfaces:**
- Runtime readiness depends on hook backend health, not TSF registration.
- UI exposes Compatibility mode, per-app reset, and diagnostics; removes setup/`Win + Space` requirements from the primary flow.

- [ ] Write failing runtime/UI tests for hook readiness and TSF-independent operation.
- [ ] Integrate `VietnameseKeyboardHook` lifecycle and enabled state.
- [ ] Update tray/settings copy and diagnostics.
- [ ] Verify focused tests pass.

### Task 6: Regression and manual verification

**Files:**
- Modify: `README.md`
- Modify: `docs/compatibility/typing.md`

- [ ] Run full Debug host tests.
- [ ] Run native Debug build and tests including bridge tests.
- [ ] Launch Keyina without selecting a Windows input profile.
- [ ] Manually verify Notepad, Windows Search, Chrome/Edge, VS Code, and the in-app test field.
- [ ] Verify select-all then typing does not crash; navigation and shortcuts reset safely.
- [ ] Record unresolved elevated/raw-input limitations truthfully.
