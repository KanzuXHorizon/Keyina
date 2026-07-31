# Native callback pass-through benchmark — 2026-08-01

## Workload

`KeyinaInput.exe --callback-latency-self-test` creates a Keyina-owned off-screen Win32 `EDIT`, makes it the foreground focus target, and runs:

- 256 warm-up key pairs;
- histogram clear and counter baselines;
- 4,096 measured `A` key pairs;
- 8,192 measured low-level-hook callback events.

Vietnamese processing and clipboard compatibility are disabled. The measured path is therefore pass-through only: native physical key-state tracking, hotkey routing, key-up fast path, key-down typing-context capture, disabled controller processing, pointer-registration decision, and callback return.

The probe verifies the owned edit remains focused before every 64-pair batch. It aborts on focus loss, unexpected events, suppression, injection, hook loss, or counter mismatch. No third-party application is an intended target.

## Invariants

Every successful run requires:

```text
processed_events             = 8,192
callback_samples             = 8,192
typing_context_captures      = 4,096
suppressed_edits             = 0
successful_injections        = 0
failed_injections            = 0
hook_running                 = true
```

This independently confirms that context capture occurs only on key-down while both key-down and key-up callbacks are profiled.

## Verification

Debug:

```powershell
cmake --build --preset windows-msvc-debug
ctest --preset windows-msvc-debug --output-on-failure
```

Result: 11/11 CTest tests passed. The new callback-latency integration test passed and native unit coverage remained 131/131.

Release:

```powershell
cmake --preset windows-msvc-release
cmake --build --preset windows-msvc-release
ctest --preset windows-msvc-release --output-on-failure
```

Result: 11/11 CTest tests passed.

## Three Release runs

Environment: Windows, MSVC Release build, Intel64 Family 6 Model 186. Values are histogram upper bounds for percentiles and exact values for maximum and mean.

| Run | p50 | p95 | p99 | Maximum | Mean |
|---:|---:|---:|---:|---:|---:|
| 1 | 262.144 µs | 524.288 µs | 2.097152 ms | 4.3208 ms | 260.516 µs |
| 2 | 262.144 µs | 524.288 µs | 2.097152 ms | 2.5887 ms | 215.454 µs |
| 3 | 262.144 µs | 524.288 µs | 2.097152 ms | 4.6274 ms | 220.319 µs |
| **Median** | **262.144 µs** | **524.288 µs** | **2.097152 ms** | **4.3208 ms** | **220.319 µs** |

Percentile buckets were identical across all three runs. Exact maximum varied by approximately 2.04 ms, showing scheduler/desktop tail noise even after warm-up. Mean varied by approximately 45 µs.

## Interpretation

The probe is repeatable enough to compare broad callback-path changes on this machine, especially p50 and p95. It is not yet suitable as a universal release SLA:

- the histogram intentionally uses coarse power-of-two buckets;
- the probe runs on an interactive Windows desktop;
- Windows scheduling, power state, security software, background input, and hardware affect the tail;
- `CallNextHookEx` and Win32 focus/context calls remain operating-system work outside the engine microbenchmark;
- the workload measures pass-through only and does not represent application-specific Unicode insertion.

No hard latency threshold is added in this slice. Future work should split the callback into content-free stage histograms for hotkey routing, context capture, engine/controller processing, and synchronous injection. An optimization should be retained only when repeated Release measurements improve the relevant stage and total percentiles without reducing compatibility or safety.
