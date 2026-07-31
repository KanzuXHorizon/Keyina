# Keyina Native Callback Latency Profiler Design

## Goal

Add trustworthy, content-free, opt-in latency measurement to the real native keyboard callback without changing the disabled production path's Win32 behavior, allocations, threads, or message flow.

## Architecture

A small `NativeLatencyHistogram` lives in the platform-independent portion of the Windows input library. It stores 64 power-of-two nanosecond buckets plus exact sample count, sum, and maximum in fixed memory. Snapshots report sample count, approximate p50/p95/p99 bucket upper bounds, exact maximum, and integer mean.

`Win32InputRuntime` receives an optional constructor flag that is false for the normal resident. When false, the callback performs one predictable flag check and never calls `QueryPerformanceCounter`. When true, a stack scope records callback-body time for accepted physical events after injected-event filtering. Timing conversion uses one cached `QueryPerformanceFrequency` value initialized at runtime startup.

The live typing self-test enables the profiler and emits its snapshot. No production UI, background timer, file writer, network path, or typed-content buffer is introduced.

## Measurement boundary

The measurement begins after hook-code/message validation and Keyina injection-marker filtering, and ends immediately before returning from `HandleKeyboardEvent`. It includes Keyina key-state handling, hotkey routing, context capture, engine processing, overlay decisions, queueing, and direct injection work executed synchronously in the callback. It excludes Windows delivery into the callback, deferred-message processing after callback return, and target-application rendering.

## Safety and overhead

- Histogram storage is fixed-size and allocation-free.
- No keys, text, HWNDs, process identifiers, clipboard data, or window metadata are recorded.
- Disabled mode performs no clock call and creates no thread or timer.
- Enabled mode is for diagnostics/self-tests; measured values include profiler overhead and are labeled accordingly.
- Percentiles are histogram upper bounds, not exact sample values.
- The profiler must never influence whether an event is suppressed or passed through.

## Acceptance criteria

- Histogram unit tests cover empty, ordered percentile, maximum, mean, overflow-bucket, and clear behavior.
- Debug and Release builds pass.
- Native unit and integration suites pass.
- Typing and clipboard self-tests report 28 callback samples for 28 accepted physical events.
- Percentiles are monotonic: p50 <= p95 <= p99 <= max, except max may be below a rounded bucket upper bound and is therefore reported independently.
- Normal resource probes remain below 10 MiB private memory with zero thread delta.
- The implementation is committed separately from the key-up optimization.
