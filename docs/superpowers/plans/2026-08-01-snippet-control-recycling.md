# Snippet Control Recycling Implementation Plan

> **For agentic workers:** Use `superpowers:executing-plans` or `superpowers:subagent-driven-development` to execute this plan task by task.

**Goal:** Replace full WinForms snippet-list rebuilds with exact-match reuse and custom-card recycling so 1,000 changed snippets no longer allocate and construct 1,000 new control trees.

**Architecture:** Keep `SettingsForm`, `FlowLayoutPanel`, and the current Fluent card UI. Split card construction from card rebinding, reconcile desired ordered rows against existing controls, and make action handlers resolve the current `SnippetRow` from `card.Tag` instead of closing over stale models.

**Tech Stack:** .NET 10, WinForms, Keyina Fluent controls, custom `[KeyinaTest]` runner, managed benchmark harness.

## Constraints

- Preserve the current Settings UI, snippet model, validation, persistence actions, filters, responsive layout, accessibility names, and keyboard behavior.
- Built-in rows must not be recycled into custom rows.
- Do not add a thread, timer, animation, package, network request, or custom-drawn virtual list.
- Controls may be created or disposed only when the required row count or shape changes.
- Removed surplus controls must be disposed.
- Do not commit or push without explicit user authorization.

## Task 1: Establish the measured baseline

**Files:**
- `apps/host/Keyina.Host/UI/SettingsForm.cs`
- `apps/host/Keyina.Host.Benchmarks/ApplicationBenchmarks.cs`
- `apps/host/Keyina.Host.Benchmarks/Program.cs`

- Preserve lazy snippet creation and unchanged-snapshot caching.
- Keep benchmark name `application_settings_apply_1000_snippets` for before/after comparison.
- Add a focused `settings` suite that runs changed and unchanged 1,000-snippet snapshots.
- Record the original local baseline: approximately 557 ms median and 56.3 MB allocation per changed snapshot.

## Task 2: Prove card identity is destroyed before the change

**File:** `apps/host/Keyina.Host.Tests/SnippetControlRecyclingTests.cs`

- Add a failing test for a complete 1,000-to-1,000 replacement.
- Add a failing test for changed content under unchanged triggers.
- Assert control reference identity, visible trigger order, and rebound expansion text.

## Task 3: Split stable construction from rebinding

**File:** `apps/host/Keyina.Host/UI/SettingsForm.cs`

- `CreateSnippetRow` creates the three- or six-column tree once.
- Child controls keep stable role names.
- `UpdateSnippetRow` updates only changed card metadata and label text.
- Button handlers resolve the current row by walking to the owning `FluentCard.Tag`.
- Wrap label updates in one suspended layout transaction per card.

## Task 4: Reconcile and recycle

**File:** `apps/host/Keyina.Host/UI/SettingsForm.cs`

- Materialize desired rows once.
- Reserve exact trigger matches using `StringComparer.OrdinalIgnoreCase`.
- Queue unmatched custom cards for recycling.
- Create a card only when no exact or recycled card is available.
- Remove and dispose only surplus cards.
- Restore desired ordering without clearing the collection.
- Preserve scroll position and active filtering.
- Mark lazy snippet rows clean only after successful reconciliation.

## Task 5: Remove measured secondary costs

**File:** `apps/host/Keyina.Host/UI/SettingsForm.cs`

- Cache static labels by name instead of recursively searching the full control tree on each snapshot.
- Do not assign child widths when the calculated width is unchanged.
- Skip filter traversal when the query is empty and the filter is `Tất cả`.
- Do not assign label text or accessibility text when the value is unchanged.

## Task 6: Regression and endurance coverage

**File:** `apps/host/Keyina.Host.Tests/SnippetControlRecyclingTests.cs`

Cover:

- complete replacement recycling;
- same-trigger content updates;
- one-row add and remove with disposal of only the delta;
- active filter preservation;
- repeated replacement stability;
- current-row resolution for edit, duplicate, and delete actions.

## Task 7: Measure and document

**Files:**
- `apps/host/Keyina.Host.Benchmarks/ApplicationBenchmarks.cs`
- `apps/host/Keyina.Host.Benchmarks/Program.cs`
- `docs/performance.md`

Run:

```powershell
 dotnet run --project apps/host/Keyina.Host.Benchmarks/Keyina.Host.Benchmarks.csproj `
   -c Release -- `
   --suite settings `
   --output artifacts/benchmarks/snippet-recycling-current `
   --warmup 10 `
   --iterations 50
```

Expected local evidence after implementation:

- changed 1,000-snippet median near 93 ms;
- changed allocation near 2.51 MB/op;
- unchanged snapshot remains on the microsecond cache fast path.

Do not encode machine-specific timing assertions in CI.

## Task 8: Full verification and deployment

Run on the actual checkout:

```powershell
 dotnet build Keyina.slnx -c Release
 dotnet run --project apps/host/Keyina.Host.Tests/Keyina.Host.Tests.csproj -c Release
 cmake --build --preset windows-msvc-release --config Release
 ctest --test-dir build/windows-msvc-release -C Release --output-on-failure
 git diff --check
```

Then:

- run the Impeccable detector over the changed UI source;
- inspect status and remove only RID-generated lockfile noise;
- publish with `scripts/windows/publish.ps1`;
- start exactly one `artifacts/publish/win-x64/KeyinaInput.exe`;
- verify path, resident count, working set, private bytes, and handles;
- do not commit or push without explicit authorization.
