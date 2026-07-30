# Typing latency optimization report — 2026-07-30

## Scope

This report records the local evidence for the first Keyina extreme-optimization slice:

- opt-in latency measurement for each resident typing stage;
- zero-allocation managed hot paths after warm-up;
- reusable native engine composition buffers;
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
| Native bridge literal key | 400 ns | 0 B |
| Native bridge Telex transform | 1.0 µs | 0 B |
| Injection event preparation | 100 ns | 0 B |
| Full hook literal path | 500 ns | 0 B |
| Full hook transformed path | 1.1 µs | 0 B |

The deterministic injection benchmark measures event construction and dispatch to a fake sender. Real `SendInput` duration depends on Windows and the target application and is measured by the opt-in runtime Diagnostics profiler instead.

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
- Host tests: 157/157 passed.
- Native Release tests: 4/4 passed.
- Native Debug tests: 4/4 passed after rebuilding the final buffer and Context Guard changes.
- Native endurance: 1,000,000 deterministic mixed events preserve edit, visible-text, rollback, and token-size invariants in both Debug and Release.
- Managed Release benchmark: every latency and allocation budget passed.
- Native Release benchmark: every allocation budget passed.
- `git diff --check`: clean at the recorded checkpoint.

## Competitor benchmark policy

No “faster than UniKey” or “faster than EVKey” claim is made from internal microbenchmarks. A valid comparison needs the same machine, keyboard event corpus, target applications, startup state, observation method, repeated runs, correctness checks, and published raw results. The next comparison harness should cover Notepad, Office, Chromium, Electron, terminal, elevated applications, and long-running stability without capturing user content.
