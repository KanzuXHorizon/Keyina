# Keyina Reliability Hardening Design

> **Current behavior note (2026-08-03):** Backspace reconstruction in this historical design is no longer exposed by native, managed, or TSF input paths. Physical Backspace resets composition and is handled by the target application.

## Goal

Make the default Keyina typing path reliable enough for sustained everyday use before pursuing broader feature, UI, or architectural expansion. This phase focuses on correctness, event delivery, runtime configuration consistency, performance regression gates, and evidence from the real Windows application.

## Scope

### Included

- Correct Vietnamese Telex composition for representative normal, delayed-order, name, Backspace, and mixed Vietnamese/Latin cases.
- Reliable physical-key and replacement-input delivery through the resident Windows hook under burst typing.
- Consistent `RestoreInvalidWord` behavior across managed configuration, the 36-byte runtime profile, native decoding, and the managed fallback hook.
- Focused cleanup of regressions introduced by the current uncommitted Telex/runtime-profile changes.
- Debug and Release verification for native and managed components.
- CPU, memory, callback-latency, resource-lifetime, and burst-typing regression checks using existing project probes and benchmarks.
- Manual real-app verification in representative browsers, editors, terminals, Office-style controls, multiline controls, password fields, and elevated/unsupported contexts.

### Excluded

- Dictionary-based spelling correction or automatic replacement of intentional misspellings.
- Broad UI redesign, new speech or translation providers, updater work, installer redesign, or unrelated refactoring.
- Network, file, allocation-heavy, or process-launch work in the low-level keyboard callback.
- Changing fail-open behavior for secure, elevated, unsupported, or uncertain targets.

## Current baseline

- Native Debug build and CTest complete successfully with 9/9 tests passing.
- Managed Debug build completes with no warnings or errors.
- Managed host tests currently pass 294/296 cases.
- One deterministic regression is caused by the current `engine.cpp` invalid-Latin restoration change: valid `nguyenx` is restored to literal text before the Backspace assertion is reached.
- One live Windows integration failure loses a physical character during burst typing, producing output such as `tước` from raw `truwocs`.
- `RuntimeInputProfileCodec.ComposeFlags()` currently applies `RestoreInvalidWordFlag` redundantly.

## Behavioral contract

### Telex correctness

- `nguyeenx` and supported delayed-order variants produce `nguyễn`.
- `nguyenx` remains a valid Telex intermediate/result according to the existing engine contract and is not converted to literal Latin merely because its nucleus analysis is incomplete.
- Backspace reconstructs the raw composition state, then subsequent Telex input continues from that reconstructed state.
- Literal Latin tokens including `search`, `research`, `powershell`, identifiers, paths, URLs, and structurally impossible Vietnamese tokens recover to physical-key text without a dictionary.
- Intentional repeated-key escape behavior remains available.
- Word boundaries commit visible text and never rewrite the preceding word.

### Hook reliability

- Every synthetic physical key-down and key-up used by the live integration harness is observed exactly once or the attempt is rejected with explicit delivery evidence.
- Keyina replacement input is never reprocessed as physical input.
- A transformed edit is fully delivered before the next test key is considered complete.
- Focus, pointer-reset, secure-input, and application-bypass transitions reset composition without swallowing unrelated physical input.
- Failures in injection fail open and reset state instead of leaving a partially owned composition.

### Runtime configuration

- The managed encoder publishes `RestoreInvalidWord` exactly once in the canonical flag byte.
- Managed and native tests share the same checksum-correct 36-byte default vector.
- Default native resident behavior matches the managed fallback hook for invalid-Latin restoration.
- Runtime profile reload applies the setting without process restart or partial-file reads.

## Architecture and change boundaries

### Native engine

The native engine remains the single source of truth for Telex composition. Restoration decisions must be based on structural evidence that a token cannot remain a valid or recoverable Vietnamese composition. A broad `InvalidNucleus + any tone key` rule is not acceptable because incomplete but valid Telex sequences can temporarily have that shape.

Any engine change must be driven by a focused failing regression. The preferred design is to refine the restoration predicate rather than alter core composition, tone placement, or rollback mechanics.

### Managed hook and Windows input delivery

The hook callback remains synchronous, bounded, and free of blocking UI work. Reliability changes should first determine whether the lost character originates from:

1. the test sender reporting completion before the target control applies injected replacement input;
2. physical key-up suppression state colliding with a later same-virtual-key event;
3. focus or pointer-reset state changing between events;
4. partial or reordered `SendInput` delivery;
5. the engine producing an unexpected edit.

Evidence must be captured at these boundaries before changing timing or adding retries. Fixed sleeps may remain only when backed by a deterministic event or state condition; condition-based completion is preferred.

### Runtime profile

The profile format and checksum remain unchanged. Only the canonical flag composition and corresponding vectors/assertions may change.

## Implementation sequence

1. Preserve and review the current uncommitted work; do not overwrite unrelated changes.
2. Reproduce the Telex regression with the smallest focused native and managed tests.
3. Replace the over-broad restoration condition with the narrowest structurally correct predicate.
4. Remove duplicate runtime-profile flag composition and verify managed/native vector parity.
5. Reproduce the live-hook loss repeatedly with expanded boundary evidence.
6. Fix the earliest incorrect boundary with a focused regression test.
7. Run full Debug and Release verification.
8. Run resource, latency, and benchmark gates and compare against existing thresholds.
9. Publish and run the actual Windows bundle, then execute the manual compatibility matrix.
10. Record remaining incompatibilities and defer non-reliability work to later phases.

## Test strategy

### Focused automated tests

- Native engine tests for names, delayed Telex order, literal Latin restoration, Backspace reconstruction, repeated-key escape, and boundary commits.
- Managed keyboard-hook tests that assert exact edit sequences and visible text.
- Managed/native runtime-profile byte-vector and decode tests.
- Live Windows hook stress tests with event counts, transform details, focus HWND/process, reset reasons, and final target text.

### Full automated gates

- Native Debug and Release build plus CTest.
- Linux Clang ASan/UBSan lane where available.
- Managed Debug and Release build and all host tests.
- Native resident self-tests: core, typing, resource, tray resource, and profile reload.
- Host self-tests: core, speech offline, hotkeys, and resources.
- Golden-vector validation and benchmark-comparator tests.
- Native and managed performance benchmarks.
- `git diff --check`, final diff inspection, and secret/artifact review.

### Real-application matrix

At minimum, verify:

- Chromium browser text field and multiline editor.
- PowerShell or Windows Terminal command line, including literal `powershell`, paths, flags, and identifiers.
- VS Code editor and integrated terminal.
- Notepad or another basic Win32 text control.
- Office-style rich-text field when installed.
- Password field, elevated target, application exclusion, clipboard paste, selection replacement, rapid Backspace, focus switching, and mouse-click reset.

For each target, record expected text, actual text, bypass/reset behavior, visible lag, CPU, memory, and any dropped or duplicated key.

## Acceptance criteria

- All native and managed automated tests pass in fresh Debug and Release runs.
- The two current host failures are resolved by verified root-cause fixes, not widened retries alone.
- No known valid Telex sequence in the regression matrix is restored to literal Latin prematurely.
- Literal `powershell`, paths, commands, URLs, identifiers, and tested English words remain literal.
- The live hook completes repeated burst cases without dropped, duplicated, or reordered physical characters.
- Existing callback latency and resource thresholds remain within their configured gates; any threshold change requires measured justification.
- No new ordinary-typing network, file, clipboard, telemetry, or credential access is introduced.
- Manual real-app testing passes the core compatibility matrix or leaves an explicit, reproducible compatibility issue with fail-open behavior.

## Risks and mitigations

- **Overfitting restoration logic:** use varied valid and invalid tokens, not a single word-specific condition.
- **Masking desktop interference with retries:** require event/focus evidence and fail the final attempt with diagnostics.
- **Timing-dependent tests:** wait on observable processed/delivered state instead of increasing arbitrary sleeps.
- **Regression in literal typing:** maintain dedicated terminal, path, URL, identifier, and English-token tests.
- **Performance regression:** run existing callback and resource benchmarks before and after each material hook change.
- **Unrelated work contamination:** keep changes limited to files directly tied to a reproduced failure or verification gate.

## Deliverables

- Focused fixes for the confirmed Telex, runtime-profile, and live-hook reliability defects.
- Regression tests proving each defect before and after the fix.
- Fresh Debug/Release test and benchmark evidence.
- A real-application compatibility result covering the defined matrix.
- A prioritized list of remaining reliability issues, separated from later UI, feature, packaging, and update work.
