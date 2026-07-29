# Keyina benchmark protocol

`keyina_bench` measures hot-path latency after a fixed warm-up and emits schema-versioned JSON. It records 100,000 individual samples per case and reports median, p95, p99, and maximum nanoseconds.

## Cases

- `ascii_pass_through`: reset plus one literal ASCII key; only `Process` is timed.
- `letter_modifier`: `a` is prepared outside the timed region, then the second `a` is measured.
- `tone_update`: `a` is prepared outside the timed region, then `s` is measured.
- `guard_protected_url`: the URL prefix is prepared outside the timed region, then the final character is measured.
- `context_guard_64_codepoints`: deterministic classification of the maximum active-token length.

The per-operation clock call is included consistently and therefore places a floor on very small measurements. Results compare only equivalent case names, schema versions, build types, compiler families, and broadly comparable hardware. Benchmarks do not replace correctness tests.

## Run

```powershell
F:\Cmake\bin\cmake.exe --preset windows-msvc-release
F:\Cmake\bin\cmake.exe --build --preset windows-msvc-release
.\build\windows-msvc-release\benchmarks\Release\keyina_bench.exe > $env:TEMP\keyina-benchmark.json
```

Compare a reviewed baseline and a current run:

```powershell
python tools\compare_benchmark.py baseline.json $env:TEMP\keyina-benchmark.json --threshold 0.20
```

A p99 increase greater than 20% returns exit code 1. Machine-specific output is evidence and is not committed as a universal baseline. A baseline becomes release-gating only after its environment and repeatability are reviewed.

## Budgets

The product design budgets remain the absolute gates:

- ASCII pass-through p99 ≤ 10 microseconds.
- Vietnamese transformation p99 ≤ 25 microseconds.
- 64-code-point Context Guard p99 ≤ 20 microseconds.

A result can pass regression comparison while failing an absolute budget; release evidence must check both.
