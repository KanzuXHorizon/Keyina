# Native Callback Latency Profiler Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Measure the real native callback body with a fixed-memory opt-in histogram while keeping the normal resident free of clock calls and allocations.

**Architecture:** Add a reusable 64-bucket nanosecond histogram to `keyina_windows_input`, integrate it into `Win32InputRuntime` behind a constructor flag, and enable it only in live typing self-tests. Use a stack scope so every callback return records consistently without changing suppression decisions.

**Tech Stack:** C++20, Win32 `QueryPerformanceCounter`, CMake, CTest, MSVC Debug/Release.

## Global Constraints

- Record no input content or target metadata.
- Allocate no memory while recording.
- Call no clock when profiling is disabled.
- Add no thread, timer, file, UI, network, or managed dependency.
- Do not use profiler results to change input behavior.
- Report histogram percentile upper bounds honestly.
- Commit this slice separately.

---

### Task 1: Define histogram behavior with failing tests

**Files:**
- Create: `tests/windows/native_latency_histogram_test.cpp`
- Modify: `tests/CMakeLists.txt`

**Interfaces:**
- Requires: `keyina::windows::NativeLatencyHistogram`.
- Requires: `NativeLatencySnapshot` fields `sample_count`, `p50_ns`, `p95_ns`, `p99_ns`, `maximum_ns`, and `mean_ns`.

- [ ] Add tests for empty snapshot, records `{1,2,3,4,5,8,9,16,1000}`, clear, and a value in the final overflow bucket.
- [ ] Build Debug and verify compilation fails because the histogram header is missing.

### Task 2: Implement the fixed histogram

**Files:**
- Create: `platform/windows/input/include/keyina/windows/native_latency_histogram.h`
- Create: `platform/windows/input/native_latency_histogram.cpp`
- Modify: `platform/windows/input/CMakeLists.txt`

**Interfaces:**
- Produces: `void RecordNanoseconds(std::uint64_t) noexcept`.
- Produces: `NativeLatencySnapshot Snapshot() const noexcept`.
- Produces: `void Clear() noexcept`.

- [ ] Implement 64 power-of-two buckets with fixed `std::array<std::uint64_t, 64>` storage.
- [ ] Saturate sum/sample counters instead of overflowing.
- [ ] Return zeroes for an empty snapshot and rounded bucket upper bounds for percentiles.
- [ ] Build and run `keyina.unit`; verify all histogram tests pass.

### Task 3: Integrate opt-in callback timing

**Files:**
- Modify: `platform/windows/input/include/keyina/windows/win32_input_runtime.h`
- Modify: `platform/windows/input/win32_input_runtime.cpp`
- Modify: `platform/windows/input/native_resident.cpp`

**Interfaces:**
- Extends constructor with `bool profile_callback_latency = false`.
- Produces: `NativeLatencySnapshot callback_latency_snapshot() const noexcept`.

- [ ] Add a stack recorder that calls `QueryPerformanceCounter` only when profiling is enabled and frequency initialization succeeded.
- [ ] Start timing after event validation and injection-marker filtering.
- [ ] Enable profiling only in `RunTypingSelfTest`.
- [ ] Emit sample count, p50, p95, p99, maximum, and mean in success/failure JSON.
- [ ] Require callback sample count to equal processed physical event count in the live self-test.

### Task 4: Verify resources and commit

**Files:**
- Create: `docs/benchmarks/2026-08-01-native-callback-latency-profiler.md`

- [ ] Run Debug build and all Debug CTest tests.
- [ ] Run Release build and all Release CTest tests.
- [ ] Run Release typing, clipboard typing, resource, and tray-resource probes.
- [ ] Record observed callback histogram values and clarify the measurement boundary.
- [ ] Run `git diff --check`, inspect scope, and commit as `perf(input): add opt-in native callback profiler`.
