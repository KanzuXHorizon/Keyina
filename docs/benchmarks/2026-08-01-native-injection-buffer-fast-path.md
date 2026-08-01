# Native Injection Buffer Fast Path Verification — 2026-08-01

## Evidence before optimization

MSVC Release disassembly of the former selection-replacement send lambda showed that every injection, including a two-event literal character, performed all of the following:

```text
stack frame: 0x3C90 bytes (15,504 bytes)
stack probe:  __chkstk
zero fill:    memset 0x3C50 bytes (15,440 bytes)
```

The maximum-capacity `INPUT` array was value-initialized on every call even though ordinary Chromium owned-stream delivery usually emits only two records.

## Implementation

Native keyboard and selection-replacement senders now use two fixed stack tiers:

```text
fast tier:     16 INPUT records
fallback tier: 386 INPUT records
```

The exact required event count is calculated before selecting a tier. The fast tier covers literal Unicode and common Telex replacements. The fallback remains available for long compositions and snippet chunks.

Both tiers use default-initialized arrays. Sequence builders construct a zero-initialized local `INPUT` for each emitted record and overwrite every destination slot that is passed to `SendInput`; unused storage is never read or sent.

The fallback helpers are explicitly no-inline so their large stack frames cannot inflate the ordinary fast caller.

Additional hardening rejects a corrupted `insert_units` value greater than the fixed insertion array size before reading source data or writing the destination buffer.

## Evidence after optimization

MSVC Release disassembly of `BuildAndSendSelectionInput<16>` and `BuildAndSendKeyboardInput<16>` shows:

```text
stack frame: 0x2C0 bytes (704 bytes)
stack probe:  none
memset:       none
```

The rare 386-record fallback still uses a `0x3C90`-byte stack frame and `__chkstk`, as expected, but it no longer zeroes the entire buffer and is reached only when the exact required count exceeds 16 records.

Compared with the former ordinary path, the fast path removes:

```text
14,800 bytes of stack reservation per call
one __chkstk call per call
15,440 bytes of memset per call
```

No heap allocation, thread, queue, retry, dependency, or additional Win32 call was added.

## Correctness coverage

Native tests now prefill destination arrays with non-keyboard garbage before constructing sequences. Every emitted record must still be fully initialized as marked keyboard input.

A corruption test verifies that oversized `insert_units` is rejected without modifying any destination slot.

Final native unit result:

```text
137/137 passed
```

## Interactive Chromium diagnostic

Three clean Debug runs of `--chromium-ordering-self-test` all produced the exact sentence at 0, 5, and 10 ms inter-key delays:

```text
tuỳ bạn cứ research và đưa ra hướng tốt nhất <space>
```

Every case processed 116/116 marked physical events, completed 58/58 text injections, and reported zero injection failures.

Injection mean values from the three runs were:

```text
0.978 ms
1.187 ms
1.239 ms
```

The median run was approximately 1.187 ms. These interactive desktop timings are directional diagnostics only; scheduler and foreground activity make them unsuitable as a universal regression percentage. The Release assembly change is the deterministic evidence for the removed local overhead.

## Final verification

- Native Debug CTest: `12/12` passed.
- Native Release CTest: `12/12` passed.
- Native unit tests: `137/137` passed.
- Managed Release tests: `307/307` passed.
- Managed Release build: 0 warnings, 0 errors.
- Resident without tray: 2,572,288-byte private working set, 4 threads, 0 thread delta, budget passed.
- Resident with tray: 2,682,880-byte private working set, 4 threads, 0 thread delta, budget passed.

## Limits

This optimization removes Keyina-owned stack setup overhead. It does not remove the dominant operating-system and target-application cost of `SendInput`, hook chaining, scheduling, or target text processing. Real Microsoft Edge testing remains a separate manual compatibility requirement.
