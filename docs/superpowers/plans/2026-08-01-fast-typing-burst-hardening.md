# Fast Typing Burst Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prevent held or rapidly repeated Telex keys from mutating composition more than once per physical press, while preserving ordinary literal repeat, Backspace repeat, deterministic ordering, and fail-open behavior.

**Architecture:** Propagate the low-level hook's existing `was_pressed` signal as an explicit `key_repeat` property on `PhysicalKeyEvent`. The resident controller will suppress a repeated key-down only when the original key-down is already owned by Keyina, avoiding a second engine transition while leaving unowned literal events unchanged. Verification uses controller regression tests and the existing native callback/transform latency self-tests.

**Tech Stack:** C++20, Win32 `WH_KEYBOARD_LL`, CMake/CTest native tests.

## Global Constraints

- Do not move composition or input injection to an asynchronous worker; physical and injected ordering must remain deterministic.
- Do not suppress repeat for ordinary unowned literal keys or Backspace.
- Preserve fail-open behavior for unknown, secure, bypassed, elevated, unsupported, and failed-injection contexts.
- Add no heap allocation to the ordinary controller path.
- Preserve all unrelated working-tree changes.

---

### Task 1: Represent and suppress owned key repeats

**Files:**
- Modify: `platform/windows/input/include/keyina/windows/resident_input_controller.h`
- Modify: `platform/windows/input/win32_input_runtime.cpp`
- Modify: `platform/windows/input/resident_input_controller.cpp`
- Test: `tests/windows/resident_input_controller_test.cpp`

**Interfaces:**
- Produces: `PhysicalKeyEvent::key_repeat`, true for a repeated key-down observed while the virtual key is already pressed.
- Consumes: `ResidentInputController::suppressed_keys_`, which records keys whose original physical event was replaced by Keyina.

- [ ] Add regression tests proving a repeated owned Telex key-down emits no second edit and remains suppressed until key-up.
- [ ] Add a regression test proving repeated unowned literal input remains pass-through.
- [ ] Run the native test binary and verify the new tests fail before implementation.
- [ ] Add `key_repeat` to `PhysicalKeyEvent` and populate it from `key_down && was_pressed` in the Win32 hook callback.
- [ ] In `ResidentInputController::Process`, return a suppress-only decision for repeated key-down when `suppressed_keys_` already owns that virtual key.
- [ ] Run the native suite and verify all tests pass.
- [ ] Run callback and transform callback latency self-tests and verify zero failed injections and no material controller regression.

### Task 2: Burst and focus regression coverage

**Files:**
- Modify: `tests/windows/resident_input_controller_test.cpp`
- Modify only if required by a reproduced failure: `platform/windows/input/resident_input_controller.cpp`

**Interfaces:**
- Consumes: `PhysicalKeyEvent::key_repeat` and existing `TypingContext` equality/reset behavior.

- [ ] Add a burst test that replays rapid transformed key-down repeats, matching key-up, subsequent literal characters, and a commit boundary; assert exact visible text after every event.
- [ ] Add a focus-change test while a repeat-owned key is active; assert controller state resets and the new target receives no stale transformation.
- [ ] Add a boundary-recovery test followed by an owned repeated modifier; assert no duplicate mutation or stale restoration.
- [ ] Run the complete native test suite and one-million-event endurance tests.

### Task 3: Delivery telemetry and partial-send policy investigation

**Files:**
- Inspect: `platform/windows/input/win32_input_runtime.cpp`
- Inspect: `platform/windows/input/include/keyina/windows/win32_input_runtime.h`
- Test: existing callback and transform callback self-tests.

**Interfaces:**
- No behavior change without a reproducible partial-send test seam.

- [ ] Confirm which injection helpers can observe the exact `SendInput` sent count.
- [ ] Define a testable injectable SendInput seam before changing production recovery behavior.
- [ ] Record separate full failure and partial-send counters only after a failing regression test exists.
- [ ] Do not claim transactional recovery: Win32 `SendInput` may have already delivered a destructive prefix.
