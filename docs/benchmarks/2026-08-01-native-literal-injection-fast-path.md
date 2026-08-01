# Native Literal Injection Fast Path Verification — 2026-08-01

## Evidence before optimization

MSVC Release disassembly of `BuildLiteralInputDecision` showed that every Chromium-owned literal character performed work for a complete edit object:

```text
stack frame: 0x160 bytes (352 bytes)
memset:       0x102 bytes (258 bytes)
output:       roughly 304 bytes copied into caller InputDecision
```

The runtime then read only one or two UTF-16 units from that large structure to create two or four Win32 `INPUT` records.

## Implementation

The literal path now calls:

```cpp
std::size_t BuildLiteralUnicodeInputSequence(
    char32_t character,
    std::span<INPUT> destination) noexcept;
```

The builder validates the Unicode scalar, checks destination capacity before writes, and emits marked Unicode key-down/key-up pairs directly. The runtime provides a four-record default-initialized stack array and passes only the exact returned count to `SendInput`.

The former `BuildLiteralInputDecision` API and the per-character `InputDecision literal_decision` are removed from production.

Validation rejects:

- U+0000;
- surrogate code points;
- values above U+10FFFF;
- destinations smaller than two events for BMP or four events for supplementary scalars.

Every rejected case returns zero without modifying the destination.

## Evidence after optimization

MSVC Release disassembly of `BuildLiteralUnicodeInputSequence` shows:

```text
stack frame: 0x58 bytes (88 bytes)
memset:       none
InputDecision construction: none
large result copy: none
```

Compared with the previous helper, every owned literal character removes:

```text
264 bytes of stack reservation
258 bytes of memset
one roughly 304-byte InputDecision copy
```

The new builder writes only the exact two or four `INPUT` records required by the scalar.

## Correctness coverage

Native tests verify:

- exact BMP event sequence, marker, scan code, and flags;
- exact surrogate order and four-event sequence for supplementary scalars;
- invalid scalar rejection;
- insufficient-capacity rejection;
- destination preservation on every rejected case;
- operation on garbage-prefilled storage.

Final native unit count remains:

```text
137/137 passed
```

## Interactive Chromium diagnostic

Three clean Debug runs of `--chromium-ordering-self-test` retained exact output at 0, 5, and 10 ms:

```text
tuỳ bạn cứ research và đưa ra hướng tốt nhất <space>
```

Every delay processed 116/116 marked physical events, completed 58/58 text injections, and reported zero failures.

Injection mean values were approximately:

```text
1.213 ms
1.264 ms
1.264 ms
```

The desktop-level latency is effectively unchanged because `SendInput`, hook chaining, scheduling, and target processing dominate this micro-optimization. The deterministic gain is removal of Keyina-owned structure initialization and copying from every literal character.

## Final verification

- Native Debug CTest: `12/12` passed.
- Native Release CTest: `12/12` passed.
- Native unit tests: `137/137` passed.
- Managed Release tests: `310/310` passed.
- Managed Release build: 0 warnings, 0 errors.
- Resident without tray: 2,584,576-byte private working set, 4 threads, 0 thread delta, budget passed.
- Resident with tray: 2,678,784-byte private working set, 4 threads, 0 thread delta, budget passed.

## Limits

This slice removes local CPU and memory traffic but does not reduce the number of `SendInput` calls. A material end-to-end latency reduction would require a different delivery primitive or safe batching architecture, both of which need application-specific compatibility evidence before adoption.
