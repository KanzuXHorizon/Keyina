# Native callback key-up fast path — 2026-08-01

> **Current behavior note (2026-08-03):** The Backspace recomposition row below measures the reusable core-engine primitive. Physical Backspace in product input paths resets composition and is handled by the target application.

## Scope

This slice removes focused-window context capture from ordinary physical key-up events after native hotkey routing. It preserves controller release suppression for keys whose key-down was consumed, and it adds a content-free context-capture counter enforced by the live typing self-test.

No typed text, key identity, window title, process name, clipboard content, transcript, or document content is stored by the new counter.

## Root cause

`ResidentInputController::Process` does not inspect `TypingContext` on key-up. Its key-up branch only clears a fixed suppressed-key bit and returns whether the release must be suppressed. The resident nevertheless called `CaptureTypingContext` for both key-down and key-up, causing `GetGUIThreadInfo`, focus checks, and focus-style inspection on events that could not use those results.

The optimized order is now:

1. update physical key/modifier state;
2. route hotkeys and preserve hotkey release commands;
3. for remaining key-up events, process controller release state with an empty context and return;
4. capture typing context only for key-down processing.

## Live invariant

Release self-tests generated 14 characters, each with one key-down and one key-up event:

```json
{"result":"typing_self_test_pass","processed_events":28,"suppressed_edits":4,"successful_injections":4,"failed_injections":0,"typing_context_captures":14,"maximum_expected_context_captures":14}
{"result":"clipboard_typing_self_test_pass","processed_events":28,"suppressed_edits":4,"successful_injections":4,"failed_injections":0,"typing_context_captures":14,"maximum_expected_context_captures":14}
```

The previous control flow captured context for both halves of the physical event pair. The self-test now fails if context captures exceed generated key-down events.

## Verification

### Debug

```powershell
cmake --build --preset windows-msvc-debug
ctest --preset windows-msvc-debug --output-on-failure
```

Result: 10/10 CTest tests passed. Native unit coverage remained green as part of `keyina.unit`.

### Release

```powershell
cmake --preset windows-msvc-release
cmake --build --preset windows-msvc-release
ctest --preset windows-msvc-release --output-on-failure
```

Result: 10/10 CTest tests passed.

### Resident resources

Release, tray disabled:

```json
{"private_working_set_bytes":2551808,"private_memory_bytes":2809856,"thread_count":4,"thread_count_delta":0,"handle_count":143,"cpu_percent":0.039062,"hook_running":true,"budget_pass":true}
```

Release, tray enabled:

```json
{"private_working_set_bytes":2650112,"private_memory_bytes":2936832,"thread_count":4,"thread_count_delta":0,"handle_count":163,"cpu_percent":0.019531,"hook_running":true,"budget_pass":true}
```

Physical input occurred during both five-second global-hook samples, so the snapshots correctly reported input contamination. Resource-budget evaluation still passed because private memory remained below 10 MiB, the hook remained installed, and no resident thread was added.

## Release engine benchmark

Environment: MSVC 19.44, Release, Intel64 Family 6 Model 186, 20,000 warmup iterations and 100,000 measured iterations per run.

The table reports the median p99 from three complete runs:

| Case | Median p99 | Allocation/op | Budget |
|---|---:|---:|---|
| ASCII pass-through | 100 ns | 0 | Pass |
| Letter modifier | 200 ns | 0 | Pass |
| Tone update | 200 ns | 0 | Pass |
| Complete `tieengs` | 1.9 µs | 0 | Pass |
| Complete `Vieetj` | 1.4 µs | 0 | Pass |
| Delayed modifier `truowcs` | 3.7 µs | 0 | Pass |
| Backspace recomposition | 2.8 µs | 0 | Pass |
| Protected URL | 1.8 µs | 0 | Pass |
| Protected email | 2.5 µs | 0 | Pass |
| Valid syllable analysis | 500 ns | 0 | Pass |
| Invalid-boundary restore | 17.5 µs | 0 | Pass |
| Context Guard, 64 code points | 100 ns | 0 | Pass |

All benchmark cases passed configured latency and allocation budgets. Maximum values remained scheduler-sensitive and are not used alone to accept or reject this change.

## Interpretation

This result proves that ordinary key-up events no longer perform typing-context capture in the live native path. It does not claim a universal end-to-end keyboard latency percentile because the existing engine benchmark does not include Windows hook delivery, target message-queue scheduling, target rendering, or application-specific injection behavior.

## Rust decision

The resident remains C++20. Current local probes show approximately 2.5–2.7 MiB private working set, no extra resident thread, and zero ordinary engine allocations. The removed cost was unnecessary Win32 work, not a language-runtime cost. A Rust rewrite is deferred until a repeatable profile identifies a remaining bottleneck or safety boundary that can be migrated independently and compared against C++ for startup, binary size, memory, p99 latency, compatibility, signing, installer behavior, and rollback.
