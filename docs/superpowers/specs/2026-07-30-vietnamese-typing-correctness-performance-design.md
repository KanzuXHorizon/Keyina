# Vietnamese Typing Correctness and Performance Design

> **Current behavior note (2026-08-03):** Raw-key Backspace reconstruction described here remains a core-engine test primitive only. Product input paths reset active composition and pass physical Backspace through so it deletes one visible character.

## Goal

Improve practical Vietnamese Telex correctness and typing-path performance without changing unrelated behavior, stealing focus, or adding background overhead.

## Scope

- Fix reproducible composition errors found in real user input, including flexible modifier order, tone placement, repeated keys, backspace, and word boundaries.
- Preserve literal text for URLs, email addresses, code, and non-Vietnamese tokens when the engine lacks enough evidence to transform safely.
- Measure both engine-only latency and the real callback pipeline, with correctness taking precedence over microbenchmark gains.
- Improve diagnostics only where needed to make benchmark results trustworthy and understandable.
- Close verified Telex compatibility gaps against the official UniKey manual and the newer x-unikey engine behavior, without copying GPL implementation code.

## Clean-room Reference Hierarchy

1. Official UniKey documentation defines user-visible Telex semantics such as `z` tone removal, repeated-key escape, single `w`, quick `[`/`]` letters, and modern `oa`/`oe`/`uy` tone placement.
2. The published x-unikey engine is a behavioral oracle only. Keyina must reproduce independently verified input/output behavior through its existing C++20 model; no source text, tables, or implementation structure may be copied.
3. Keyina-specific safety rules take precedence where blindly matching a legacy IME would damage code, URLs, identifiers, or English text.

## UniKey Compatibility Delta

- `z` removes an existing tone mark while preserving the vowel shape (`ắz` becomes `ă`, `ớz` becomes `ơ`). When no tone is present, `z` remains literal so English and identifiers are not silently damaged.
- Modern placement covers open `oa`, `oe`, and `uy`: `hoaf` → `hoà`, `khoer` → `khoẻ`, `thuyr` → `thuỷ`. Traditional placement keeps `hòa`, `khỏe`, and `thủy`.
- `u` after `q` and `i` after `g` remain consonantal glide handling, so `quyf` targets `y` and `giaf` targets `a` regardless of open-cluster policy.
- A standalone Telex `w` may produce `ư`, but the repeated-key escape must restore literal `w` deterministically.
- Quick letters `[` → `ư` and `]` → `ơ` are engine capabilities behind an explicit disabled-by-default configuration because brackets are common in source code.
- Every compatibility behavior includes uppercase, replacement-tone, repeated-key, Backspace reconstruction, and boundary-stability coverage where applicable.

## Composition History Model

The engine keeps three distinct views of the active token:

1. **Physical raw keys** preserve every key event for exact Backspace reconstruction and diagnostics-free state ownership.
2. **Canonical literal text** replays the user's intended Latin text while collapsing a repeated Telex escape pair. For example, the physical sequence `harrdcode` has canonical literal text `hardcode`, and `guitarrist` has canonical literal text `guitarist`.
3. **Vietnamese composition** applies Telex modifiers and tone placement.

`restore_invalid_word` selects canonical literal text only when the Vietnamese composition is structurally impossible under Vietnamese onset/nucleus/coda and orthography rules. It must not contain a hard-coded English word list. Recoverable states such as an invalid checked-coda tone remain editable so a later tone key can replace the mark.

## Behavioral Principles

1. Never rewrite committed visible text at a separator unless the configured behavior explicitly owns and validates that rewrite.
2. A valid Vietnamese syllable must remain stable at a word boundary.
3. Backspace must match what the user sees: either the engine owns the active composition and rebuilds it consistently, or it releases ownership and lets the application delete normally. It must not create a hidden state mismatch.
4. Flexible Telex order is accepted only when the resulting syllable is structurally valid and unambiguous.
5. Repeated modifier keys must provide a deterministic escape to literal input.
6. Existing user changes are preserved and reviewed before modification.

## Correctness Validation

Add focused regression vectors based on observed input, including:

- `muoson` → `muốn`
- `gox` → `gõ`
- `dodoj` / common flexible variants → `độ`
- `dudojcw` / `dduocwj` / `dduowcj` → `được`
- `haonf` and flexible tone order → `hoàn`
- `asz` → `a`, `aasz` → `â`, and replacement-tone sequences remain deterministic
- modern/traditional `hoaf`, `khoer`, `thuyr`, plus `quyf` and `giaf`
- standalone/repeated `w`, and opt-in `[`/`]` shortcuts
- structural Latin restoration: `fix`, `hard`, `hardcode`, and other tokens rejected by Vietnamese syllable rules
- repeated-escape continuation: `harrdcode` → `hardcode`, `guitarrist` → `guitarist`
- recoverable tone correction: `catfs` → `cát` rather than prematurely restoring literal text
- boundary stability for valid syllables
- literal preservation for representative URL, email, identifier, and English tokens

Tests must verify both the visible output after each key and the final engine state where backspace or boundary ownership matters.

## Performance Design

- Keep the hot path allocation-free after initialization where current interfaces permit it.
- Avoid whole-buffer recomputation when a key can be handled by the existing bounded composition state.
- Benchmark common letters, shape modifiers, tone modifiers, backspace, and commit boundaries separately.
- Retain optimizations only when correctness suites remain green and repeated release benchmarks show a meaningful non-regression.

## Diagnostics

Diagnostics should distinguish insufficient samples from meaningful percentile data, report sample count and tail latency, and avoid presenting bucket-rounded percentiles as exact measurements. No input content may be captured.

## Acceptance Criteria

- All existing native tests pass.
- New observed-input regressions pass.
- No valid Vietnamese word is rewritten destructively at boundaries.
- Backspace behavior has no mismatch between engine state and displayed text.
- Literal-token protections remain green, including words containing an ordinary `z` when no tone exists.
- Invalid-word restoration is derived from syllable structure, not a per-word English exception list.
- Repeated Telex escape keys never reappear when later input causes literal restoration.
- Official UniKey examples added to the checked-in golden corpus pass in both visible-output and Backspace rollback checks.
- Release benchmark results do not regress materially; any retained optimization has measured evidence.
- Final diff contains no unrelated cleanup.
