# Keyina Telex Accuracy and Compatibility Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enable consistent invalid-Latin restoration in the default native backend and lock correct Vietnamese, English, and flexible Telex behavior with regression tests.

**Architecture:** Keep the existing native C++ engine and structural Vietnamese syllable analysis unchanged unless a failing regression proves an engine defect. Publish the existing `RestoreInvalidWord` runtime flag from the managed host, then verify the same profile bytes decode correctly in C++.

**Tech Stack:** C++20, CMake/CTest, .NET 10, custom Keyina test runner, Win32 native resident input runtime.

## Global Constraints

- Do not add dictionary-based spelling correction.
- Do not silently rewrite `nguễn` to `Nguyễn`.
- Do not add work, allocation, I/O, or process launch to the keyboard callback.
- Preserve fail-open injection and bounded composition state.
- Do not commit or modify remote state without explicit authorization.

---

### Task 1: Engine regression matrix

**Files:**
- Modify: `tests/engine_flexible_telex_test.cpp`

**Interfaces:**
- Consumes: `keyina::Engine`, `EngineConfig.restore_invalid_word`, and the existing `TypeSequence` helper.
- Produces: regression coverage for names, literal Latin tokens, wrong-order Telex, and intentional misspellings.

- [ ] **Step 1: Add tests for `nguyeenx`, common delayed orders, `search`, `research`, `powershell`, and literal `ngueenx`.**
- [ ] **Step 2: Build and run `keyina_tests`; confirm whether failures are engine defects or only profile configuration gaps.**
- [ ] **Step 3: Change engine code only if a new test fails for a reproducible engine reason.**
- [ ] **Step 4: Re-run native tests and retain all regressions.**

### Task 2: Publish invalid-Latin restoration

**Files:**
- Modify: `apps/host/Keyina.Host.Core/Configuration/RuntimeInputProfile.cs`
- Modify: `apps/host/Keyina.Host.Tests/RuntimeInputProfileTests.cs`
- Modify: `tests/windows/runtime_profile_test.cpp`

**Interfaces:**
- Consumes: runtime profile flag bit `1 << 4` and the existing 36-byte cross-language profile format.
- Produces: a default profile with `RestoreInvalidWord = true` decoded identically by managed and native code.

- [ ] **Step 1: Update managed and native tests to require the restore flag and the exact checksum-correct vector.**
- [ ] **Step 2: Run focused host/native tests and verify they fail because `ComposeFlags` omits the flag.**
- [ ] **Step 3: Add `RestoreInvalidWordFlag` to the encoded default profile.**
- [ ] **Step 4: Re-run focused tests and verify the profile round trip reports `RestoreInvalidWord = true`.**

### Task 3: Full verification and performance checks

**Files:**
- Inspect only unless a verified regression requires a minimal fix.

**Interfaces:**
- Consumes: Debug and Release build presets, host test runner, native self-tests, and benchmark executables.
- Produces: fresh evidence for correctness, compatibility, CPU/RAM/resource stability, and unchanged callback performance.

- [ ] **Step 1: Run all native Debug tests.**
- [ ] **Step 2: Run all host Debug tests.**
- [ ] **Step 3: Build native and managed Release outputs.**
- [ ] **Step 4: Run native typing/resource self-tests and available benchmark comparison gates.**
- [ ] **Step 5: Inspect `git diff --check`, final diff scope, and repository status.**
