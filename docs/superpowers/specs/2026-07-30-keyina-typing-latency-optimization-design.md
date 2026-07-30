# Keyina Typing Latency and Extreme Optimization Design

## Purpose

Make Keyina's typing path measurable end-to-end, identify the real bottlenecks instead of optimizing by intuition, and provide a safe path toward a native resident runtime that can outperform existing Vietnamese input tools without sacrificing correctness or application compatibility.

This design covers the first independently shippable slice: restore a green baseline, add opt-in per-stage latency telemetry, expose it in Diagnostics, extend reproducible benchmarks, and remove any instrumentation overhead from normal typing when telemetry is disabled.

## Product requirements

- Normal typing remains offline and never records typed text, transcript text, clipboard data, snippets, or document content.
- Diagnostics may record only stage identifiers, duration buckets, counts, process IDs already present in existing content-free traces, and reason codes.
- Latency telemetry is disabled by default and can be enabled or cleared from the Diagnostics page.
- The user can see sample count, median, p95, p99, maximum, and mean latency for each measured stage.
- Measurements cover at least callback total, focus/context checks, native engine processing, and input injection.
- Metrics are clearly marked as local measurements from the current machine and session, not universal claims.
- All existing native and host correctness tests remain green.
- No claim that Keyina is faster than UniKey or EVKey is permitted without a same-machine black-box comparison.

## Architecture

### 1. Resident hot path

The current low-level keyboard hook remains the default backend for this slice. The callback must stay fail-open and return quickly. The managed callback is instrumented only when the profiler is explicitly enabled.

The profiler uses `Stopwatch.GetTimestamp()` and preallocated fixed-size histograms. Recording a sample performs no file I/O, no JSON serialization, no UI work, no string formatting, and no per-sample heap allocation. When disabled, the hook reads one volatile flag and performs no timing calls.

### 2. Stages

The first version measures these stages:

- `CallbackTotal`: full managed callback duration for a key-down event.
- `ForegroundContext`: foreground process change detection and reset decision.
- `SafetyGuard`: shortcut, secure-field, disabled-mode, and boundary checks.
- `EngineProcess`: native bridge call plus edit conversion.
- `InputInjection`: `SendInput` preparation and invocation for transformed edits.

Stages are independent histograms. A key may contribute to only a subset of stages, depending on the path taken.

### 3. Histogram model

Each stage owns a fixed logarithmic histogram with nanosecond buckets, an atomic sample count, an atomic cumulative duration, and an atomic maximum. Percentiles are approximate and resolved from bucket boundaries. This avoids storing individual samples or allocating arrays on the hook path.

Snapshots copy counters outside the hook path and compute readable values for the UI. Clearing metrics replaces or resets counters only from the UI thread while recording uses atomic increments.

### 4. Diagnostics UI

The Diagnostics page gains a dedicated typing-latency card with:

- explicit enable/disable control;
- status text explaining the privacy model;
- refresh and clear actions;
- a compact table showing stage, samples, median, p95, p99, maximum, and mean;
- an empty state explaining that the user should enable profiling and type normally.

The UI must remain usable at 100%, 125%, 150%, and 200% scaling. It must be keyboard accessible and must not update continuously while hidden. Refresh is user initiated in this slice to avoid timer and repaint overhead.

### 5. Benchmarks

The native benchmark remains the source of truth for the clean-room Telex engine. It is extended only after profiler work is green to report more representative cases, including complete words and protected contexts.

The managed benchmark adds isolated cases for:

- profiler disabled fast path;
- profiler enabled sample recording;
- native engine bridge processing;
- edit injection preparation with a fake native sender;
- full hook decision path using deterministic fake dependencies.

Benchmark output remains JSON and includes p50, p95, p99, maximum, allocation per operation, runtime details, and process memory. Budgets are regression gates, not the optimization target.

### 6. Optimization policy

Optimization follows measured evidence:

1. Restore a warning-free Release build.
2. Establish baseline results on the current machine.
3. Add profiler and verify disabled-path overhead.
4. Measure the largest stage.
5. Optimize one bottleneck at a time with a failing regression or benchmark assertion first.
6. Keep an optimization only when repeated Release measurements improve the target percentile without reducing correctness or compatibility.

Likely later work includes moving hook dispatch and edit injection into the native resident process, fixed-capacity engine storage, incremental composition, link-time optimization, representative PGO training, ARM64-native builds, and black-box competitor comparison. These are separate implementation slices and are not silently bundled into this first slice.

## Error handling and safety

- Profiler failures must never block or suppress a physical key.
- Snapshot and formatting code operate outside the hook callback.
- Counter overflow is handled by saturating or resetting at a documented boundary; it must not throw from the hook path.
- `SendInput` failures continue to fail open and reset engine state.
- Secure and unsupported contexts remain literal pass-through.
- No telemetry is uploaded and no automatic report writing occurs.

## Testing

### Unit and integration tests

- Disabled profiler records zero samples.
- Enabled profiler records the correct stage and sample count.
- Snapshot percentile ordering is monotonic: median <= p95 <= p99 <= maximum.
- Clear removes all samples without disabling the profiler.
- Hook paths record only the stages they execute.
- Literal pass-through never invokes injection timing.
- Transform paths record engine, injection, and callback totals.
- Diagnostics controls correctly enable, refresh, and clear metrics.

### Verification gates

- `dotnet build Keyina.slnx -c Release` succeeds with zero warnings and zero errors.
- All host tests pass.
- Native Debug and Release tests pass.
- Native and managed benchmarks complete successfully in Release.
- Profiler-disabled benchmark shows no per-operation allocation and a bounded overhead compared with a direct branch baseline.
- Final diff contains no raw keystroke logging, network telemetry, clipboard use, or unrelated refactoring.

## Success criteria

This slice is complete when the project builds cleanly, the user can view trustworthy per-stage typing latency locally, normal typing has effectively zero profiler cost while disabled, benchmark output is reproducible, and all correctness and safety tests remain green.
