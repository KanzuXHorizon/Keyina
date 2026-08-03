# Keyina Reliability Hardening Implementation Plan

> **Current behavior note (2026-08-03):** The Backspace reconstruction task below is historical. Current input backends reset composition and leave physical Backspace to the target application, preventing tone-key rollback.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the native resident typing path correct and stable for sustained Vietnamese and literal-Latin typing, with fresh Debug/Release, resource, latency, and real Windows input evidence.

**Architecture:** Keep the native C++ engine as the source of truth for Telex composition. The managed host publishes one checksum-protected 36-byte runtime profile; the native resident decodes it and applies settings without restart. Diagnose live input at physical-event, engine-edit, injection, focus/reset, and target-text boundaries before changing timing or retry behavior.

**Tech Stack:** C++20, CMake/CTest, Win32 `WH_KEYBOARD_LL` and `SendInput`, .NET 10 WinForms, custom Keyina test runner, Python benchmark validators.

## Global Constraints

- Do not add dictionary-based spelling correction or silently rewrite intentional misspellings.
- Do not add network, file, clipboard, telemetry, allocation-heavy, UI, or process-launch work to the keyboard callback.
- Preserve fail-open behavior for secure, elevated, unsupported, excluded, and uncertain targets.
- Preserve the runtime profile size, format version, and checksum algorithm.
- Preserve unrelated uncommitted changes and do not commit or modify remote state without explicit authorization.
- Do not widen retries, timeouts, memory budgets, or latency thresholds without measured root-cause evidence.

---

### Task 1: Snapshot and fresh baseline

**Files:**
- Inspect: all current modified files reported by `git status --short`
- Inspect: `docs/superpowers/specs/2026-07-31-keyina-reliability-hardening-design.md`

**Interfaces:**
- Consumes: current checkout and existing build presets.
- Produces: a fresh failure list from binaries rebuilt from the current source tree.

- [ ] Run `git status --short`, `git diff --stat`, and `git diff --check`; preserve all pre-existing changes.
- [ ] Run `cmake --preset windows-msvc-debug -DKEYINA_BUILD_TSF=ON` and `cmake --build --preset windows-msvc-debug`.
- [ ] Run `dotnet build Keyina.slnx -c Debug` and require zero warnings and zero errors.
- [ ] Run `ctest --preset windows-msvc-debug --output-on-failure` and record every failing gate.
- [ ] Run the host test runner from the fresh Debug build; treat exclusive-desktop mutex contention separately from product failures.

### Task 2: Telex restoration and Backspace correctness

**Files:**
- Modify only when required: `core/src/engine.cpp`
- Modify only when required: `core/include/keyina/engine.h`
- Test: `tests/engine_flexible_telex_test.cpp`
- Test: `tests/engine_history_test.cpp`
- Test: `apps/host/Keyina.Host.Tests/VietnameseKeyboardHookTests.cs`

**Interfaces:**
- Consumes: `keyina::Engine`, `EngineConfig.restore_invalid_word`, `Engine::Process`, `Engine::RawKeys`.
- Produces: structural restoration that keeps valid editable Telex while restoring proven Latin tokens.

- [ ] Verify native tests cover `nguyeenx -> nguyễn`, `nguyeexn -> nguyễn`, `ngueenx -> nguễn`, and editable `nguyenx -> nguyẽn`.
- [ ] Verify `backspace_then_retyping_modifier_reuses_the_rebuilt_composition` removes `x`, restores raw `nguyen`, then accepts `e` to produce `nguyên`.
- [ ] Verify literal cases include `search`, `research`, `powershell`, `browser`, `source`, `windows`, identifiers, paths, URLs, repeated-key escape, and boundary commit behavior.
- [ ] Run the rebuilt native unit executable and the managed Backspace hook regression; if a case fails, narrow only the restoration predicate and rerun the same test before the full native suite.
- [ ] Inspect reset, Backspace, maximum-token rollover, and repeated escape paths to ensure any restoration state is recomputed or cleared deterministically.

### Task 3: Runtime profile parity

**Files:**
- Modify only when required: `apps/host/Keyina.Host.Core/Configuration/RuntimeInputProfile.cs`
- Test: `apps/host/Keyina.Host.Tests/RuntimeInputProfileTests.cs`
- Modify only when required: `platform/windows/input/runtime_profile.cpp`
- Modify only when required: `platform/windows/input/include/keyina/windows/runtime_profile.h`
- Test: `tests/windows/runtime_profile_test.cpp`
- Test: `platform/windows/input/native_resident.cpp`

**Interfaces:**
- Consumes: flag bit `1 << 4`, 36-byte profile, FNV-1a checksum.
- Produces: default and decoded profiles with `restore_invalid_word = true` in managed and native code.

- [ ] Require the exact vector `4B4952500224110602030001052000055600055400055A00001B000001000000B6CD5DCA` in managed and native tests.
- [ ] Keep `ComposeFlags` with one canonical `flags |= RestoreInvalidWordFlag` operation.
- [ ] Verify `DefaultRuntimeInputProfile()` sets `restore_invalid_word = true` and native decode recognizes the same bit.
- [ ] Run managed vector/round-trip/corruption tests and native runtime-profile tests.
- [ ] Run `KeyinaInput.exe --profile-reload-self-test` and require the flag to survive an atomic enable/disable reload.

### Task 4: Live hook and resource stability

**Files:**
- Modify only from reproduced evidence: `apps/host/Keyina.Host.Tests/LiveKeyboardHookIntegrationTests.cs`
- Modify only from reproduced evidence: `apps/host/Keyina.Host.Windows/Typing/VietnameseKeyboardHook.cs`
- Modify only from reproduced evidence: `platform/windows/input/native_resident.cpp`
- Modify only from reproduced evidence: `platform/windows/input/win32_input_runtime.cpp`

**Interfaces:**
- Consumes: physical key event counts, target HWND/focus, `TypingTraceBuffer`, native resource snapshots.
- Produces: repeated burst typing with no dropped/duplicated keys and resource measurements that exclude asynchronous process-startup noise without hiding resident-created threads.

- [ ] Run the live focused-textbox test repeatedly with exclusive desktop-input ownership and preserve mismatch diagnostics.
- [ ] Distinguish mutex contention from text divergence, focus reset, pointer reset, partial `SendInput`, and engine-edit failure.
- [ ] Keep retries at three and native typing attempts at five; do not increase them to mask a failure.
- [ ] Make completion condition-based on observed processed events and expected target text; retain fixed sleeps only when they are not used as proof of delivery.
- [ ] Run tray and non-tray resource probes repeatedly in fresh processes. Establish the baseline only after process thread count is stable and the process has completed cold startup; retain the 10 MiB private-memory and zero resident thread-growth gates.

### Task 5: Full Debug and Release gates

**Files:**
- Inspect only unless a focused gate proves a defect.

**Interfaces:**
- Consumes: project CI commands and existing benchmark budgets.
- Produces: fresh correctness, resource, sanitizer, and performance evidence.

- [ ] Run all native Debug CTest and all managed Debug tests.
- [ ] Run `python tools/check_vectors.py` and `python tools/test_compare_benchmark.py`.
- [ ] Configure, build, and run native Release CTest with optional TSF coverage.
- [ ] Build the complete .NET solution in Release and run all Release host tests.
- [ ] Run native self-test, typing, resource, tray-resource, and profile-reload probes.
- [ ] Run host self-test, offline speech self-test, hotkey self-test, and resource self-test.
- [ ] Run native and managed Release benchmarks and require the documented allocation and absolute latency budgets.
- [ ] Run the Linux Clang ASan/UBSan preset when the local toolchain is available; otherwise record the unavailable toolchain explicitly.

### Task 6: Published bundle and compatibility evidence

**Files:**
- Create: `docs/compatibility/2026-07-31-reliability-hardening-results.md`
- Inspect: `scripts/windows/publish.ps1`

**Interfaces:**
- Consumes: published `KeyinaInput.exe`, `Keyina.Host.exe`, `KeyinaEngine.dll`, runtime profile, and compatibility matrix from the design.
- Produces: reproducible pass/fail evidence and remaining issue list.

- [ ] Publish the Windows bundle and run all self-tests from the published directory.
- [ ] Exercise a real Win32 text control through native and managed live-hook integration tests.
- [ ] Verify literal `powershell`, paths, switches, identifiers, URLs, paste, selection replacement, Backspace, focus switching, password bypass, exclusion, and pointer reset.
- [ ] Verify Chromium, VS Code, terminal, and Office-style targets when desktop automation and installed applications are available; mark untestable targets as not executed rather than passing them by assumption.
- [ ] Record expected text, actual text, bypass/reset behavior, visible lag, CPU, private memory, dropped/duplicated keys, exact commands, and remaining risks.
- [ ] Run `git diff --check`, inspect the final scoped diff for secrets/artifacts/unrelated edits, and report only checks actually executed.
