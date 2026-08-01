# Core typing corpus and correctness results — 2026-08-01

## Scope

This slice expanded deterministic correctness evidence for Keyina's existing C++20 Telex engine. It did not promote TSF, add background work, change the Windows delivery primitive, add a dictionary, or capture real user text.

The worktree was based on the clean repository HEAD while the actual checkout contained separate Settings, overlay, benchmark, and snippet-list work. Only targeted core, corpus, test, benchmark, and documentation files are intended for synchronization.

## Corpus coverage

| Evidence | Result |
|---|---:|
| Word and technical-token vectors | 283 |
| Event-script sentence cases | 30 |
| Technical vectors with a guard reason | at least 30 |
| Modern/traditional placement pair | `hoaf`, `thuyr`, `khoer` |
| Generated mixed-language stream | more than 2,000 physical events |
| Backspace replay | every eligible golden vector |
| Backspace configurations | restore-invalid-word off and on |
| Native unit tests after expansion | 146/146 |

The event-script corpus covers:

- Vietnamese phrases and a long mixed paragraph;
- delayed Telex modifier orders;
- tone replacement and `z` tone clearing;
- Space, punctuation, Tab, Enter, Reset, and Backspace;
- uppercase and mixed case;
- English words containing Telex keys;
- identifiers, PowerShell switches, URLs, email addresses, paths, IPv4, and IPv6;
- emoji, CJK, mathematical symbols, smart punctuation, precomposed Vietnamese, and NFD combining marks routed as explicit literal boundaries.

Every event checks the 64-key active-composition bound, edit ownership, exact external text, active raw state, visible suffix, and stale-state removal at boundaries.

## Defects reproduced and fixed

### IPv6 technical tokens were not protected

`2001:db8::1` was classified as ordinary transformable text. The correction adds a bounded allocation-free hexadecimal IPv6 recognizer to `ContextGuard` and reports valid addresses as `VersionOrHash`.

Regression coverage includes compressed and full forms plus negative cases:

```text
2001:db8::1
::1
fe80::1234:abcd
2001:0db8:85a3:0000:0000:8a2e:0370:7334

time:now
abc:def
2001:::1
2001:db8::1:
1:2:3:4:5:6:7:8:9
```

The review pass found and corrected a trailing-single-colon acceptance edge case before synchronization.

### Explicit `z` tone clearing conflicted with Latin restoration

With `restore_invalid_word=true`, inputs such as `aasz`, `owjz`, and `uwxz` were restored to their raw Latin sequences instead of preserving the shaped vowels `â`, `ơ`, and `ư` after the user explicitly removed the tone.

`ComposeRaw` now reports whether `z` actually cleared a pending or applied tone. Invalid-Latin restoration is bypassed only when that explicit clear leaves a shaped Vietnamese vowel. Near misses remain literal:

```text
asdz
jazz
fizz
```

No dictionary or general spelling exception was added.

## Corpus expectation corrections

Several proposed vectors were corrected before any production change:

- `ddeepj` does not encode `đẹp`; the correct raw sequence is `ddepj`.
- `vaaf` encodes `vầ`; `và` is `vaf`.
- `roif` encodes `ròi`; `rồi` is `rooif`.

These were oracle defects, not engine defects. First-difference diagnostics now report the case, Unicode scalar index, expected scalar, actual scalar, and event count.

## Backspace evidence

For every eligible checked-in raw vector:

1. type the complete sequence;
2. remove one physical key with engine Backspace;
3. replay the remaining raw prefix in a fresh engine;
4. compare exact external text, `RawKeys()`, and `VisibleText()`;
5. continue until empty.

The matrix runs with `restore_invalid_word=false` and `true`. All prefixes passed. Boundary deletion remains owned by the resident-controller tests rather than being incorrectly attributed to the engine.

## Native Release benchmarks

Three runs used 20,000 warm-up and 100,000 measured iterations on Windows 10.0.26200, Intel64 Family 6 Model 186, MSVC 19.44.

Representative ranges:

| Case | Median | p95 | Allocations/op |
|---|---:|---:|---:|
| ASCII pass-through | 100 ns | 100–200 ns | 0 |
| Letter modifier | 100 ns | 200–300 ns | 0 |
| Tone update | 100–200 ns | 200–400 ns | 0 |
| Complete `tieengs` | 1.1 µs | 1.7–2.3 µs | 0 |
| Complete `Vieetj` | 0.9 µs | 1.1–1.3 µs | 0 |
| Delayed `truowcs` | 2.1–2.2 µs | 2.4–2.5 µs | 0 |
| Backspace recomposition | 1.3 µs | 1.4–2.2 µs | 0 |
| URL guard | 1.0 µs | 1.1–1.2 µs | 0 |
| Email guard | 1.5 µs | 1.9–2.9 µs | 0 within existing budget |
| 64-codepoint guard | 0.2–0.3 µs | 0.3 µs | 0 |

A dedicated post-change IPv6 probe measured:

```text
guard_protected_ipv6 median 100 ns
p95 100 ns
p99 100 ns
0 allocations/op
```

`invalid_boundary_restore` retained stable median near 12.4–12.8 µs and p95 near 22.4–23.1 µs. Its p99/max remained scheduler-noisy as previously documented; no threshold was widened.

## Hook and delivery evidence

Release self-tests after the core changes:

### Pass-through callback

```text
result                         pass
processed events               8,192 / 8,192
failed injections              0
callback p50                   262.144 µs
callback p95                   2.097152 ms
controller mean                403 ns
hook running                   true
```

### Transform callback

```text
result                         pass
processed events               4,096 / 4,096
suppressed edits               512
successful injections          512
failed injections              0
callback p50                   131.072 µs
callback p95                   2.097152 ms
injection p50                  2.097152 ms
injection p95                  4.194304 ms
injection mean                 1.368917 ms
hook running                   true
```

### Typing self-test

```text
result                         pass
processed events               28
successful injections          4
failed injections              0
exact output                   pass
```

The engine remains a microsecond-scale component. Windows delivery and target processing remain the dominant end-to-end cost; this slice intentionally did not introduce unsafe asynchronous delivery.

## Resource evidence from the published worktree bundle

| Mode | Private working set | Private memory | Threads | Thread delta | Handles | Idle CPU | Result |
|---|---:|---:|---:|---:|---:|---:|---:|
| Resident without tray | 2,564,096 B | 2,813,952 B | 4 | 0 | 144 | 0% | Pass |
| Resident with tray | 2,768,896 B | 3,121,152 B | 4 | 0 | 166 | 0% | Pass |

Both probes reported no input contamination and remained below the existing 10 MiB private-working-set budget.

## Verification

Worktree evidence before synchronization:

- Native Debug CTest: 12/12 passed.
- Native Release CTest: 12/12 passed.
- Native unit executable: 146/146 passed.
- Managed Release build: 0 warnings, 0 errors.
- Managed Release tests: 324/324 passed.
- Golden-vector checker: 283 vectors validated.
- Published bundle: completed successfully.
- Resource and tray-resource self-tests: passed.
- `git diff --check`: clean.

### Actual-checkout verification after synchronization

The synchronized `F:\Keyina` checkout produced:

- Native focused unit executable: 146/146 passed.
- Native deterministic Debug CTest lane: 8/8 passed.
- Native deterministic Release CTest lane: 8/8 passed.
- Managed Release build: 0 warnings, 0 errors.
- Managed Release tests: 324/324 passed.
- Golden-vector checker: 283 vectors validated.
- Release engine benchmarks: every case within its allocation budget; IPv6 guard measured 100 ns median and zero allocations.
- Published resident resource probes: passed at 2,547,712-byte private working set without tray and 2,760,704 bytes with tray, with zero thread growth and no input contamination.
- Published unified resident: exactly one process from `F:\Keyina\artifacts\publish\win-x64\KeyinaInput.exe`.

Four desktop-focus-dependent CTest cases could not reacquire the foreground window in the final main-checkout run because the foreground HWND was owned continuously by the LocalSystem `GameInputServiceWindow`. They exited before processing any keyboard event and reported `foreground_confirmed=false`; engine, controller, and injection assertions were not reached. Stopping the service was not permitted, and Keyina did not change the user's 200,000 ms `ForegroundLockTimeout` to force a pass. The same four tests passed as part of the fresh 12/12 Debug and Release worktree runs with byte-equivalent source before synchronization.

## Remaining limits

This corpus proves deterministic engine, context-guard, controller-state, and synthetic hook behavior. It does not prove compatibility with every real third-party input stack.

Real Edge/Chrome, VS Code/Electron, Windows Terminal, Office, elevated applications, Remote Desktop, accessibility tools, and games/raw-input targets remain later compatibility slices. Unsupported or uncertain targets must continue to fail open rather than block or corrupt physical input.
