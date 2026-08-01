# Native Literal Injection Fast Path Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove full `InputDecision` construction and copying from every Chromium-owned literal character.

**Architecture:** Build marked Unicode `INPUT` records directly into a four-record stack buffer, validate scalars and capacity before writes, and send the exact count. Retain existing owned-stream failure and key-up behavior.

**Tech Stack:** C++20, Win32 `INPUT`/`SendInput`, MSVC Release disassembly, CMake/CTest.

## Global Constraints

- Preserve Unicode order, event flags, marker, focus policy, and fail-open behavior.
- No heap allocation or production resource increase.
- Leave unrelated managed benchmark files untouched and unstaged.

---

### Task 1: Replace literal decision tests with direct sequence tests

**Files:**
- Modify: `platform/windows/input/include/keyina/windows/input_injection.h`
- Modify: `tests/windows/input_injection_test.cpp`

- [x] Define `BuildLiteralUnicodeInputSequence(char32_t, std::span<INPUT>)`.
- [x] Test BMP output as two exact Unicode events.
- [x] Test supplementary output as two surrogate pairs/four exact events.
- [x] Test invalid scalars and insufficient capacity return zero without writes.

### Task 2: Implement direct literal sequence construction

**Files:**
- Modify: `platform/windows/input/input_injection.cpp`

- [x] Factor scalar-to-UTF-16 validation without allocation.
- [x] Emit fully initialized marked keyboard records into caller storage.
- [x] Remove `BuildLiteralInputDecision`.
- [x] Run native unit tests.

### Task 3: Use the direct sender in the hook

**Files:**
- Modify: `platform/windows/input/win32_input_runtime.cpp`

- [x] Add a private four-record literal send helper.
- [x] Replace the `InputDecision literal_decision` branch.
- [x] Preserve exact failure reset and fail-open behavior.

### Task 4: Verify and commit

**Files:**
- Update: `docs/superpowers/specs/2026-08-01-chromium-ordering-probe-design.md`
- Update: `docs/superpowers/plans/2026-08-01-chromium-ordering-probe.md`
- Create: `docs/benchmarks/2026-08-01-native-literal-injection-fast-path.md`

- [x] Run native Debug and Release CTest.
- [x] Run managed Release build/tests.
- [x] Run Chromium interactive diagnostic.
- [x] Run resource probes.
- [x] Inspect Release disassembly before/after.
- [x] Run `git diff --check`, inspect staged scope, and commit separately.
