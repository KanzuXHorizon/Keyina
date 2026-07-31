# Keyina Native Callback Benchmark Design

## Goal

Add a repeatable Release probe with thousands of isolated physical keyboard events so native callback p50/p95/p99 values are statistically more useful than the 28-event correctness self-test.

## Scope

The first workload measures the resident pass-through callback path only:

- 256 warm-up `A` key pairs, excluded from measured counters and histogram;
- 4,096 measured synthetic `A` key pairs;
- 8,192 measured low-level-hook events;
- Vietnamese processing disabled;
- one off-screen Keyina-owned Win32 `EDIT` control as the focused target;
- callback profiling enabled;
- no text injection, clipboard replacement, external process, managed host, network, or third-party application.

Transformation, direct Unicode injection, standard-edit replacement, Chromium deferred replacement, and clipboard compatibility remain separate later workloads because they include different target/application costs.

## Architecture

Add `--callback-latency-self-test` to `KeyinaInput.exe`. The probe creates an off-screen test window and standard `EDIT`, focuses it using the existing focus helper, starts `Win32InputRuntime` with Vietnamese disabled and native callback profiling enabled, then sends fixed virtual-key down/up pairs through `SendInput`.

The probe checks focus before every bounded batch and clears the test control between batches to keep text storage bounded. After warm-up it captures counter baselines and clears the latency histogram. It then waits for the exact measured event delta and validates:

- `processed_keyboard_events == 8192`;
- `callback_latency.sample_count == processed_keyboard_events`;
- `typing_context_capture_count == 4096`;
- no suppressed edit or failed injection;
- callback percentiles are non-zero and p50 <= p95 <= p99;
- the native hook remains installed.

Output is one JSON object suitable for local comparison and CTest validation.

## Measurement boundary

The existing profiler starts after hook argument validation and Keyina injection-marker rejection and ends at callback return. The pass-through benchmark therefore includes physical key-state tracking, hotkey routing, key-up fast-path handling, key-down typing-context capture, disabled-controller processing, pointer-registration decision, and `CallNextHookEx` return work performed before the scope ends.

It excludes `SendInput` caller overhead, Windows dispatch into the hook, test-window paint/render time, deferred message-loop work, and managed-host behavior.

## Safety

- The target is created and owned by the probe process.
- The probe aborts if the owned edit loses focus or its window is no longer foreground.
- Input is emitted in bounded batches.
- No unrelated application receives benchmark text by design; focus is verified before each batch.
- The probe restores the previous foreground window when possible.
- The normal resident mode remains unchanged and profiling remains disabled.

## Acceptance criteria

- Debug and Release builds pass.
- Existing 131 native unit tests remain green.
- Debug and Release CTest suites include and pass the new callback-latency test.
- The probe reports exactly 8,192 callback samples and 4,096 typing-context captures.
- All injections/suppressed edits remain zero because Vietnamese processing is disabled.
- Three Release runs complete successfully; the report records the median p50/p95/p99 and run-to-run spread.
- The probe does not establish a universal latency SLA. A gate may be added later only after repeatability is measured across power states, machines, and Windows versions.
- The implementation and evidence are committed as one focused commit.
