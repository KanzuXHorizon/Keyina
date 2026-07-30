# Keyina Typing Latency and Extreme Optimization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restore a clean Release baseline, add opt-in allocation-free per-stage typing latency telemetry, display it in Diagnostics, and extend reproducible host benchmarks so later optimizations are driven by evidence.

**Architecture:** Add a fixed logarithmic histogram profiler in `Keyina.Host.Windows`, instrument the managed hook only behind one volatile enabled flag, expose immutable snapshots to the WinForms host, and benchmark both disabled and enabled paths. Keep all formatting, sorting, UI updates, and file output outside the global keyboard callback.

**Tech Stack:** .NET 10, WinForms, `WH_KEYBOARD_LL`, `Stopwatch.GetTimestamp`, lock-free/atomic counters, C++20 native engine benchmark, CMake/MSVC.

## Global Constraints

- Do not record or persist typed text, transcript text, clipboard content, snippets, or document content.
- Profiling is disabled by default and performs no timestamp calls or allocations when disabled.
- Profiler failures must never suppress a physical key or escape the hook callback.
- The hook remains fail-open in secure, elevated, unsupported, or injection-failure contexts.
- Existing Telex golden vectors and native/host tests must remain green.
- No speed claim against UniKey or EVKey without same-machine black-box evidence.
- Preserve all unrelated uncommitted user changes.

---

### Task 1: Restore the current Release build

**Files:**
- Modify: `apps/host/Keyina.Host/UI/Fluent/FluentControls.cs`
- Modify: `apps/host/Keyina.Host/UI/Fluent/FluentTrayRenderer.cs`
- Modify: `apps/host/Keyina.Host/UI/SettingsForm.cs`

**Interfaces:**
- Produces: warning-free implementations of the current Fluent controls and Settings surface.
- Consumes: existing WinForms override contracts and the current screenshot/test model.

- [ ] **Step 1: Reproduce the build failure**

Run:

```bat
cmd.exe /c dotnet build Keyina.slnx -c Release --nologo
```

Expected: failure at the current nullable/analyzer violations and missing `SnippetRows` symbol.

- [ ] **Step 2: Trace each error to the earliest incorrect source**

Read the complete override signatures, tray renderer null flow, and snippet table implementation. Confirm whether `SnippetRows` was renamed, deleted, or intended to be local data before editing.

- [ ] **Step 3: Apply the smallest mechanical corrections**

Rename override parameters to match base signatures, make helper methods static or concrete where required by analyzers, guard nullable `ToolStrip`/item references at their source, and replace the missing snippet source with the existing canonical snippet-row data.

- [ ] **Step 4: Verify the solution builds**

Run the Release build command again.

Expected: zero warnings and zero errors.

### Task 2: Add the latency histogram profiler with TDD

**Files:**
- Create: `apps/host/Keyina.Host.Windows/Typing/TypingLatencyProfiler.cs`
- Create: `apps/host/Keyina.Host.Tests/TypingLatencyProfilerTests.cs`
- Modify: `apps/host/Keyina.Host.Tests/Program.cs` only if discovery requires no change; tests use the existing attribute discovery.

**Interfaces:**
- Produces: `TypingLatencyStage`, `TypingLatencySnapshot`, `TypingLatencyStageSnapshot`, and static `TypingLatencyProfiler` methods:
  - `bool IsEnabled { get; }`
  - `void SetEnabled(bool enabled)`
  - `long Start()` returning `0` when disabled
  - `void Record(TypingLatencyStage stage, long startedAt)`
  - `IReadOnlyList<TypingLatencyStageSnapshot> Snapshot()`
  - `void Clear()`
- Percentiles are approximate upper bounds from fixed logarithmic nanosecond buckets.

- [ ] **Step 1: Write failing tests**

Add tests proving:

```csharp
TypingLatencyProfiler.SetEnabled(false);
TypingLatencyProfiler.Clear();
var start = TypingLatencyProfiler.Start();
TypingLatencyProfiler.Record(TypingLatencyStage.CallbackTotal, start);
AssertEx.Equal(0L, TypingLatencyProfiler.Snapshot().Single(x => x.Stage == TypingLatencyStage.CallbackTotal).SampleCount);
```

Also add enabled recording, percentile monotonicity, and clear-without-disable tests.

- [ ] **Step 2: Run focused tests and verify RED**

Run:

```bat
cmd.exe /c dotnet run --project apps/host/Keyina.Host.Tests/Keyina.Host.Tests.csproj -c Release
```

Expected: compile failure because profiler types do not exist.

- [ ] **Step 3: Implement the minimal fixed histogram**

Use preallocated per-stage counter arrays, `Interlocked.Increment`, `Interlocked.Add`, `Interlocked.CompareExchange` for maximum, and `Stopwatch.GetTimestamp()` only when enabled. Do not store individual samples.

- [ ] **Step 4: Run focused tests and verify GREEN**

Expected: all profiler tests pass and no existing tests regress.

### Task 3: Instrument the keyboard hook with TDD

**Files:**
- Modify: `apps/host/Keyina.Host.Windows/Typing/VietnameseKeyboardHook.cs`
- Modify: `apps/host/Keyina.Host.Tests/VietnameseKeyboardHookTests.cs`

**Interfaces:**
- Consumes: `TypingLatencyProfiler`.
- Produces stage samples for `CallbackTotal`, `ForegroundContext`, `SafetyGuard`, `EngineProcess`, and `InputInjection`.

- [ ] **Step 1: Write failing hook tests**

Add deterministic tests that enable and clear profiling, dispatch a literal event and a transforming event through the existing fake native API, and assert:

- literal path records callback/context/guard/engine but no injection sample;
- transform path records callback/context/guard/engine/injection;
- disabled profiling records no samples.

- [ ] **Step 2: Run tests and verify RED**

Expected: stage sample counts remain zero because the hook is not instrumented.

- [ ] **Step 3: Add guarded timing scopes**

Read `TypingLatencyProfiler.IsEnabled` once at callback entry. When false, execute the existing path without timestamp calls. When true, capture stage start ticks around the existing boundaries and record in `finally` for callback total. Never format strings or build reports inside the callback.

- [ ] **Step 4: Run tests and verify GREEN**

Expected: new hook tests and all existing host tests pass.

### Task 4: Expose latency metrics in Diagnostics

**Files:**
- Modify: `apps/host/Keyina.Host/UI/SettingsModels.cs`
- Modify: `apps/host/Keyina.Host/Runtime/KeyinaApplicationContext.cs`
- Modify: `apps/host/Keyina.Host/UI/SettingsForm.cs`
- Modify: `apps/host/Keyina.Host.Tests/SettingsFormTests.cs`

**Interfaces:**
- Extend `SettingsActions` with:
  - `Action<bool> SetTypingLatencyEnabled`
  - `Func<IReadOnlyList<TypingLatencyStageSnapshot>> GetTypingLatencySnapshot`
  - `Action ClearTypingLatency`
- Diagnostics refreshes only on explicit user action.

- [ ] **Step 1: Write failing UI tests**

Assert the Diagnostics page contains named controls for enabling profiling, refreshing the latency table, clearing metrics, and rendering stage rows without exposing typed content.

- [ ] **Step 2: Run tests and verify RED**

Expected: controls are missing.

- [ ] **Step 3: Implement the Diagnostics card**

Add a compact table with columns `Stage`, `Samples`, `P50`, `P95`, `P99`, `Max`, and `Mean`; a toggle; refresh and clear buttons; and a privacy explanation. Use microseconds for values >= 1,000 ns and nanoseconds below that.

- [ ] **Step 4: Wire runtime actions**

Connect actions directly to `TypingLatencyProfiler` without background timers or network calls.

- [ ] **Step 5: Run host tests and Release build**

Expected: all tests pass and build remains warning-free.

### Task 5: Extend managed typing benchmarks

**Files:**
- Modify: `apps/host/Keyina.Host.Benchmarks/Program.cs`
- Create or modify test doubles under: `apps/host/Keyina.Host.Benchmarks/`

**Interfaces:**
- Produces JSON benchmark cases:
  - `typing_profiler_disabled_start`
  - `typing_profiler_enabled_record`
  - `typing_native_engine_literal`
  - `typing_native_engine_transform`
- Each case reports p50, p95, p99, maximum, allocated bytes per operation, and a regression budget.

- [ ] **Step 1: Add benchmark cases with conservative regression gates**

The disabled profiler case must allocate `0` bytes per operation. Enabled recording must also allocate `0` bytes per operation after warmup. Native bridge cases use a long-lived engine instance and reset it between samples.

- [ ] **Step 2: Build and run Release benchmark**

Run:

```bat
cmd.exe /c dotnet run --project apps/host/Keyina.Host.Benchmarks/Keyina.Host.Benchmarks.csproj -c Release
```

Expected: valid JSON and every budget passes.

- [ ] **Step 3: Run the benchmark at least three times**

Record the median p99 from the three runs in the implementation report. Do not select only the best run.

### Task 6: Expand native benchmark visibility

**Files:**
- Modify: `benchmarks/engine_benchmark.cpp`
- Modify: `benchmarks/baseline.schema.json` only if the schema changes.

**Interfaces:**
- Adds representative complete-token cases while preserving schema compatibility.

- [ ] **Step 1: Add cases for complete Telex words and backspace reconstruction**

Measure at least `tieengs`, `Vieetj`, a delayed modifier word, a protected URL/email token, and backspace recomposition.

- [ ] **Step 2: Build and run Release native benchmark**

Run:

```bat
cmd.exe /c cmake --build --preset windows-msvc-release
cmd.exe /c build\windows-msvc-release\benchmarks\Release\keyina_bench.exe
```

Expected: valid JSON with all existing and new cases.

### Task 7: Optimize the largest measured bottleneck

**Files:**
- Determined by Task 5 and Task 6 evidence; likely one of:
  - `core/src/engine.cpp`
  - `core/include/keyina/engine.h`
  - `apps/host/Keyina.Host.Windows/Typing/NativeEngineClient.cs`
  - `apps/host/Keyina.Host.Windows/Typing/UnicodeInputInjector.cs`

**Interfaces:**
- Must preserve current public behavior and bridge ABI unless a separate migration test is added.

- [ ] **Step 1: Select exactly one bottleneck from measured p99/allocation data**

Write the hypothesis in the implementation report, including the before measurement and why the selected code boundary causes it.

- [ ] **Step 2: Add a failing regression or stricter benchmark gate**

The check must fail against the current implementation for the intended reason.

- [ ] **Step 3: Apply the smallest optimization**

Examples allowed only when supported by evidence: reuse a preallocated buffer, remove per-key string formatting, avoid repeated process lookup, or replace a full-token recomputation with an incremental state update.

- [ ] **Step 4: Verify correctness and repeat benchmarks three times**

Keep the optimization only if the median p99 improves and allocations do not increase.

### Task 8: Final verification

**Files:**
- Modify: `README.md` or `docs/` only where user-facing diagnostics need documentation.

- [ ] **Step 1: Run full Release build and host tests**
- [ ] **Step 2: Run native Debug and Release tests**
- [ ] **Step 3: Run managed and native Release benchmarks**
- [ ] **Step 4: Inspect `git diff --check` and final status**
- [ ] **Step 5: Confirm no raw keystrokes, text content, clipboard data, secrets, or network telemetry were added**
- [ ] **Step 6: Report measured results, skipped manual compatibility checks, and remaining risks truthfully**
