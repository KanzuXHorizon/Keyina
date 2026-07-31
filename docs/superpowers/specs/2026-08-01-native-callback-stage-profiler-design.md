# Keyina Native Callback Stage Profiler Design

## Goal

Split the existing opt-in native callback latency measurement into fixed, content-free stages so later optimization targets measured work rather than total-callback guesses.

## Stages

The profiler records five independent histograms in addition to callback total:

1. `KeyStateAndHotkey` — physical key bitsets, modifier refresh, character translation, toggle gesture, hotkey routing, and immediate hotkey command/suppression handling.
2. `KeyUpRelease` — controller release-state processing for ordinary key-up events.
3. `TypingContext` — foreground/focus capture, process/application lookup, and secure/password detection.
4. `ControllerProcess` — native controller/engine processing, snippet-overlay decision, and pointer-observation decision.
5. `Injection` — Chromium-target classification, deferred/direct injection, failure recovery, counters, and snippet-command dispatch after a suppressing edit.

`CallNextHookEx`, exception handling, branch glue, and code between stages remain visible as the difference between total callback latency and named stages. Stage sums are not reported as exact per-event totals because histogram snapshots are aggregated independently.

## Architecture

Add `NativeCallbackLatencyStage` beside `NativeLatencySnapshot`. `Win32InputRuntime` owns a fixed `std::array<NativeLatencyHistogram, Count>` and exposes read-only snapshots. `ClearCallbackLatency` clears callback total and every stage.

Existing `NativeCallbackLatencyScope` is reused around bounded lexical blocks. Profiling-disabled behavior remains a null histogram pointer and performs no `QueryPerformanceCounter` call.

The isolated pass-through probe outputs stage snapshots and validates expected sample counts:

- `KeyStateAndHotkey`: 8,192;
- `KeyUpRelease`: 4,096;
- `TypingContext`: 4,096;
- `ControllerProcess`: 4,096;
- `Injection`: 0.

## Safety and semantics

- Stage scopes must not move, duplicate, or reorder input behavior.
- Every existing early return remains semantically identical.
- No stage captures keys, text, HWNDs, process identifiers, window metadata, clipboard data, or command payloads.
- No allocation, lock, thread, timer, file, network, or UI dependency is added to recording.
- Normal resident profiling stays disabled.
- The pass-through benchmark keeps injection at zero; transformation workloads will measure injection separately later.

## Acceptance criteria

- Debug and Release compile and pass all 11 CTest tests.
- Native unit coverage remains 131/131.
- The pass-through probe reports the exact stage sample counts above.
- Three Release runs produce stage p50/p95/p99 and mean values.
- The evidence report identifies the dominant named stage and the residual total-callback cost without claiming causality beyond the measured boundaries.
- The change is committed separately.
