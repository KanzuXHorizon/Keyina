# Native Transform Callback Benchmark Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an isolated Telex composition benchmark that measures controller and direct Unicode injection stages across 256 committed Vietnamese words.

**Architecture:** Reuse the Keyina-owned off-screen edit and opt-in profiler. Send `tieengs ` with raw virtual-key pairs, validate committed `tiếng ` or `TIẾNG ` output in 16-word batches, evaluate counters by warm-up/measured deltas, and expose the probe through CTest.

**Tech Stack:** C++20, Win32 `SendInput`, `WH_KEYBOARD_LL`, CMake, CTest, MSVC Debug/Release.

## Global Constraints

- Never send input unless the owned test edit is foreground and focused.
- Do not modify Caps Lock; adapt expected text to its initial state.
- Use direct Unicode injection, not clipboard compatibility.
- Count only original physical events; Keyina-marked replacement events remain excluded.
- Validate exact committed Unicode output for every measured batch.
- Commit separately after Debug/Release verification.

---

### Task 1: Add a failing CLI dispatch

**Files:**
- Modify: `platform/windows/input/native_resident.cpp`

- [ ] Detect `--transform-callback-latency-self-test` and dispatch to `RunTransformCallbackLatencySelfTest()`.
- [ ] Build Debug and verify the function is missing.

### Task 2: Generalize text verification and implement workload

**Files:**
- Modify: `platform/windows/input/native_resident.cpp`

- [ ] Generalize `WaitForExpectedText` to accept differently sized fixed arrays.
- [ ] Create the owned test window/edit and enable Vietnamese with direct Unicode injection.
- [ ] Warm up 16 words, capture counter baselines, and clear all latency histograms.
- [ ] Send 256 measured words in 16-word batches using raw virtual-key pairs without synthetic Shift.
- [ ] Validate exact batch text and clear the edit after each batch.
- [ ] Require 4,096 callback samples, 2,048 contexts/controller/key-up samples, 512 injection samples, 512 successful injections, zero failures, and a live hook.
- [ ] Emit callback/context/controller/injection percentile and mean fields.

### Task 3: Add CTest and collect evidence

**Files:**
- Modify: `platform/windows/input/CMakeLists.txt`
- Create: `docs/benchmarks/2026-08-01-native-transform-callback-benchmark.md`

- [ ] Add `keyina.windows.input_transform_callback_latency` with a 30-second timeout and pass regex.
- [ ] Run Debug CTest; expect 12/12.
- [ ] Run Release CTest; expect 12/12.
- [ ] Run three Release probes and record stage medians/spread.
- [ ] Compare pass-through and transformation cautiously.

### Task 4: Inspect and commit

- [ ] Run `git diff --check`, inspect exact scope, and confirm no unrelated/generated files.
- [ ] Commit as `perf(input): add native transform latency probe`.
