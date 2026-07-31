# Native Telex transform callback benchmark — 2026-08-01

## Workload

`KeyinaInput.exe --transform-callback-latency-self-test` measures the real native Telex and direct Unicode replacement path against a Keyina-owned off-screen Win32 `EDIT`.

Configuration and workload:

- Vietnamese Telex enabled;
- clipboard compatibility disabled;
- raw physical sequence `tieengs `;
- expected committed output `tiếng `, or `TIẾNG ` when Caps Lock is active;
- 16 warm-up words;
- 256 measured words in 16-word batches;
- 4,096 original physical callback events;
- 512 expected suppressing edits and successful Unicode injections.

Each measured batch validates exact Unicode output before the edit is cleared. The target is confirmed before every synthetic character. A private self-test input marker ensures unrelated global keyboard events pass through untouched and are not processed, profiled, or allowed to alter the benchmark engine state. Keyina's own replacement events retain the production injection marker and are excluded before callback counters/profilers.

## Required invariants

Every successful run requires:

```text
processed original events       4,096
callback samples                4,096
typing-context captures         2,048
key-up samples                   2,048
controller samples               2,048
suppressing edits                  512
injection-stage samples            512
successful injections              512
failed injections                    0
exact committed text             pass
hook running                      true
```

Warm-up independently requires 256 original events, 128 contexts, 32 suppressions, and 32 successful injections.

## Verification

Debug:

```powershell
cmake --build --preset windows-msvc-debug
ctest --preset windows-msvc-debug --output-on-failure
```

Result after adding the test: 12/12 CTest tests passed; native unit coverage remained 131/131.

Release:

```powershell
cmake --build --preset windows-msvc-release
ctest --preset windows-msvc-release --output-on-failure
```

Result: 12/12 CTest tests passed after self-test input isolation was enabled.

## Three Release runs

Percentiles are histogram upper bounds; means and maximums are exact aggregate values.

### Callback total

| Run | p50 | p95 | p99 | Mean | Maximum |
|---:|---:|---:|---:|---:|---:|
| 1 | 262.144 µs | 2.097152 ms | 4.194304 ms | 284.674 µs | 4.8408 ms |
| 2 | 131.072 µs | 2.097152 ms | 4.194304 ms | 255.726 µs | 4.2313 ms |
| 3 | 262.144 µs | 2.097152 ms | 4.194304 ms | 302.359 µs | 5.8569 ms |
| **Median** | **262.144 µs** | **2.097152 ms** | **4.194304 ms** | **284.674 µs** | **4.8408 ms** |

### Key stages

| Stage | Samples/run | Median p50 | Median p95 | Median p99 | Median mean | Median maximum |
|---|---:|---:|---:|---:|---:|---:|
| Typing context | 2,048 | 16.384 µs | 16.384 µs | 32.768 µs | 7.212 µs | not separately reported |
| Controller process | 2,048 | 4.096 µs | 8.192 µs | 32.768 µs | 2.865 µs | not separately reported |
| Direct Unicode injection | 512 | 2.097152 ms | 4.194304 ms | 4.194304 ms | 1.333907 ms | 4.8315 ms |

## Weighted contribution

Injection occurs on 512 of 4,096 original events, or one eighth of callbacks. Using median stage means:

```text
typing context:     7.212 µs × 0.5   =   3.606 µs/event
controller process: 2.865 µs × 0.5   =   1.433 µs/event
injection:          1.333907 ms × 0.125 = 166.738 µs/event
callback total mean                         284.674 µs/event
```

Direct Unicode injection is therefore the dominant named cost in real Telex composition on this setup. It also explains why transform callback p95/p99 are substantially higher than pass-through, while context and controller remain tens of microseconds or below.

## Interpretation and next optimization boundary

The injection stage contains synchronous `SendInput` delivery for backspaces and Unicode units, including re-entry through the global hook chain for Keyina-marked events. Simply rewriting C++ code in Rust would not remove this Win32 delivery cost.

The next optimization must preserve visible-order correctness. Moving replacement blindly to another thread/message can allow a later pass-through physical character to reach the target before the queued replacement. Viable work therefore starts with reducing the replacement sequence itself:

1. measure current backspace and UTF-16 event counts per edit;
2. verify whether decisions rewrite more of the active composition than necessary;
3. derive and test a longest-common-prefix/suffix-minimal edit only when it preserves exact rollback and surrogate semantics;
4. re-run the transform callback probe and retain the change only if injection p50/p95/mean improve without compatibility regressions.

No universal latency SLA or “top 0.1%” claim is made from one interactive Windows machine. The current probes create a reproducible evidence base for comparing future changes.
