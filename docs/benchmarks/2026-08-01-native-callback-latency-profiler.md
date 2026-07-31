# Native callback latency profiler — 2026-08-01

## Scope

This slice adds an opt-in, fixed-memory histogram around the accepted physical-event body of `Win32InputRuntime::HandleKeyboardEvent`.

The measurement starts after hook-code/message validation and Keyina injection-marker filtering. It includes native key-state updates, hotkey routing, key-up release handling, typing-context capture, engine processing, overlay decisions, and synchronous queue/injection work. It excludes Windows delivery into the hook, deferred message-loop work after callback return, and target-application rendering.

## Privacy and overhead model

The histogram records only elapsed nanoseconds. It does not store keys, text, HWNDs, process identifiers, window titles, clipboard content, transcripts, or document content.

Storage is fixed at 64 counters plus aggregate fields. Percentiles are power-of-two bucket upper bounds. Maximum and mean are calculated from exact recorded durations, subject only to saturating integer protection.

The normal resident keeps profiling disabled. In disabled mode no high-resolution clock call occurs, no thread or timer is created, and no allocation is performed. The callback constructs a small scope whose null histogram pointer makes both entry and exit immediately return.

## Test-driven histogram coverage

Four new native tests cover:

- empty snapshots;
- p50/p95/p99 bucket selection for a known ordered sample set;
- exact maximum and integer mean;
- state clearing;
- saturation into the final bucket for `UINT64_MAX`.

Native unit count increased from 127 to 131 tests.

## Live self-test evidence

Both live modes generated 14 physical characters, producing 28 accepted keyboard events and 28 callback samples. Context capture remained limited to the 14 key-down events.

Release direct Unicode mode:

```json
{"result":"typing_self_test_pass","processed_events":28,"typing_context_captures":14,"callback_samples":28,"callback_p50_ns":524288,"callback_p95_ns":4194304,"callback_p99_ns":8388608,"callback_maximum_ns":5182500,"callback_mean_ns":876353}
```

Release clipboard-compatibility mode:

```json
{"result":"clipboard_typing_self_test_pass","processed_events":28,"typing_context_captures":14,"callback_samples":28,"callback_p50_ns":524288,"callback_p95_ns":2097152,"callback_p99_ns":2097152,"callback_maximum_ns":1514300,"callback_mean_ns":452625}
```

These 28-sample snapshots prove integration and measurement coverage, not a statistically stable universal callback benchmark. With 28 samples, p99 effectively reflects the slowest sample and bucket rounding can exceed the exact maximum. A later dedicated isolated probe should generate thousands of harmless events for repeatable tail-latency comparison.

## Verification

### Debug

```powershell
cmake --build --preset windows-msvc-debug
ctest --preset windows-msvc-debug --output-on-failure
```

Result: 10/10 CTest tests passed; `keyina.unit` reported 131/131 tests passed.

### Release

```powershell
cmake --preset windows-msvc-release
cmake --build --preset windows-msvc-release
ctest --preset windows-msvc-release --output-on-failure
```

Result: 10/10 CTest tests passed; Release `keyina.unit` passed.

## Disabled-mode resident resources

Release, tray disabled:

```json
{"private_working_set_bytes":2572288,"private_memory_bytes":2822144,"thread_count":4,"thread_count_delta":0,"handle_count":143,"cpu_percent":0.000000,"processed_keyboard_events":0,"hook_running":true,"budget_pass":true}
```

Release, tray enabled:

```json
{"private_working_set_bytes":2670592,"private_memory_bytes":3035136,"thread_count":4,"thread_count_delta":0,"handle_count":163,"cpu_percent":0.019531,"processed_keyboard_events":32,"hook_running":true,"budget_pass":true}
```

The tray-enabled sample was contaminated by physical desktop input, which the probe reports explicitly. Both samples remained below the existing 10 MiB private-memory gate and added no resident thread.

## Interpretation

The profiler provides the missing low-level evidence surface needed for subsequent optimization. It intentionally does not impose a callback latency gate yet because the current live sample count is too small and includes application-specific synchronous injection behavior. The next benchmark slice should add an isolated multi-thousand-event workload, separate pass-through, engine-transform, direct-injection, deferred-injection, and standard-edit replacement cases, and compare median p95/p99 across repeated Release runs.
