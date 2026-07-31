# Keyina Native Callback Fast-Path Design

## Goal

Reduce real `WH_KEYBOARD_LL` callback work without changing Vietnamese composition, hotkey release handling, secure-input behavior, focus safety, or application compatibility.

## Scope

This is the first independently verifiable slice of the wider native-performance program:

1. Remove typing-context capture from ordinary key-up events after hotkey routing.
2. Preserve release suppression for physical keys whose key-down was consumed by the engine.
3. Add content-free counters that prove the optimized path is used in the live typing self-test.
4. Re-run native correctness, integration, resource, and Release benchmark gates.
5. Record the Rust migration decision and defer any rewrite until a measured C++ bottleneck remains after Win32-call reduction.

## Architecture

The native resident remains C++20. `Win32InputRuntime` continues to own the low-level hook, key state, hotkey routing, context capture, injection, message loop, tray, and profile reload. After hotkey routing, non-suppressed key-up events are sent directly to `ResidentInputController::Process` with an empty context because that branch only clears the controller's suppressed-key state and never reads typing context.

Key-down events retain the current flow: capture focused context, apply secure/application bypass rules, process the engine, update pointer observation, and inject or defer edits. A monotonically increasing context-capture counter is exposed only for diagnostics and self-tests; it stores no key, text, window title, clipboard content, or process name.

## Safety invariants

- Negative hook codes and unsupported messages always call `CallNextHookEx`.
- Keyina-marked injected events are never reprocessed.
- Hotkey key-up suppression and push-to-talk release behavior remain before the new fast path.
- Engine-consumed physical key-up events remain suppressed.
- Secure, password, elevated, excluded, unknown, and failed contexts remain fail-open.
- No heap allocation, file access, network access, formatting, logging, or process launch is added to the callback.
- The normal callback does not call a high-resolution timer unless a future opt-in profiler explicitly enables it.

## Performance model

A normal physical keystroke produces a down and an up event. Before this slice, both events call `GetGUIThreadInfo`, inspect focus, and query focus style even though the controller's key-up path only manages a fixed key-state bitset. The new path removes those Win32 calls from ordinary key-up processing, reducing OS transitions for approximately half of non-hotkey keyboard events.

The live typing self-test must prove that context captures do not exceed generated key-down events. Resource gates remain based on private working set, private bytes, thread delta, handle count, idle CPU, hook state, and input contamination.

## Rust decision

Do not rewrite the resident or engine in Rust in this slice. The current resident already uses native C++20, fixed arrays on the ordinary path, zero-allocation engine processing, and approximately 2.6–2.8 MiB private working set in local resource probes. The dominant remaining work includes Win32 calls, target-control behavior, `SendInput`, focus validation, and Windows message delivery; a language rewrite does not remove those operating-system costs.

Rust remains acceptable for a later isolated component when all of the following are true:

1. A repeatable profile identifies memory-safety or maintainability risk that Rust materially reduces.
2. A narrow C ABI or process boundary prevents a whole-resident rewrite.
3. Release size, startup, private memory, p99 latency, compatibility, signing, and installer tests are no worse than C++.
4. The migration can be rolled back without changing configuration or user-visible behavior.

## Acceptance criteria

- Live native typing still produces the expected Vietnamese text in direct and clipboard compatibility modes.
- The typing self-test reports no more typing-context captures than generated physical key-down events.
- Engine-consumed key-up events are still suppressed by existing controller tests.
- Native unit tests pass.
- All native CTest integration tests pass.
- Debug and Release native builds pass.
- Resident private working set and private bytes remain below the existing 10 MiB gate with no added resident thread.
- Release engine benchmark cases remain within configured latency and allocation budgets.
- The final commit contains only the design, plan, focused runtime changes, and their tests/documentation.

## Later slices

1. Add opt-in native callback histograms with fixed buckets and effectively zero disabled cost.
2. Classify injection outcomes without guessing UIPI from `GetLastError`.
3. Reduce repeated application/profile lookups while preserving secure-context freshness.
4. Split the monolithic runtime by message-loop, injection, context, tray, and diagnostics responsibilities without changing binary boundaries.
5. Add long-duration burst, suspend/resume, desktop-switch, focus-churn, and hook-liveness compatibility probes.
