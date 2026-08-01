# Performance measurement and regression gates

Keyina performance results are machine-specific evidence, not universal product guarantees. Compare runs on the same computer, Windows build, power mode and background-load conditions.

## Managed benchmark suites

Run the application-local suite in Release mode:

```powershell
 dotnet run --project apps/host/Keyina.Host.Benchmarks/Keyina.Host.Benchmarks.csproj `
   -c Release -- `
   --suite application `
   --output artifacts/benchmarks/current `
   --warmup 3 `
   --iterations 30
```

Available suites are `snippets`, `commands`, `application`, `settings`, `resident` and `all`. The focused `settings` suite measures changed and unchanged 1,000-snippet snapshots without running speech or translation probes. The resident suite additionally requires `--resident <path-to-KeyinaInput.exe>`.

Each output directory contains:

- `managed.json`: machine-readable benchmark document used by the gate;
- `managed.csv`: spreadsheet-friendly measurements;
- `managed.md`: compact human-readable report.

The application suite uses local deterministic seams for speech and translation. It does not call a provider, use a microphone, or type into another application. Provider/network latency must be measured separately and must not be treated as Keyina local processing cost.

## Comparing a baseline

```powershell
 powershell -NoProfile -ExecutionPolicy Bypass `
   -File scripts/windows/compare-benchmarks.ps1 `
   -Baseline artifacts/benchmarks/baseline/managed.json `
   -Current artifacts/benchmarks/current/managed.json `
   -Thresholds benchmarks/thresholds.json
```

The comparator matches cases by exact benchmark `Name` and checks both `MedianNanoseconds` and `P95Nanoseconds`. For each metric, the permitted increase is the larger of:

- baseline value multiplied by the configured relative tolerance;
- configured absolute tolerance in nanoseconds.

This avoids rejecting tiny benchmarks because of timer noise while still detecting material regressions in expensive paths. A missing baseline case in the current report is a failure. Additional current cases are allowed so new probes can be introduced before a new baseline is adopted.

Exit code `0` means all baseline cases remained within their configured limits. Exit code `1` means a regression, missing case, malformed input or invalid threshold configuration was found. Regression output names the benchmark and failed metric.

## Publish integration

Normal local publish remains ungated:

```powershell
 powershell -NoProfile -ExecutionPolicy Bypass `
   -File scripts/windows/publish.ps1
```

Supplying a baseline and current report enables the gate before packaging:

```powershell
 powershell -NoProfile -ExecutionPolicy Bypass `
   -File scripts/windows/publish.ps1 `
   -BenchmarkBaseline artifacts/benchmarks/baseline/managed.json `
   -BenchmarkCurrent artifacts/benchmarks/current/managed.json `
   -BenchmarkThresholds benchmarks/thresholds.json
```

Release verification should additionally require the gate so missing report arguments fail immediately:

```powershell
 powershell -NoProfile -ExecutionPolicy Bypass `
   -File scripts/windows/publish.ps1 `
   -RequireBenchmarkGate `
   -BenchmarkBaseline artifacts/benchmarks/baseline/managed.json `
   -BenchmarkCurrent artifacts/benchmarks/current/managed.json
```

## Current application findings

An earlier five-iteration Release probe after lazy snippet loading established the following local baseline:

| Case | Median | Allocation per operation |
|---|---:|---:|
| Construct Settings with sample data | 22 ms | 2.84 MB |
| Construct Settings with 1,000 snippets | 26 ms | 2.84 MB |
| Apply unchanged 1,000-snippet snapshot | 3 ms | 2.8 KB |
| Rebuild changed 1,000-snippet library | 557 ms | 56.3 MB |
| Stubbed speech start/stop | 0.05 ms | 8 KB |
| Stubbed translation preview | below 0.01 ms | 0.8 KB |

The snippet library now reconciles existing rows by trigger and recycles unmatched custom cards instead of clearing and reconstructing every control. A 50-iteration focused Release run on the same development machine measured:

| Snippet Settings case | Before | Current | Change |
|---|---:|---:|---:|
| Replace 1,000 changed snippets, median | 557 ms | 92.65 ms | about 83% lower |
| Replace 1,000 changed snippets, P95 | not captured in the original five-run baseline | 112.78 ms | current evidence only |
| Replace 1,000 changed snippets, allocation | 56.3 MB | 2.51 MB | about 95.5% lower |
| Apply unchanged 1,000-snippet snapshot, median | 3 ms | 0.0069 ms | cache fast path retained |
| Apply unchanged 1,000-snippet snapshot, allocation | 2.8 KB | 1.78 KB | lower |

Run the focused probe with:

```powershell
 dotnet run --project apps/host/Keyina.Host.Benchmarks/Keyina.Host.Benchmarks.csproj `
   -c Release -- `
   --suite settings `
   --output artifacts/benchmarks/snippet-recycling-current `
   --warmup 10 `
   --iterations 50
```

The remaining limitation is initial construction and simultaneous ownership of 1,000 visible WinForms row control trees. Updates are now incremental and recycled, but a future custom virtualized list could further reduce initial population time and resident UI handles if real users routinely keep libraries at that scale.

## Interpretation limits

- Use at least 30 measured iterations for release evidence; five iterations are suitable only for quick local diagnosis.
- Do not compare Debug and Release results.
- Avoid active builds, antivirus scans, games, browser stress tests and thermal throttling during a run.
- Treat isolated improvements below both tolerances as noise unless repeated across multiple runs.
- Never widen a threshold solely to make a failing release pass. Capture a profile and document the measured reason first.
