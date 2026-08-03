# Typing latency optimization report — 2026-07-30

> **Current behavior note (2026-08-03):** Backspace reconstruction measurements in this historical report cover the reusable core-engine primitive. Shipped native, managed, and optional TSF input paths now reset composition and pass physical Backspace through so it deletes one visible character rather than a Telex modifier.

## Scope

This report records the local evidence for the first Keyina extreme-optimization slice:

- opt-in latency measurement for each resident typing stage;
- zero-allocation managed hot paths after warm-up;
- reusable native engine composition buffers;
- a dedicated keyboard-hook message thread independent from the UI thread;
- Raw Input click/wheel observation on the same message loop, registered only while a composition exists;
- no low-level mouse hook and no pointer-observer thread;
- resident startup, CPU, RAM, thread, and handle measurement with a strict 10 MiB private-memory gate;
- allocation-aware native benchmarks and regression budgets;
- correctness, integration, Debug, and Release verification.

These results were measured on one development machine. They are useful for regression decisions on the same environment, not as a universal claim that Keyina is faster than another Vietnamese input method.

## Environment

- Windows: `Microsoft Windows 10.0.26200`
- Architecture: x64
- Logical processors: 16
- .NET runtime: 10.0.10
- Native compiler: MSVC 19.44
- Native benchmark: Release, 20,000 warm-up operations and 100,000 measured operations per case
- Managed benchmark: Release, 5,000 warm-up operations

## Resident typing stages

The Diagnostics page now exposes an opt-in local table for:

1. full keyboard callback;
2. foreground-process context;
3. safety and secure-input guard;
4. native Vietnamese engine processing;
5. Unicode input injection.

For each stage it shows sample count, p50, p95, p99, maximum, and mean. Profiling is disabled by default. When disabled, the hook performs one volatile flag read and does not call the clock. The profiler stores fixed duration histograms only; it does not store typed text, raw keys, clipboard content, transcripts, or document content.

## Managed hot-path results

Final values below are the median p99 from three Release runs:

| Case | p99 | Allocation/op |
|---|---:|---:|
| Profiler disabled fast path | 100 ns | 0 B |
| Profiler enabled record | 100 ns | 0 B |
| Real Win32 foreground/focus/password snapshot | 400 ns | 0 B |
| Native bridge literal key | 300 ns | 0 B |
| Native bridge Telex transform | 600 ns | 0 B |
| Injection event preparation | 100 ns | 0 B |
| Disabled hook fast path | 100 ns | 0 B |
| Full hook literal path | 500 ns | 0 B |
| Full hook transformed path | 1.0 µs | 0 B |

The foreground-context case calls the real Win32 focus and password-style probe rather than a fake. The deterministic injection benchmark measures event construction and dispatch to a fake sender. Real `SendInput` duration depends on Windows and the target application and is measured by the opt-in runtime Diagnostics profiler instead.

## Resident input runtime

`KeyinaInput.exe` is now the default resident process. It owns one native message loop, one `WH_KEYBOARD_LL` hook, the C++ Vietnamese engine, Unicode injection, configurable hotkey routing, and the minimal tray. The .NET/WinForms host starts only for Settings, Speech, Translation, Undo, or fallback diagnostics.

Pointer clicks and wheel input are observed through the same message-only window. Keyina does not install `WH_MOUSE_LL`, does not create a pointer thread, and never generates `INPUT_MOUSE`. Raw mouse registration is absent when there is no active composition and is posted asynchronously after composition begins. One million synthetic movement packets are classified as non-reset events; only button-down and wheel flags reset composition.

The keyboard callback performs no file I/O, network I/O, UI work, or process launch. It updates fixed-size state, runs the engine, and posts optional commands to the message loop. Escape and Undo are not captured unless the on-demand command companion is already active, avoiding interference with normal applications and games. The runtime profile is checked once per second on the message loop and atomically reloaded without restarting the hook.

The published-bundle resource gate measures both private working set and private commit, rejects any value over 10 MiB, and rejects resident thread growth. Physical desktop input is reported as contamination metadata instead of failing the product gate; benchmark summaries use only uncontaminated samples. Three uncontaminated Release tray runs produced:

| Metric | Range | Median |
|---|---:|---:|
| Idle CPU over 5 seconds | 0–0.0195% measured | 0% |
| Total working set, shared-inclusive | 11.19–11.23 MiB | 11.20 MiB |
| Private working set | 2.51–2.55 MiB | 2.52 MiB |
| Private bytes / commit | 2.76–2.82 MiB | 2.77 MiB |
| Resident thread delta | 0 | 0 |
| Physical keyboard events | 0 | 0 |
| 10 MiB private-memory gate | pass in 3/3 runs | pass |

Total working set includes shared Windows and injected-module pages and is not the amount of private RAM owned by Keyina. The Windows CPU counter also has finite resolution, so `0%` means no measurable CPU time in the five-second sample rather than mathematical zero.

### Allocation improvements

| Path | Before | After |
|---|---:|---:|
| Native bridge literal | 24 B/op | 0 B/op |
| Native bridge transform | 48 B/op | 0 B/op |
| Input preparation | 120 B/op | 0 B/op |
| Full hook literal | 24 B/op | 0 B/op |
| Full hook transform | 168 B/op | 0 B/op |

The changes responsible were a lazy one-character UTF-16 string cache, span-based injection, stack allocation for ordinary edits, pooled fallback buffers for large edits, direct literal-character comparison, and disabling diagnostic trace formatting outside explicit profiling sessions.

## Native engine results

The native benchmark now counts heap allocations in addition to latency. The table uses the median p99 from three post-change Release runs.

| Case | Baseline p99 | Final median p99 | Baseline allocations/op | Final allocations/op |
|---|---:|---:|---:|---:|
| Complete `tieengs` | 2.7 µs | 1.9 µs | 11 | 0 |
| Complete `Vieetj` | 2.0 µs | 1.3 µs | 6 | 0 |
| Delayed modifier `truowcs` | 5.5 µs | 3.1 µs | 16 | 0 |
| Backspace recomposition | 4.9 µs | 2.4 µs | 15 | 0 |
| Protected URL | 2.9 µs | 1.6 µs | 33 | 0 |
| Protected email | 36.8 µs | 27.1 µs | 36 | 1 |
| Invalid boundary restoration | 31.9 µs | 20.4 µs | 67 | 1 |

The engine now reserves its maximum active-token storage once and reuses dedicated composition and previous-key buffers. It computes the edit against the existing visible buffer and swaps buffers instead of copying and reallocating the token at every key.

Context Guard also has a specialized fast path for ordinary ASCII/Unicode letter tokens. Tokens containing technical punctuation or digits continue through the fully compatible classifier. In Debug builds, every fast-path result is asserted against the original classifier; the deterministic million-event endurance test exercises this equivalence continuously. The 64-code-point ordinary-token Context Guard p99 decreased from roughly 900 ns to a three-run median of 300 ns.

The remaining single allocation in protected-email and invalid-boundary cases is the owned multi-character replacement text returned to the caller when a whole token must be restored. This is a rare transition rather than the normal per-key path. Eliminating it would require changing the `TextEdit` ownership representation and must be accepted only if a benchmark shows that a larger inline edit object does not regress ordinary keys.

## Rejected optimization

Release interprocedural optimization/LTCG was tested and removed. On this machine it made representative complete-token p99 results worse, including `tieengs`, delayed modifiers, protected email, and invalid-word restoration.

A universal single-pass technical-token classifier was also benchmarked and rejected because it improved ordinary words but regressed URL, email, and maximum-length Context Guard cases. It was replaced by the narrower ordinary-word fast path described above. Keyina does not retain an optimization merely because it sounds faster; changes remain only when repeated measurements and compatibility checks support them.

## Verification

Fresh gates after the changes:

- Release solution build: 0 warnings, 0 errors.
- Host tests, including real desktop hook integration, shared modifier observation, profile publishing, settings/command companions, focus-locked dictation, startup resolution, release/installer contracts, pointer lifetime, injected-event isolation, disabled fast path, and resource-budget checks: 282/282 passed.
- Dedicated-hook regression: input remains responsive while the owner UI thread is blocked.
- Secure-input regression: password state is refreshed even when the focused HWND does not change.
- Partial-startup regression: a pointer-observer startup failure releases the already-installed keyboard hook.
- Pointer regression: pointer reset never calls the keyboard injector and cannot synthesize a click.
- Published native tray resource self-test: three uncontaminated runs passed the 10 MiB private-memory and zero-thread-delta gate.
- Native Release CTest: 6/6 passed, including live typing, tray resource, profile reload, injection, pointer, hotkey, and unit coverage.
- Native Debug CTest: 6/6 passed with the same integration lanes.
- Native endurance: 1,000,000 deterministic mixed events preserve edit, visible-text, rollback, and token-size invariants in both Debug and Release.
- Managed Release benchmark: every latency and allocation budget passed; disabled hook p99 was 100 ns, literal p99 700 ns, and transformed p99 1.8 µs, all at 0 B/op.
- Published bundle smoke tests passed for native startup, live typing, resource measurement, profile reload, managed self-test, and isolated companion-state publishing.
- Native Release benchmark: every allocation budget passed.
- `git diff --check`: clean at the recorded checkpoint.

## Local EVKey process comparison

EVKey64 was sampled five times while already running on the same development machine. Every sample reported 13.31 MiB total working set, 0.46 MiB private working set, 4.48 MiB private bytes, 0% measured CPU, 4 threads, and 1,002 handles. Keyina's three-run native-tray median was 11.20 MiB total working set, 2.52 MiB private working set, 2.77 MiB private bytes, 0% measured CPU, no thread growth beyond the process baseline, and 164 handles. EVKey therefore had the lower immediately resident private working set in this snapshot; Keyina had roughly 38% lower private commit, lower shared-inclusive working set, and substantially fewer handles. These metrics measure different resource concepts and do not establish an overall winner.

No universal “faster than UniKey” or “faster than EVKey” claim is made from internal microbenchmarks or one process snapshot. A valid product comparison still requires the same keyboard corpus, target applications, startup state, observation method, correctness checks, repeated frame-time measurements, elevated/fullscreen coverage, and long-running stability without capturing user content.
