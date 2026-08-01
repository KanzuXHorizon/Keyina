# Native Snippet Overlay Fast Path Design

## Evidence

`UpdateSnippetOverlay()` currently runs after every physical key-down. Even when no snippet suggestion prefix exists, it:

- calls `snippet_suggestions(...)` and constructs an empty vector;
- calls `HideSnippetOverlay()`;
- after an overlay has existed once, repeatedly calls `ShowWindow(SW_HIDE)` on every unrelated key-down.

When suggestions are present, `SetWindowPos(..., SWP_SHOWWINDOW)` is immediately followed by a redundant `ShowWindow(SW_SHOWNOACTIVATE)`.

Release pass-through callback baseline from three isolated runs:

```text
callback mean: 106.804–150.175 µs
median mean:   118.384 µs
p95:           262.144 µs in all runs
p99:           1.049 ms in all runs
```

The desktop benchmark is noisy, so this slice is accepted primarily on deterministic call elimination and unchanged correctness.

## Goal

Make ordinary non-snippet key-downs return before vector construction and make overlay visibility transitions idempotent.

## Architecture

Add a pure `IsRuntimeSnippetSuggestionPrefix(token)` helper and use it in both the matcher and Win32 overlay path. The predicate recognizes only tokens beginning with case-insensitive `;k`, matching the existing suggestion contract.

Add `snippet_overlay_visible_` to `Win32InputRuntime`:

- hide only when the overlay is currently visible;
- set visible after successful `SetWindowPos(..., SWP_SHOWWINDOW)`;
- remove the redundant second `ShowWindow` call;
- reset visibility when the overlay is destroyed.

## Constraints

- No change to snippet matching, expansion, case sensitivity, maximum suggestions, text content, position, size, focus behavior, or profile format.
- No heap allocation, thread, timer, dependency, or new Win32 call.
- Preserve fail-open behavior when overlay creation or positioning fails.
- Do not stage unrelated managed benchmark work.

## Acceptance criteria

- Pure prefix tests cover empty, partial, lowercase, uppercase, and unrelated tokens.
- Existing snippet matcher tests remain green.
- Native Debug and Release CTest pass.
- Managed Release tests remain green.
- Chromium ordering diagnostic remains exact.
- Three post-change pass-through runs do not regress median p95/p99 and are reported without cherry-picking.
- Resource budgets remain green and the slice is committed separately.
