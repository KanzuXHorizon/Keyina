# Boundary Recovery and Tone Validation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Preserve editable Telex context when the user backs across a recent word boundary and prevent tone application to invalid Vietnamese vowel nuclei.

**Architecture:** Extend `ResidentInputController` with a bounded post-boundary recovery state that survives a small literal suffix and restores the prior engine composition only after that suffix and the delimiter are deleted. Strengthen the engine tone path by validating the candidate syllable/nucleus before applying a pending tone, falling back to literal input when the structure is not a valid Vietnamese candidate.

**Tech Stack:** C++20, existing Keyina engine/syllable analyzer, Win32 resident controller, CMake/CTest.

## Global Constraints

- Avoid word-specific dictionaries and one-off string hardcoding.
- Preserve existing Telex escape, valid Vietnamese composition, snippets, secure-context reset, and zero-allocation hot-path behavior.
- Keep recovery bounded and reset on pointer/focus/shortcut/unsupported navigation boundaries.
- Add regression tests before production changes and run focused plus full unit verification.

---

### Task 1: Invalid nucleus tone guard

**Files:**
- Modify: `tests/engine_tone_test.cpp`
- Modify: `core/src/engine.cpp`

**Interfaces:**
- Consumes: `AnalyzeVietnameseSyllable(std::u32string_view)` and existing pending-tone flow.
- Produces: tone keys remain literal when applying them would create an invalid vowel nucleus.

- [ ] Add failing tests for `laiuj` remaining literal while `laij` still becomes `lại`.
- [ ] Run `cmake --build --preset windows-msvc-debug --target keyina_tests && ctest --preset windows-msvc-debug -R keyina.unit` and confirm the new invalid-nucleus case fails.
- [ ] Add the smallest structural validation around pending tone application.
- [ ] Re-run the focused unit test and confirm all engine tests pass.

### Task 2: Bounded boundary recovery

**Files:**
- Modify: `platform/windows/input/include/keyina/windows/resident_input_controller.h`
- Modify: `platform/windows/input/resident_input_controller.cpp`
- Modify: `tests/windows/resident_input_controller_test.cpp`

**Interfaces:**
- Consumes: committed raw/visible composition already captured at a delimiter.
- Produces: a bounded recovery state that tracks literal characters typed after the delimiter and restores the prior composition only after those characters and the delimiter are physically deleted.

- [ ] Add failing controller tests for `sai<space>x<backspace><backspace>f -> sài`, multiple suffix characters, and cancellation on pointer/focus boundaries.
- [ ] Run the resident controller test target and confirm the new recovery tests fail for the intended state-loss reason.
- [ ] Implement a small counter/state machine without reading surrounding application text or allocating per key.
- [ ] Re-run controller and unit tests and confirm compatibility.

### Task 3: Rule-family audit and verification

**Files:**
- Modify only tests/data files if a missing generic regression vector is identified.

**Interfaces:**
- Consumes: existing repeated-key, restore-invalid-word, boundary, tone, corpus, and invariant suites.
- Produces: evidence that the fixes do not regress adjacent Telex rules.

- [ ] Run full Debug build and `ctest --preset windows-msvc-debug`.
- [ ] Run the typing corpus/invariant tests included in the unit executable.
- [ ] Inspect `git diff --check` and the final scoped diff.
- [ ] Report exact commands, pass/fail counts, and any remaining platform-level risk.
