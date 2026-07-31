# Native Callback Benchmark Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an isolated, warmed 8,192-event pass-through probe that reports repeatable native callback latency percentiles.

**Architecture:** Extend `KeyinaInput.exe` with a self-test-only command that focuses a Keyina-owned Win32 edit control, sends 4,096 bounded virtual-key pairs with Vietnamese disabled, and reads the existing opt-in callback histogram. Add the probe to CTest without changing normal resident startup or configuration.

**Tech Stack:** C++20, Win32 `SendInput`, `WH_KEYBOARD_LL`, CMake, CTest, MSVC Debug/Release.

## Global Constraints

- Never send benchmark input unless the Keyina-owned edit remains focused and its window remains foreground.
- Use 256 warm-up key pairs, then exactly 4,096 measured key pairs and 8,192 measured callback events.
- Keep Vietnamese disabled so the workload is pass-through and does not invoke Unicode replacement.
- Keep profiling disabled in normal resident mode.
- Do not add a latency pass/fail threshold until repeatability is established.
- Restore the previous foreground window when possible.
- Commit the verified probe separately.

---

### Task 1: Add a failing CLI dispatch

**Files:**
- Modify: `platform/windows/input/native_resident.cpp`

- [ ] Add argument detection for `--callback-latency-self-test` and dispatch to `RunCallbackLatencySelfTest()` before normal mutex startup.
- [ ] Build Debug and verify compilation fails because the function is undefined.

### Task 2: Implement the isolated benchmark

**Files:**
- Modify: `platform/windows/input/native_resident.cpp`

**Interfaces:**
- Produces JSON fields: `result`, `iterations`, `expected_events`, `processed_events`, `typing_context_captures`, `callback_samples`, `callback_p50_ns`, `callback_p95_ns`, `callback_p99_ns`, `callback_maximum_ns`, `callback_mean_ns`, `suppressed_edits`, `failed_injections`, and `hook_running`.

- [ ] Create the off-screen test window and standard edit using the same process-local pattern as `RunTypingSelfTest`.
- [ ] Start a runtime with `vietnamese_enabled=false`, no tray, no profile reload, and callback profiling enabled.
- [ ] Send 256 warm-up `A` key pairs in batches of 64, checking focus and foreground before each batch.
- [ ] Capture counter baselines and clear the callback histogram after warm-up.
- [ ] Send 4,096 measured key pairs in batches of 64 and clear the edit between batches.
- [ ] Wait for an exact measured delta of 8,192 processed events.
- [ ] Validate exactly 4,096 context captures, 8,192 callback samples, zero suppressed edits, zero failed injections, non-zero monotonic percentiles, and a live hook.
- [ ] Emit pass/failure JSON and restore the previous foreground window.

### Task 3: Add CTest and verify repeated Release runs

**Files:**
- Modify: `platform/windows/input/CMakeLists.txt`
- Create: `docs/benchmarks/2026-08-01-native-callback-benchmark.md`

- [ ] Add `keyina.windows.input_callback_latency` calling `KeyinaInput --callback-latency-self-test`, with a 20-second timeout and pass regex for `callback_latency_self_test_pass`.
- [ ] Run Debug build and CTest; expect 11/11 tests.
- [ ] Run Release build and CTest; expect 11/11 tests.
- [ ] Run the Release probe three times and record p50/p95/p99/max/mean and sample invariants.
- [ ] Report median values and spread without claiming a cross-machine universal SLA.

### Task 4: Inspect and commit

- [ ] Run `git diff --check`, `git status --short`, and inspect the exact diff.
- [ ] Confirm only the spec, plan, benchmark report, `native_resident.cpp`, and input `CMakeLists.txt` changed.
- [ ] Commit as `perf(input): add isolated callback latency probe`.
