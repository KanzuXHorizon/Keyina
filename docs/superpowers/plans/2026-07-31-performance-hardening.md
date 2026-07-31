# Keyina Performance Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add reproducible whole-application performance measurement, optimize only measured bottlenecks, and produce a regression-checked release candidate.

**Architecture:** Extend the existing native benchmark executable for resident hot paths and add a focused managed benchmark/test harness for snippet, command-output and host-process measurements. Store machine-readable results under `artifacts/benchmarks`, compare them against an explicit baseline, then apply small TDD-backed optimizations one subsystem at a time.

**Tech Stack:** C++20/CMake/CTest, .NET 8/C#, WinForms, PowerShell release scripts, JSON/CSV/Markdown reports.

## Global Constraints

- Preserve existing user-visible behavior and configuration compatibility.
- Preserve hidden execution, no elevation, absolute executable validation, bounded output and focus verification for command-output snippets.
- Never inject benchmark text into an unrelated focused application.
- Preserve all unrelated dirty working-tree changes.
- Reject optimizations that cause lost characters, focus mistakes, process leaks, secure-input bypass or speech/translation regressions.
- Record median, p95 and p99 for latency workloads and machine/build metadata for every run.

---

### Task 1: Native benchmark reporting and configurable workloads

**Files:**
- Modify: `benchmarks/engine_benchmark.cpp`
- Modify: `benchmarks/baseline.schema.json`
- Modify: `benchmarks/CMakeLists.txt`
- Test: `tests/engine_flexible_telex_test.cpp`

**Interfaces:**
- Produces: command-line options `--iterations`, `--warmup`, `--output-json`, `--output-csv`, and stable result fields `median_ns`, `p95_ns`, `p99_ns`, `max_ns`, `allocations_per_operation`.

- [ ] Add a failing CTest/CLI check that invokes the benchmark with small iteration counts and verifies JSON and CSV files are created with all required percentile fields.
- [ ] Run the focused benchmark check and confirm it fails because output-path options are unsupported.
- [ ] Refactor argument parsing and report serialization into focused functions without changing existing benchmark workloads.
- [ ] Add build, compiler, OS, processor, timestamp, warmup and iteration metadata to JSON.
- [ ] Run the focused check and native tests.
- [ ] Capture the first 0.1.11 native baseline under `artifacts/benchmarks/0.1.11/`.

### Task 2: Managed benchmark harness for snippets

**Files:**
- Create: `apps/host/Keyina.Host.Benchmarks/Keyina.Host.Benchmarks.csproj`
- Create: `apps/host/Keyina.Host.Benchmarks/Program.cs`
- Create: `apps/host/Keyina.Host.Benchmarks/BenchmarkReport.cs`
- Create: `apps/host/Keyina.Host.Benchmarks/SnippetBenchmarks.cs`
- Modify: `Keyina.slnx`
- Test: `apps/host/Keyina.Host.Tests/SnippetSuggestionTests.cs`

**Interfaces:**
- Produces: `dotnet run --project apps/host/Keyina.Host.Benchmarks -c Release -- --suite snippets --output <directory>`.
- Produces workloads for 10, 100, 1,000 and 10,000 snippets covering exact, prefix, miss and Unicode filtering.

- [ ] Write focused tests for deterministic generated snippets and result percentile calculation.
- [ ] Run tests and confirm the missing benchmark helpers fail compilation.
- [ ] Implement deterministic data generation and allocation/time sampling with warmup.
- [ ] Emit JSON, CSV and Markdown reports with runtime, OS, CPU, process architecture and commit metadata when available.
- [ ] Run managed tests and capture the 0.1.11 snippet baseline.

### Task 3: Managed command-output benchmark and safety regression

**Files:**
- Create: `apps/host/Keyina.Host.Benchmarks/CommandOutputBenchmarks.cs`
- Modify: `apps/host/Keyina.Host.Benchmarks/Program.cs`
- Modify: `apps/host/Keyina.Host.Tests/SnippetCommandOutputTests.cs`
- Modify only if measured: `apps/host/Keyina.Host/Runtime/SnippetCommandOutput.cs`

**Interfaces:**
- Produces workloads for direct executable, PowerShell and CMD cold/warm launch, bounded stdout and timeout.

- [ ] Add regression tests for timeout cleanup, output cap, non-zero exit handling and focus-bound delivery.
- [ ] Run focused tests and verify any newly specified unsupported behavior fails for the intended reason.
- [ ] Add benchmark workloads using harmless local commands only.
- [ ] Measure process creation and output capture separately where possible.
- [ ] Profile results and modify runtime only when a dominant cost is inside Keyina rather than Windows process startup.
- [ ] Run focused tests and capture baseline/after reports.

### Task 4: Resident process resource sampler

**Files:**
- Create: `apps/host/Keyina.Host.Benchmarks/ProcessResourceSampler.cs`
- Create: `apps/host/Keyina.Host.Benchmarks/ResidentBenchmarks.cs`
- Modify: `apps/host/Keyina.Host.Benchmarks/Program.cs`
- Modify only if measured: `platform/windows/input/native_resident.cpp`
- Modify only if measured: `platform/windows/input/win32_input_runtime.cpp`

**Interfaces:**
- Produces startup milliseconds, idle CPU delta, working set, private bytes, thread count and handle count.

- [ ] Add parser/unit tests for resource samples and process lifecycle cleanup.
- [ ] Implement explicit process launch, readiness timeout, idle sampling and guaranteed termination.
- [ ] Ensure the benchmark uses isolated synthetic/self-test modes and never targets the currently focused third-party application.
- [ ] Capture 0.1.11 resident startup and idle baseline.
- [ ] Profile resident hotspots and make only focused, test-backed changes.
- [ ] Re-run native and managed regression suites.

### Task 5: Settings, speech and translation timing probes

**Files:**
- Create: `apps/host/Keyina.Host.Benchmarks/ApplicationBenchmarks.cs`
- Modify: `apps/host/Keyina.Host.Benchmarks/Program.cs`
- Modify only if measured: `apps/host/Keyina.Host/UI/SettingsForm.cs`
- Modify only if measured: `apps/host/Keyina.Host/Speech/DictationCoordinator.cs`
- Modify only if measured: translation companion/runtime files discovered during profiling

**Interfaces:**
- Produces Settings construction/show-ready timing, snippet-list population timing, speech readiness timing and translation-preview timing.

- [ ] Add test seams for clocks/readiness notifications without exposing production-only benchmark UI.
- [ ] Add focused tests verifying timing probes do not alter behavior or swallow failures.
- [ ] Measure Release paths with services stubbed only where real network/API latency would make results nondeterministic.
- [ ] Apply targeted optimization only to costs attributable to local Keyina work.
- [ ] Verify accessibility and keyboard behavior tests remain green.

### Task 6: Baseline comparison and regression gate

**Files:**
- Create: `scripts/windows/compare-benchmarks.ps1`
- Create: `benchmarks/thresholds.json`
- Modify: `scripts/windows/publish.ps1`
- Create: `docs/performance.md`

**Interfaces:**
- Produces a non-zero exit only for statistically/materially meaningful regressions defined in `benchmarks/thresholds.json`.

- [ ] Add script tests or fixture-driven checks for improvement, neutral noise and regression cases.
- [ ] Implement comparison by benchmark name using median and p95, with configurable relative and absolute tolerances.
- [ ] Make benchmark gating opt-in for normal local publish and mandatory for release verification when a baseline is supplied.
- [ ] Document exact commands, report locations and interpretation limits.
- [ ] Run comparison against the captured 0.1.11 baseline.

### Task 7: Final verification and release candidate

**Files:**
- Modify: `CHANGELOG.md`
- Modify: `installer/Keyina.iss`
- Modify: release/version files discovered by `scripts/windows/publish.ps1`

**Interfaces:**
- Produces a new versioned installer, portable ZIP, manifest and SHA-256 file without overwriting 0.1.11 artifacts.

- [ ] Run clean Release build.
- [ ] Run all managed tests and record exact pass count.
- [ ] Run all native tests and record exact pass count.
- [ ] Run native and managed benchmark suites and compare against 0.1.11.
- [ ] Run resource, speech, hotkey, typing and profile-reload self-tests.
- [ ] Build installer and portable archive in a new version directory.
- [ ] Verify manifest fields and SHA-256 checksums.
- [ ] Inspect final diff for unrelated edits, generated noise and secrets before reporting completion.
