# Keyina Performance Hardening Design

## Goal

Establish reproducible performance baselines for Keyina 0.1.11, optimize only profiler-proven bottlenecks, preserve existing behavior and compatibility, and produce a verified release candidate.

## Scope and order

1. Resident typing path: startup, idle CPU/RAM, keyboard-event latency, allocations, handles and threads.
2. Snippet and external-command output: lookup, filtering, overlay presentation, process launch, timeout, output limits and focus safety.
3. Application-wide paths: Settings startup/navigation, speech start/stop, translation preview, publish size and installer size.

## Strategy

Use a balanced approach. Internal refactoring is allowed when measured evidence supports it, but user-visible behavior, existing configuration formats and safety invariants must remain compatible. Every optimization requires a before/after benchmark and focused regression coverage.

## Benchmark model

Benchmarks use fixed workloads, warmup runs and repeated samples. Reports include median, p95 and p99 latency where applicable, plus working set, private bytes, CPU time, allocation counts, thread count and handle count where the platform exposes them. Results are emitted as JSON, CSV and Markdown so they can be compared automatically and inspected manually.

The baseline is the current 0.1.11 working tree. Machine and build metadata are recorded with each run. Cold-start and warm-start measurements are separated.

## Workloads

### Resident

Measure native engine operations, hook startup, resident process idle resources, profile reload and sustained synthetic keyboard-event processing. No benchmark may inject into an unrelated focused application.

### Snippets

Measure exact match, prefix match, misses and Unicode against 10, 100, 1,000 and 10,000 snippets. Include overlay filtering and update latency. Larger 100,000-item stress data is used only for scalability diagnostics, not as a normal product requirement.

### Command output

Measure direct executable launch and PowerShell/CMD launch, small and bounded large stdout, timeout, cancellation and focus-change abort. Hidden execution, no elevation, absolute executable validation and output caps remain mandatory.

### Application

Measure Settings startup and navigation, snippet list rendering, speech start/stop readiness, translation preview and packaging output size. UI measurements must not change accessibility, keyboard navigation or DPI behavior.

## Acceptance criteria

Targets are directional rather than forced: resident private memory should improve by about 10% where a real hotspot exists; hook startup by about 15%; snippet lookup/overlay by about 20%; command-output cold start by about 15%. A change is rejected if it introduces reliability regressions even when it meets a speed target.

No accepted change may cause lost characters, incorrect focus delivery, secure-input bypass, process leaks, configuration incompatibility, speech/translation regression or installer breakage.

## Verification

Each vertical slice runs its focused tests first, then managed Release tests, native Release tests and relevant resource checks. Final verification includes Release build, managed/native suites, benchmark comparison, resource self-test, packaging verification, manifest and SHA-256 validation. Existing unrelated working-tree changes must be preserved.
