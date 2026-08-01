# Native Literal Injection Fast Path Design

## Evidence

MSVC Release disassembly of `BuildLiteralInputDecision` shows that every owned Chromium literal character currently:

- reserves a `0x160`-byte stack frame;
- zeroes `0x102` bytes with `memset`;
- initializes a complete `InputDecision`;
- copies roughly 304 bytes from the temporary decision into the caller;
- then converts that decision into only two or four `INPUT` records.

This work occurs for every supported literal character in the Chromium owned text stream.

## Goal

Encode and send one literal Unicode scalar directly as marked Win32 keyboard input without constructing or copying `InputDecision`.

## Architecture

Replace the internal literal-decision helper with:

```cpp
std::size_t BuildLiteralUnicodeInputSequence(
    char32_t character,
    std::span<INPUT> destination) noexcept;
```

The builder:

- rejects U+0000, surrogate code points, and values above U+10FFFF;
- requires capacity 2 for BMP scalars or 4 for supplementary scalars;
- emits Unicode key-down/key-up pairs with the Keyina injection marker;
- returns zero without modifying destination when validation or capacity fails.

The Win32 runtime uses a four-record default-initialized stack array and calls `SendInput` with the exact returned count. It preserves the existing failure behavior: if injection fails, reset controller state and fail open to the original physical event.

`BuildLiteralInputDecision` is removed because no production caller needs a full edit decision for a zero-erasure literal scalar.

## Constraints

- No heap allocation, thread, queue, retry, dependency, or extra Win32 call.
- Preserve exact Unicode surrogate order, event flags, markers, key-up suppression, and owned-stream policy.
- Destination remains unchanged on invalid scalar or insufficient capacity.
- Do not modify unrelated managed benchmark work.

## Acceptance criteria

- Unit tests cover BMP, non-BMP, invalid scalar, insufficient capacity, garbage-prefilled destination, and exact marker/flags.
- Native Debug and Release CTest pass.
- Managed Release tests remain green.
- Chromium interactive diagnostic remains exact at 0, 5, and 10 ms.
- MSVC Release assembly for the literal builder/sender contains no large `InputDecision` memset or copy.
- Resource budgets remain unchanged and the slice is committed separately.
