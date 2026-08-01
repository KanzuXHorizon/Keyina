# Chromium Input Ordering Design

## Problem

Keyina currently posts transformed Chromium edits to its resident window and returns from the low-level keyboard hook. The deferred path has one pending slot. A following physical key can therefore reach the target application before the prior replacement is injected, or the second transformed edit can fail because the slot is occupied. Real Microsoft Edge burst testing reproduced severe reordering and malformed Vietnamese text.

## Goal

Preserve exact physical-key order for Chromium and clipboard-compatibility text replacement without adding a worker thread, runtime dependency, or unbounded queue.

## Constraints

- Keep the native resident architecture and one low-level keyboard hook.
- Keep the callback bounded; no file, network, managed-host, or process-launch work.
- Preserve injection markers so Keyina never reprocesses its own events.
- Preserve secure-input, elevated-target, focus-change, and fail-open behavior.
- Do not change Telex composition rules in this slice.
- Do not add a background thread or heap-backed event queue.

## Options considered

### 1. Synchronous atomic delivery — selected

For transformed edits, choose one delivery mode and execute it before returning from the hook:

- ordinary targets: Backspace plus Unicode in one `SendInput` sequence;
- Chromium targets: Shift+Left selection plus Unicode replacement in one `SendInput` sequence;
- explicit clipboard compatibility: bounded clipboard replacement followed by marked Ctrl+V.

`SendInput` serializes every event in the supplied array without interleaving user or other injected keyboard events. This removes the ordering gap and the single-slot backpressure state. Clipboard acquisition remains bounded by the existing five retries with 2 ms sleeps.

### 2. Fixed FIFO and replay

Suppress every subsequent physical key while a deferred edit exists, enqueue key-down/key-up/modifier state, then replay the stream. This keeps expensive work outside the hook but introduces stuck-key, modifier, overflow, focus, and shutdown states. It also requires more resident memory and substantially more tests.

### 3. Worker thread

Move delivery to a dedicated thread and queue all physical events. This still requires suppressing and replaying the complete stream to prevent overtaking, adds a resident thread, and violates the existing one-thread resource target.

## Decision

Use synchronous atomic delivery and delete the deferred text-edit queue. Rename Chromium classification to describe the required selection-replacement behavior rather than deferral. Centralize mode selection in a small pure function so the ordering policy has direct regression tests.

## Follow-up correction

Implementation evidence showed that synchronous replacement alone removes the pending-slot race but does not guarantee complete target-queue ordering. Physical literal characters can still coexist with injected replacement events and interleave under burst input. The follow-up design `2026-08-01-chromium-ordering-probe-design.md` therefore makes safe Chromium text a single marked Keyina-owned stream. This document remains the rationale for deleting the deferred queue; the follow-up owns the final ordering architecture.

## Error handling

If the selected delivery fails, reset composition, disable pointer observation, increment failure diagnostics, and pass the current physical event onward as the existing fail-open path does. Clipboard restoration remains sequence-number guarded and never overwrites clipboard changes made by another application.

## Verification

1. A focused unit test must fail before implementation because the new synchronous delivery policy does not exist.
2. Native Debug and Release tests must pass.
3. Managed tests must remain green because runtime profile and companion contracts are unchanged.
4. A real Edge input must produce exactly `tùy bạn cứ research và đưa ra hướng tốt nhất` at 0 ms, 5 ms, and 10 ms inter-key delay.
5. The opt-in callback profiler must show the transformed path remains far below the Windows low-level-hook timeout; ordinary typing latency must not regress materially.
