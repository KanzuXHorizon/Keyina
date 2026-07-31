# Native Resident Hot Path Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reduce repeated Win32 work in the native keyboard hook path while preserving compatibility, literal fallback, and current input behavior.

**Architecture:** Keep the existing C++ resident and low-level keyboard hook, but cache window-class compatibility decisions by focused HWND. Invalidate the cache whenever focus changes or context capture fails. Continue deferring Chromium injection to the resident message loop and keep all uncertain/error paths fail-open.

**Tech Stack:** C++20, Win32 `WH_KEYBOARD_LL`, `SendInput`, CMake/CTest.

## Global Constraints

- Do not copy source or algorithms from VKey; use only public behavior and primary Windows documentation as references.
- Preserve all unrelated working-tree changes.
- The hook callback must not perform file, network, managed-host, or process-launch work.
- Any cache miss or Win32 failure must preserve the current literal fail-open behavior.
- No new runtime dependency or background thread.

---

### Task 1: Cache Chromium compatibility by focused window

**Files:**
- Modify: `platform/windows/input/include/keyina/windows/win32_input_runtime.h`
- Modify: `platform/windows/input/win32_input_runtime.cpp`
- Test: `tests/windows/input_injection_test.cpp`

**Interfaces:**
- Produces: `bool IsDeferredInputTarget(std::uintptr_t focus_window) noexcept`.
- Consumes: existing `ShouldDeferInputForWindowClass(std::wstring_view)`.

- [ ] Add focused-window cache state to `Win32InputRuntime`.
- [ ] Implement `IsDeferredInputTarget` so `GetClassNameW` runs only when the focused HWND changes.
- [ ] Replace the per-suppressed-edit class lookup in `HandleKeyboardEvent` with the cached helper.
- [ ] Clear the cache when context capture fails or the active/focus window changes.
- [ ] Run native input tests and the native Debug build.

### Task 2: Verify native resident behavior and scope

**Files:**
- Inspect: `platform/windows/input/win32_input_runtime.cpp`
- Inspect: `platform/windows/input/input_injection.cpp`

- [ ] Run `ctest --preset windows-msvc-debug --output-on-failure`.
- [ ] Run native resident self-tests available in the Debug build.
- [ ] Run `git diff --check` and inspect the focused diff for accidental changes.
- [ ] Record any remaining hot-path risks for the next slice: hook latency histogram, `SendInput` failure classification, and application-profile cache.
