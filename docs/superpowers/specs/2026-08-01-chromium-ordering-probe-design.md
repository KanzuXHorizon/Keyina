# Chromium Owned Text Stream and Ordering Probe Design

## Problem

Synchronous selection replacement removed the previous deferred single-slot queue, but it did not fully preserve order. Literal physical characters still entered the target application's queue while transformed replacements were injected from inside the low-level hook. Under burst input, the two streams could interleave even though each individual `SendInput` call was internally ordered.

A focused native probe reproduced the corruption. The controller produced the correct mixed Vietnamese/Latin sentence, but the target received text such as `reseẻch`, proving the defect was in delivery rather than Telex composition.

## Goal

Make Chromium text delivery one ordered stream and retain a safe interactive probe that verifies the real hook path at 0, 5, and 10 ms inter-key delays.

## Production architecture

For a non-secure Chromium selection-replacement target with Vietnamese input enabled and clipboard compatibility disabled:

1. Keyina suppresses every supported literal text key-down, not only transformed edits.
2. Literal characters are encoded as validated UTF-16 and injected with the Keyina marker.
3. Transformed edits continue to use selection replacement.
4. Matching key-up events for owned literal keys are suppressed through a fixed 256-key bitset.

This makes the application's text queue contain one marked Keyina stream instead of a mixture of physical literal input and injected replacements.

The owned stream is disabled when Vietnamese input is off, the target is secure/bypassed, clipboard delivery is selected, or the target does not require selection replacement. Modifier shortcuts, navigation, Backspace, unsupported keys, and injection failures remain fail-open.

## Pure policy and encoding

`ShouldOwnTextStream(...)` centralizes the safety guards without Win32 calls or allocation.

`BuildLiteralUnicodeInputSequence(...)` converts one valid Unicode scalar directly into two or four marked Win32 Unicode events. It rejects U+0000, surrogate code points, values above U+10FFFF, and insufficient destinations without modifying the caller's buffer.

## Interactive diagnostic

`KeyinaInput.exe --chromium-ordering-self-test` creates a Keyina-owned off-screen `EDIT` and forces Chromium-style selection replacement only when a private self-test input marker is configured.

The probe sends:

- raw Telex: `tuyf banj cuws research vaf dduwa ra huowngs toots nhaats `
- expected output: `tuỳ bạn cứ research và đưa ra hướng tốt nhất `
- delays: 0 ms, 5 ms, and 10 ms

It verifies exact text, marked physical-event counts, suppression/injection counts, zero injection failures, and callback/injection latency snapshots.

The probe is intentionally not a default CTest gate. Foreground ownership is a shared desktop resource and can be stolen by terminals, test runners, notifications, or user activity. Such interference must produce an explicit probe failure rather than a false product regression.

## Safety

- Production cannot enable the forced selection mode without a non-zero accepted self-test marker.
- The interactive probe verifies its own foreground window and focused control before every physical key.
- No third-party application is an intended test target.
- The probe adds no thread; production and self-test resident thread/resource behavior remain directly comparable.
- No clipboard, network, managed host, unbounded queue, or external process is used by the probe.

## Acceptance criteria

- Pure policy and UTF-16 encoding tests pass in Debug and Release.
- The mixed Vietnamese/Latin controller sentence regression passes.
- A clean interactive Debug run produces exact output for all three delays.
- Full native Debug and Release CTest suites remain green without relying on foreground automation.
- Managed Release tests, resource probes, and `git diff --check` remain green.
- Real Edge verification remains a separate manual compatibility requirement and is never claimed from the synthetic probe alone.
