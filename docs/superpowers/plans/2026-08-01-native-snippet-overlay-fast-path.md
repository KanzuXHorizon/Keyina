# Native Snippet Overlay Fast Path Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove ordinary-key snippet suggestion work and redundant overlay visibility calls from the native callback.

**Architecture:** Centralize `;k` suggestion-prefix detection in a pure matcher helper, reject unrelated tokens before vector construction, and track overlay visibility so hide/show transitions occur only when state changes.

**Tech Stack:** C++20, Win32 overlay window, CMake/CTest, native callback latency probe.

## Global Constraints

- Preserve snippet matching, expansion, profile, focus, text, size, and positioning behavior.
- No production allocation, thread, timer, dependency, or additional Win32 call.
- Leave unrelated managed benchmark changes unstaged.

---

### Task 1: Add the pure suggestion-prefix contract

**Files:**
- Modify: `platform/windows/input/include/keyina/windows/runtime_snippet_matcher.h`
- Modify: `platform/windows/input/runtime_snippet_matcher.cpp`
- Modify: `tests/windows/runtime_snippet_matcher_test.cpp`

- [x] Add `IsRuntimeSnippetSuggestionPrefix(std::u32string_view) noexcept`.
- [x] Test empty, `;`, `;k`, `;K`, longer valid prefixes, and unrelated input.
- [x] Make `Suggestions()` consume the helper without changing remaining filters.

### Task 2: Add the overlay fast path and visibility state

**Files:**
- Modify: `platform/windows/input/include/keyina/windows/win32_input_runtime.h`
- Modify: `platform/windows/input/win32_input_runtime.cpp`

- [x] Return before suggestion vector construction unless the pure prefix predicate passes.
- [x] Add `snippet_overlay_visible_`.
- [x] Hide only on a visible-to-hidden transition.
- [x] Set visible after successful `SetWindowPos(..., SWP_SHOWWINDOW)`.
- [x] Remove redundant `ShowWindow(SW_SHOWNOACTIVATE)`.
- [x] Reset state on destruction.

### Task 3: Verify and commit

**Files:**
- Create: `docs/benchmarks/2026-08-01-native-snippet-overlay-fast-path.md`

- [x] Run native Debug and Release CTest.
- [x] Run managed Release build/tests.
- [x] Run Chromium ordering diagnostic.
- [x] Run three Release pass-through callback probes and compare all runs.
- [x] Run resource probes.
- [x] Run `git diff --check`, inspect staged scope, and commit separately.
