# Chromium Owned Text Stream Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prevent Chromium burst corruption by making literal and transformed text one marked Keyina delivery stream, with deterministic unit coverage and an explicit interactive ordering diagnostic.

**Architecture:** Add pure owned-stream and UTF-16 literal policies to the native injection module. In the Win32 hook, own supported text only for safe Chromium selection-replacement contexts, suppress matching key-up events with a fixed bitset, and retain fail-open behavior. Keep the foreground-dependent probe outside default CTest.

**Tech Stack:** C++20, Win32 `WH_KEYBOARD_LL`, `SendInput`, CMake/CTest.

## Global Constraints

- Production Chromium classification remains class-based and cached by focused HWND.
- Owned text is disabled for secure/bypassed contexts, disabled Vietnamese mode, clipboard delivery, and ordinary targets.
- No production thread, queue, dependency, network, file, or managed-host work is added.
- Preserve injection markers, hotkeys, Backspace/navigation behavior, clipboard restoration, and fail-open behavior.

---

### Task 1: Specify and test pure owned-stream policy

**Files:**
- Modify: `platform/windows/input/include/keyina/windows/input_injection.h`
- Modify: `platform/windows/input/input_injection.cpp`
- Modify: `tests/windows/input_injection_test.cpp`

**Interfaces:**
- `bool ShouldOwnTextStream(bool vietnamese_enabled, bool bypass_typing, bool clipboard_delivery, bool selection_replacement_target) noexcept`
- `std::size_t BuildLiteralUnicodeInputSequence(char32_t character, std::span<INPUT> destination) noexcept`

- [x] Test the only allowed policy combination and every rejecting guard.
- [x] Test BMP and non-BMP Unicode event encoding.
- [x] Test U+0000, surrogate, out-of-range, and insufficient-capacity rejection without destination mutation.
- [x] Implement the minimal allocation-free pure functions.

### Task 2: Own Chromium literal text and releases

**Files:**
- Modify: `platform/windows/input/include/keyina/windows/win32_input_runtime.h`
- Modify: `platform/windows/input/win32_input_runtime.cpp`

**Interfaces:**
- Add fixed `KeyStateSet owned_text_keys_`.
- Consume `ShouldOwnTextStream` and `BuildLiteralUnicodeInputSequence`.

- [x] Clear owned-key state at start, stop, and input-profile replacement.
- [x] Suppress and clear matching owned key-up events before context capture.
- [x] For safe Chromium literal text, inject one marked Unicode decision and suppress the physical key-down.
- [x] Preserve existing transformed delivery and pass through unsupported/shortcut/failure cases.
- [x] Replace full literal `InputDecision` construction with direct marked Unicode event construction.

### Task 3: Preserve mixed Vietnamese and Latin composition

**Files:**
- Modify: `tests/windows/resident_input_controller_test.cpp`

**Interfaces:**
- Regression sentence: `tuyf banj cuws research vaf dduwa ra huowngs toots nhaats `.
- Expected visible text: `tuỳ bạn cứ research và đưa ra hướng tốt nhất `.

- [x] Add the sentence-level controller regression.
- [x] Prove the engine/controller boundary is correct independently of Win32 delivery.

### Task 4: Add the interactive ordering diagnostic

**Files:**
- Modify: `platform/windows/input/include/keyina/windows/win32_input_runtime.h`
- Modify: `platform/windows/input/win32_input_runtime.cpp`
- Modify: `platform/windows/input/native_resident.cpp`

**Interfaces:**
- CLI mode `--chromium-ordering-self-test`.
- Self-test-only constructor flag guarded by a non-zero accepted input marker.

- [x] Host an isolated Keyina-owned off-screen target `EDIT` without adding a thread.
- [x] Verify foreground/focus before every character.
- [x] Run 0, 5, and 10 ms cases and emit exact counters and latency snapshots.
- [x] Keep the foreground-dependent probe out of default CTest.

### Task 5: Verification and commit

**Files:**
- Update this plan and the matching design/results documentation.

- [x] Run full native Debug CTest.
- [x] Run full native Release CTest.
- [x] Run managed Release build/tests.
- [x] Run native resource probes.
- [x] Run one clean interactive Chromium ordering probe and record its output.
- [x] Run `git diff --check`, inspect the final diff, and commit the slice.
