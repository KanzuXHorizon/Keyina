# Vietnamese Typing Correctness and Performance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make practical Vietnamese Telex input more accurate and keep the typing hot path fast without introducing destructive boundary or backspace behavior.

**Architecture:** Extend the existing native engine and test corpus rather than adding a second transformation layer. Reproduce observed sequences through the public `Engine::Process` interface, correct the earliest transformation/state boundary, then validate with native test and release benchmark lanes.

**Tech Stack:** C++20, CMake/CTest, existing Keyina native test framework and benchmarks.

## Global Constraints

- Preserve unrelated user changes.
- Correctness takes precedence over benchmark improvements.
- Do not capture typed content in diagnostics.
- Do not add background work, focus-stealing UI, or steady-state allocations without evidence.
- Every retained behavior change requires a focused regression test.

---

### Task 1: Baseline and observed-input regressions

**Files:**
- Modify: `tests/engine_flexible_telex_test.cpp`
- Modify as evidence requires: `tests/engine_history_test.cpp`
- Inspect: `core/src/engine.cpp`

**Interfaces:**
- Consumes: `keyina::Engine::Process(const KeyEvent&) -> TextEdit`
- Produces: regression coverage for observed Telex sequences and per-key visible output.

- [ ] **Step 1: Record the current Git diff and run the focused native tests.**

Run: `ctest --preset windows-msvc-debug --output-on-failure -R "engine_flexible_telex|engine_history|vietnamese_syllable"`

Expected: a trustworthy baseline; any failure is recorded before editing.

- [ ] **Step 2: Add failing vectors for observed user input.**

Add exact cases for `muoson`, `gox`, `dodoj`, `dudojcw`, and `haonf`, using the existing `TypeSequence` helper and expected Unicode output.

- [ ] **Step 3: Run the focused tests and confirm each new failure reflects the intended mismatch.**

Run the same focused `ctest` command and inspect the first differing sequence.

- [ ] **Step 4: Inspect the earliest incorrect transformation in `Engine::Process` and its helpers.**

Trace raw keys, visible text, syllable analysis, tone placement, and composition ownership for each failing vector.

### Task 2: Minimal correctness fixes

**Files:**
- Modify: `core/src/engine.cpp`
- Modify only if directly implicated: the corresponding existing core helper file
- Test: `tests/engine_flexible_telex_test.cpp`
- Test: `tests/engine_history_test.cpp`
- Test: `tests/vietnamese_syllable_test.cpp`

**Interfaces:**
- Produces: stable visible text and engine state for flexible Telex, boundary, repeated modifier, and backspace cases.

- [ ] **Step 1: Implement the smallest fix for the first reproduced failure.**

Keep existing public interfaces and bounded composition model.

- [ ] **Step 2: Run the focused test executable or CTest regex and verify the case passes.**

- [ ] **Step 3: Repeat one failure at a time until all observed-input vectors pass.**

- [ ] **Step 4: Add state assertions for boundary and backspace ownership.**

Assert `TextEdit`, `RawKeys()`, and `VisibleText()` consistently represent who owns the deletion/rewrite.

- [ ] **Step 5: Run the complete native debug suite.**

Run: `ctest --preset windows-msvc-debug --output-on-failure`

Expected: 100% pass.

### Task 3: Performance verification and surgical optimization

**Files:**
- Inspect/modify only with evidence: `benchmarks/**`
- Modify only with measured evidence: `core/src/engine.cpp`

**Interfaces:**
- Consumes: existing benchmark targets and engine hot path.
- Produces: repeatable before/after latency evidence with unchanged correctness.

- [ ] **Step 1: Identify and run the existing release benchmark target.**

Use the repository preset/target rather than a global benchmark tool.

- [ ] **Step 2: Capture repeated baseline results for common key, modifier, backspace, and boundary paths.**

- [ ] **Step 3: Inspect allocations/recomputation only if benchmark or code evidence identifies a meaningful hotspot.**

- [ ] **Step 4: Apply at most one surgical optimization and rerun focused tests plus repeated release benchmarks.**

Reject the optimization if results are noisy, correctness changes, or the gain is not meaningful.

### Task 4: Final verification and review

**Files:**
- Review all changed files.

**Interfaces:**
- Produces: a bounded, verified diff and explicit residual-risk report.

- [ ] **Step 1: Run complete debug tests and relevant release tests/benchmarks fresh.**

- [ ] **Step 2: Inspect `git diff --check`, `git diff --stat`, and the full final diff.**

- [ ] **Step 3: Confirm no unrelated files, generated artifacts, input content, or secrets were added.**

- [ ] **Step 4: Report exact commands, pass/fail counts, measured performance, and any remaining uncertainty.**
