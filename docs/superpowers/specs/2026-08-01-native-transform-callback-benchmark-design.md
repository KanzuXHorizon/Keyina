# Keyina Native Transform Callback Benchmark Design

## Goal

Measure the real native Vietnamese composition path, including synchronous Unicode replacement, with a repeatable isolated workload and stage histograms.

## Workload

Add `KeyinaInput.exe --transform-callback-latency-self-test` with:

- a Keyina-owned off-screen Win32 `EDIT` target;
- Vietnamese Telex enabled;
- direct Unicode injection enabled and clipboard compatibility disabled;
- 16 warm-up words;
- 256 measured words;
- raw physical sequence `tieengs ` per word;
- 2,048 measured key-down events and 2,048 measured key-up events;
- 4,096 measured callback-total samples;
- expected visible output `tiếng `, or `TIẾNG ` when Caps Lock is active;
- expected two suppressing replacements per word: letter-shape composition and tone update.

Words are sent in batches of 16. Each batch must produce exactly 16 expected committed words before the edit is cleared. Focus is revalidated or safely reacquired only to the owned edit before input is emitted.

## Measurement

The existing callback and stage profilers record only original physical events. Unicode replacement events carry Keyina's private injection marker and are rejected before profiler/counter entry, preventing feedback and double-counting.

Expected measured samples:

```text
callback total             4,096
key state + hotkey         4,096
key-up release             2,048
typing context             2,048
controller process         2,048
injection                    512
successful injections        512
failed injections               0
```

The injection stage includes target classification, sequence construction, synchronous `SendInput`, counters, failure recovery, and snippet command dispatch after a suppressing edit.

## Safety and correctness

- Input is never sent without confirming the Keyina-owned edit is focused and foreground.
- Caps Lock is not modified; expected output follows its initial state.
- Each batch validates exact committed Unicode output before clearing.
- Any focus failure, event-count mismatch, text mismatch, injection failure, unexpected suppression count, or hook loss fails the probe.
- The normal resident remains unchanged and profiling-disabled.
- No typed benchmark content leaves the local process target.

## Acceptance criteria

- Debug and Release pass all CTest tests, with the new test increasing the total to 12.
- Native unit coverage remains 131/131.
- Three Release runs satisfy all sample/count/text invariants.
- The report records total, context, controller, and injection p50/p95/p99/mean values.
- Results are compared with pass-through without pretending the workloads are identical.
- The implementation is committed separately.
