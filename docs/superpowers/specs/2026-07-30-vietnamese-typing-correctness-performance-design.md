# Vietnamese Typing Correctness and Performance Design

## Goal

Improve practical Vietnamese Telex correctness and typing-path performance without changing unrelated behavior, stealing focus, or adding background overhead.

## Scope

- Fix reproducible composition errors found in real user input, including flexible modifier order, tone placement, repeated keys, backspace, and word boundaries.
- Preserve literal text for URLs, email addresses, code, and non-Vietnamese tokens when the engine lacks enough evidence to transform safely.
- Measure both engine-only latency and the real callback pipeline, with correctness taking precedence over microbenchmark gains.
- Improve diagnostics only where needed to make benchmark results trustworthy and understandable.

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
- Literal-token protections remain green.
- Release benchmark results do not regress materially; any retained optimization has measured evidence.
- Final diff contains no unrelated cleanup.
