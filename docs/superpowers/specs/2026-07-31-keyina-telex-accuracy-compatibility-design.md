# Keyina Telex Accuracy and Compatibility Design

## Goal

Make the default native typing path behave consistently for Vietnamese Telex, mixed Vietnamese/English text, names, and common out-of-order Telex input without adding dictionary-based autocorrection.

## Behavioral contract

- Correct Telex sequences produce the intended Vietnamese text, including `nguyeenx` → `nguyễn` and representative delayed tone/shape orders.
- Literal Latin tokens such as `search`, `research`, `powershell`, identifiers, paths, and URLs recover to their physical-key text when a Vietnamese interpretation becomes structurally impossible.
- Repeated Telex escape behavior remains available for users who intentionally want literal modifier keys.
- Misspellings such as `nguễn` are not silently changed to `Nguyễn`; Keyina is an input method, not a spelling corrector.
- Word boundaries never rewrite already visible text.

## Root cause addressed

The managed runtime profile format already contains a `RestoreInvalidWord` flag and the native resident consumes it, but the managed encoder does not publish the flag. As a result, the default native backend does not enable the same invalid-Latin restoration behavior already enabled by the managed fallback hook.

## Implementation

1. Expand the engine regression matrix with Vietnamese names, delayed Telex orders, literal English tokens, and malformed-but-literal samples.
2. Publish `RestoreInvalidWord` in the default runtime profile so `KeyinaInput.exe` receives the intended behavior.
3. Update the managed/native cross-language profile vectors and assertions.
4. Run focused tests, full native tests, host tests, Release builds, and typing/resource benchmarks.

## Performance and safety

- No dictionary, regex, allocation-heavy lookup, file I/O, or network work is added to the keyboard callback.
- Existing bounded engine buffers and constant-time profile decoding remain unchanged.
- The fix changes one profile flag and relies on the existing structural syllable analysis.
- Injection remains fail-open and injected Keyina events remain excluded from reprocessing.
