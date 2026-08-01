# Native Injection Buffer Fast Path Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove 15.5 KiB stack zeroing and stack probing from ordinary native input injection while preserving exact event construction and fallback capacity.

**Architecture:** Compute required input-event count, use a 16-record uninitialized stack buffer for ordinary decisions, and retain uninitialized maximum buffers only when the decision exceeds the fast tier. Centralize `SendInput` validation in private helpers.

**Tech Stack:** C++20, Win32 `INPUT`/`SendInput`, MSVC Release disassembly, CMake/CTest.

## Global Constraints

- Preserve exact event sequence, flags, markers, Unicode encoding, and partial-send failure behavior.
- No heap allocation or production resource increase.
- Keep long composition/snippet support through existing maximum capacities.
- Do not stage or modify unrelated managed benchmark work in the shared checkout.

---

### Task 1: Strengthen sequence initialization tests

**Files:**
- Modify: `tests/windows/input_injection_test.cpp`

- [x] Prefill destination buffers with `INPUT_MOUSE` and non-zero bytes.
- [x] Build keyboard and selection-replacement sequences.
- [x] Assert every emitted record is fully initialized as marked keyboard input.
- [x] Run native unit tests and confirm the new coverage passes against existing builders.

### Task 2: Add tiered native send helpers

**Files:**
- Modify: `platform/windows/input/win32_input_runtime.cpp`

- [x] Add pure required-count helpers for keyboard and selection replacement.
- [x] Add a 16-event fast buffer and maximum-capacity fallback buffer.
- [x] Default-initialize arrays without `{}`; rely only on builder-written used slots.
- [x] Replace duplicated send lambdas in `Inject` and `InjectWithSelectionReplacement`.
- [x] Preserve empty-decision and extended-insert chunk semantics.

### Task 3: Verify behavior and generated code

**Files:**
- Create: `docs/benchmarks/2026-08-01-native-injection-buffer-fast-path.md`

- [x] Run full native Debug CTest.
- [x] Run full native Release CTest.
- [x] Run managed Release build/tests.
- [x] Run Chromium interactive ordering diagnostic.
- [x] Run resident resource probes.
- [x] Inspect MSVC Release disassembly and record stack/memset evidence before and after.
- [x] Run `git diff --check`, inspect staged scope, and commit separately.
