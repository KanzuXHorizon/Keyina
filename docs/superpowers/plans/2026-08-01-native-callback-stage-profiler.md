# Native Callback Stage Profiler Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add five fixed-memory native callback stage histograms and report them from the isolated pass-through probe.

**Architecture:** Extend the existing opt-in histogram storage in `Win32InputRuntime`; reuse the existing RAII clock scope around behavior-preserving lexical blocks; expose snapshots only to self-tests/diagnostics. The normal resident keeps all histogram pointers null and performs no clock calls.

**Tech Stack:** C++20, Win32 `QueryPerformanceCounter`, CMake, CTest, MSVC Debug/Release.

## Global Constraints

- Preserve exact hook behavior, early returns, suppression, and fail-open semantics.
- Add no content capture, allocation, lock, thread, timer, file, network, or managed dependency.
- Keep profiling disabled by default.
- Expect stage samples of 8192/4096/4096/4096/0 for pass-through.
- Commit separately after full Debug and Release verification.

---

### Task 1: Add failing stage expectations to the benchmark

**Files:**
- Modify: `platform/windows/input/native_resident.cpp`

- [ ] Read snapshots for `KeyStateAndHotkey`, `KeyUpRelease`, `TypingContext`, `ControllerProcess`, and `Injection`.
- [ ] Add exact sample-count assertions and stage p50/p95/p99/mean fields to benchmark JSON.
- [ ] Build Debug and verify compilation fails because stage APIs do not exist.

### Task 2: Add stage storage and snapshot APIs

**Files:**
- Modify: `platform/windows/input/include/keyina/windows/native_latency_histogram.h`
- Modify: `platform/windows/input/include/keyina/windows/win32_input_runtime.h`

- [ ] Define `enum class NativeCallbackLatencyStage : std::uint8_t` with five stages and `Count`.
- [ ] Add a fixed histogram array to `Win32InputRuntime`.
- [ ] Add `callback_stage_latency_snapshot(stage)` with bounds-safe empty fallback.
- [ ] Extend `ClearCallbackLatency` and startup reset to clear every stage.

### Task 3: Instrument behavior-preserving lexical blocks

**Files:**
- Modify: `platform/windows/input/win32_input_runtime.cpp`

- [ ] Wrap key state/event construction/hotkey handling in `KeyStateAndHotkey`.
- [ ] Wrap ordinary controller key-up release processing in `KeyUpRelease`.
- [ ] Wrap `CaptureTypingContext` in `TypingContext`.
- [ ] Wrap controller processing, overlay decision, and pointer decision in `ControllerProcess`.
- [ ] Wrap suppressing target classification/injection/failure recovery/snippet command in `Injection`.
- [ ] Do not move any branch or change any return value.

### Task 4: Verify, report, and commit

**Files:**
- Create: `docs/benchmarks/2026-08-01-native-callback-stage-profiler.md`

- [ ] Run Debug build and 11/11 CTest tests.
- [ ] Run Release build and 11/11 CTest tests.
- [ ] Run the Release callback probe three times.
- [ ] Record stage percentile medians and identify dominant stage/residual honestly.
- [ ] Run `git diff --check`, inspect scope, and commit as `perf(input): profile native callback stages`.
