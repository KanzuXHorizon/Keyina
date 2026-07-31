# Native callback stage profiler — 2026-08-01

## Scope

The opt-in callback profiler now records five content-free fixed-memory stage histograms in addition to callback total:

- key state and hotkey routing;
- ordinary key-up controller release;
- typing-context capture;
- controller/engine plus overlay and pointer decisions;
- suppressing edit injection.

The existing isolated pass-through probe keeps Vietnamese disabled, so the injection stage correctly contains zero samples.

## Sample invariants

Each successful measured run reports:

```text
callback total             8,192 samples
key state and hotkey       8,192 samples
key-up release             4,096 samples
typing context             4,096 samples
controller process         4,096 samples
injection                      0 samples
```

The probe also validates 4,096 context captures, zero suppressed edits, zero successful/failed injections, and a live hook.

Focus can be reclaimed only to the same Keyina-owned test edit before a batch. No input is sent until ownership, focus, and foreground are confirmed. Counters and histograms are evaluated by warm-up/measured deltas so startup events are not mixed into results.

## Verification

Debug:

```powershell
cmake --build --preset windows-msvc-debug
ctest --preset windows-msvc-debug --output-on-failure
```

Result: 11/11 CTest tests passed; native unit coverage remained 131/131.

Release:

```powershell
cmake --build --preset windows-msvc-release
ctest --preset windows-msvc-release --output-on-failure
```

Result: 11/11 CTest tests passed.

## Three Release runs

Percentiles are power-of-two histogram upper bounds. Means are exact aggregate integer means.

### Callback total

| Run | p50 | p95 | p99 | Mean | Maximum |
|---:|---:|---:|---:|---:|---:|
| 1 | 262.144 µs | 524.288 µs | 2.097152 ms | 228.469 µs | 5.3861 ms |
| 2 | 262.144 µs | 524.288 µs | 2.097152 ms | 240.106 µs | 3.6540 ms |
| 3 | 262.144 µs | 524.288 µs | 2.097152 ms | 199.188 µs | 3.4783 ms |
| **Median** | **262.144 µs** | **524.288 µs** | **2.097152 ms** | **228.469 µs** | **3.6540 ms** |

### Median stage results

| Stage | Samples/run | p50 | p95 | p99 | Median mean |
|---|---:|---:|---:|---:|---:|
| Key state + hotkey | 8,192 | 128 ns | 512 ns | 512 ns | 146 ns |
| Key-up release | 4,096 | 256 ns | 512 ns | 1.024 µs | 274 ns |
| Typing context | 4,096 | 16.384 µs | 65.536 µs | 65.536 µs | 18.592 µs |
| Controller process | 4,096 | 512 ns | 1.024 µs | 2.048 µs | 459 ns |
| Injection | 0 | — | — | — | — |

Typing context is the dominant named Keyina stage in pass-through mode. Key state/hotkey routing, controller processing while disabled, and key-up release are already sub-microsecond at their medians.

## Weighted interpretation

To compare per-event means, key-down-only and key-up-only stages are weighted by one half because each represents 4,096 of 8,192 events. Using median means:

```text
key state + hotkey                 0.146 µs/event
key-up release × 0.5               0.137 µs/event
typing context × 0.5               9.296 µs/event
controller process × 0.5           0.230 µs/event
-----------------------------------------------
named-stage weighted total         9.809 µs/event
callback total mean              228.469 µs/event
unattributed difference          218.660 µs/event
```

The difference is not automatically “Keyina overhead.” It includes `CallNextHookEx`, downstream hook-chain work, branch/glue outside named scopes, and the stage profiler's own additional `QueryPerformanceCounter` calls. The measurement supports two conclusions only:

1. micro-optimizing hotkey, key-up, or disabled-controller code cannot materially change total pass-through latency on this setup;
2. typing-context capture is the only named pass-through stage large enough to justify further targeted investigation.

A secure-context optimization must not cache password or focus state in a way that can miss a control changing to protected input. Before changing context semantics, the next probe should split `GetGUIThreadInfo`, application/process cache refresh, and password/style detection into sub-stages, then retain only changes that improve repeated Release measurements without weakening fail-open behavior.
