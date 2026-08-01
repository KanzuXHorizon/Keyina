# Native Injection Buffer Fast Path Design

## Evidence

MSVC Release disassembly of the existing selection-replacement sender shows that every call:

- allocates a `0x3C90`-byte stack frame;
- calls `__chkstk`;
- zeroes `0x3C50` bytes with `memset`;
- then builds and sends as few as two `INPUT` records for an ordinary literal character.

Chromium owned-stream delivery invokes this path for every supported text key, so the maximum-capacity buffer setup is no longer a rare transformed-edit cost.

## Goal

Remove maximum-buffer initialization and stack probing from ordinary keyboard and selection-replacement injection without changing event sequences, markers, failure semantics, or long-snippet support.

## Architecture

Introduce private native send helpers with two buffer tiers:

- fast tier: 16 `INPUT` records, enough for literal characters and common Telex replacements;
- fallback tier: the existing maximum-capacity arrays for long active compositions and snippet chunks.

Required event count is computed before selecting a tier. Both tiers use default-initialized storage rather than value-initialized storage. The sequence builders already construct a zero-initialized local `INPUT` for every emitted record and assign every used slot before `SendInput`; unused slots are never passed.

The helper returns true for a genuinely empty decision, false for invalid/oversized construction, and otherwise preserves the exact `SendInput(expected) == expected` contract.

## Constraints

- No heap allocation, thread, queue, dependency, retry, or additional Win32 call.
- No change to event order, `dwExtraInfo`, Unicode surrogate handling, clipboard mode, or target policy.
- Fallback maximum capacities remain unchanged.
- Ordinary fast paths must not call `memset` or `__chkstk` in Release assembly.

## Acceptance criteria

- Existing input sequence tests remain green.
- Add tests proving builders fully overwrite used slots even when destination storage contains non-keyboard garbage.
- Native Debug and Release CTest suites pass.
- Managed Release tests remain green.
- Release disassembly shows the ordinary sender no longer allocates/zeros the 15.5 KiB maximum buffer.
- Chromium interactive diagnostic still produces exact text at 0, 5, and 10 ms.
- Resource gates remain within existing budgets.
