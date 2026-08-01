# Native Snippet Overlay Fast Path Verification — 2026-08-01

## Previous callback behavior

`UpdateSnippetOverlay()` ran after every physical key-down. For ordinary typing without a `;k` suggestion prefix it still:

- called `snippet_suggestions(...)`, constructing an empty vector;
- called `HideSnippetOverlay()`;
- after the overlay had been created once, issued `ShowWindow(SW_HIDE)` repeatedly while it was already hidden.

When suggestions were shown, `SetWindowPos(..., SWP_SHOWWINDOW)` was followed by a redundant `ShowWindow(SW_SHOWNOACTIVATE)`.

## Implementation

A pure predicate now centralizes the suggestion-prefix contract:

```cpp
bool IsRuntimeSnippetSuggestionPrefix(
    std::u32string_view token) noexcept;
```

It accepts case-insensitive `;k` prefixes and rejects empty, partial, reversed, and unrelated tokens.

The matcher and Win32 overlay both consume the same predicate. The Win32 callback now returns before suggestion vector construction for every unrelated token.

The runtime also tracks `snippet_overlay_visible_`:

- hide is skipped while already hidden;
- visibility becomes true only after successful `SetWindowPos(..., SWP_SHOWWINDOW)`;
- the redundant second `ShowWindow` is removed;
- visibility resets during window destruction.

No matching, expansion, text, size, positioning, focus, profile, thread, timer, dependency, or allocation contract changed.

## Callback measurements

Three Release pass-through runs before the change:

```text
mean: 150.175 µs, 106.804 µs, 118.384 µs
median mean: 118.384 µs
p95: 262.144 µs in all runs
p99: 1.049 ms in all runs
```

Three Release pass-through runs after the change:

```text
mean: 117.139 µs, 125.415 µs, 118.576 µs
median mean: 118.576 µs
p95: 262.144 µs in all runs
p99: 1.049 ms in all runs
```

The median mean difference is approximately +0.16%, which is neutral within interactive desktop noise. Tail percentiles are unchanged. The optimization is retained because it deterministically removes unnecessary vector construction and redundant visibility calls without a measurable regression.

## Correctness coverage

New native tests verify the pure prefix contract for:

```text
empty token
partial ; token
;k
;K
longer ;k prefix
unrelated ;other
reversed k;
```

Final native unit result:

```text
138/138 passed
```

## Final verification

- Native Debug CTest: `12/12` passed.
- Native Release CTest: `12/12` passed.
- Native unit tests: `138/138` passed.
- Managed Release tests: `310/310` passed.
- Managed Release build: 0 warnings, 0 errors.
- Chromium ordering diagnostic: exact output at 0, 5, and 10 ms; 116/116 marked events and 58/58 injections per delay; zero failures.
- Resident without tray: 2,580,480-byte private working set, 4 threads, 0 thread delta, budget passed.
- Resident with tray: 2,715,648-byte private working set, 4 threads, 0 thread delta, budget passed.

## Interpretation

This is a housekeeping optimization, not a major latency breakthrough. `GetGUIThreadInfo`, hook chaining, scheduling, and `SendInput` still dominate callback timing. The value of this slice is removal of guaranteed useless work and idempotent UI state transitions while preserving all correctness and resource gates.
