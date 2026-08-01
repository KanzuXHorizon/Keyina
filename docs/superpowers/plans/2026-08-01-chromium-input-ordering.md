# Chromium Input Ordering Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the deferred single-slot race by delivering every transformed edit synchronously before the keyboard hook returns. Complete Chromium stream ordering is finalized by the follow-up owned-stream plan `2026-08-01-chromium-ordering-probe.md`.

**Architecture:** Replace the single-slot posted-message path with a pure delivery-mode selector and direct calls to the existing keyboard, selection-replacement, or clipboard injectors. Remove deferred state and messages so subsequent physical keys cannot overtake a pending edit.

**Tech Stack:** C++20, Win32 `WH_KEYBOARD_LL`, `SendInput`, CMake/CTest, PowerShell, Microsoft Edge.

## Global Constraints

- Preserve one native low-level keyboard hook and the current native resident architecture.
- Do not add a thread, dependency, unbounded queue, file I/O, network work, managed-host call, or process launch to the callback.
- Preserve injection markers, secure-input bypass, focus guards, elevated-target fail-open behavior, clipboard sequence restoration, and current Telex rules.
- Keep clipboard acquisition bounded to the existing five retries with 2 ms sleeps.
- Preserve all unrelated source changes.

---

### Task 1: Specify synchronous delivery policy

**Files:**
- Modify: `platform/windows/input/include/keyina/windows/input_injection.h`
- Modify: `tests/windows/input_injection_test.cpp`

**Interfaces:**
- Produces: `enum class TextDeliveryMode : std::uint8_t { Keyboard, SelectionReplacement, Clipboard };`
- Produces: `TextDeliveryMode ChooseTextDeliveryMode(bool clipboard_compatibility_enabled, bool chromium_target) noexcept`.
- Produces: `bool RequiresSelectionReplacementForWindowClass(std::wstring_view class_name) noexcept`.

- [ ] Add tests asserting clipboard mode takes precedence, Chromium selects synchronous selection replacement, ordinary targets use keyboard injection, and Chromium class matching no longer describes deferral.
- [ ] Run the focused native test build and verify compilation fails because the new enum/functions do not exist.

### Task 2: Implement the pure policy

**Files:**
- Modify: `platform/windows/input/input_injection.cpp`
- Modify: `platform/windows/input/include/keyina/windows/input_injection.h`

**Interfaces:**
- Consumes: booleans for explicit clipboard compatibility and Chromium target classification.
- Produces: deterministic `TextDeliveryMode` without allocation or Win32 calls.

- [ ] Implement the enum declarations and pure selector.
- [ ] Rename `ShouldDeferInputForWindowClass` to `RequiresSelectionReplacementForWindowClass`.
- [ ] Run the focused native tests and verify the new tests pass.

### Task 3: Remove asynchronous text delivery

**Files:**
- Modify: `platform/windows/input/include/keyina/windows/win32_input_runtime.h`
- Modify: `platform/windows/input/win32_input_runtime.cpp`

**Interfaces:**
- Consumes: `ChooseTextDeliveryMode` and `RequiresSelectionReplacementForWindowClass`.
- Removes: `kDeferredInputMessage`, `QueueDeferredInput`, `ProcessDeferredInput`, and all `pending_input_*` state.

- [ ] Replace the deferred branch in `HandleKeyboardEvent` with a switch that directly calls `Inject`, `InjectWithSelectionReplacement`, or `InjectViaClipboard`.
- [ ] Remove the deferred message handler, queue methods, shutdown cleanup, and pending state.
- [ ] Keep successful/failed injection counters and fail-open reset behavior unchanged.
- [ ] Build native Debug and run CTest.

### Task 4: Verify release and real Edge ordering

**Files:**
- No production file changes expected.

**Interfaces:**
- Uses: Release `KeyinaInput.exe`, real Microsoft Edge, opt-in callback profiler.

- [ ] Build native and managed Release.
- [ ] Run all native Release tests and all managed tests.
- [ ] Publish a reversible bundle under `artifacts/publish/win-x64`.
- [ ] Run the real Edge burst harness at 0 ms, 5 ms, and 10 ms; each result must equal `tùy bạn cứ research và đưa ra hướng tốt nhất`.
- [ ] Run the native callback profiler and compare ordinary and transformed-path percentiles with the pre-change baseline.
- [ ] Run `git diff --check`, inspect the final diff, and record residual risk for clipboard contention and elevated targets.
