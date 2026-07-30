# Keyina benchmark protocol

`keyina_bench` measures hot-path latency after a fixed warm-up and emits schema-versioned JSON. It records 100,000 individual samples per case and reports median, p95, p99, maximum nanoseconds, heap allocations per operation, and whether the allocation budget passed. Allocation-aware results use schema version 2 because those required fields were not part of version 1.

## Cases

- `ascii_pass_through`: reset plus one literal ASCII key; only `Process` is timed.
- `letter_modifier`: `a` is prepared outside the timed region, then the second `a` is measured.
- `tone_update`: `a` is prepared outside the timed region, then `s` is measured.
- `complete_word_tieengs` and `complete_word_Vieetj`: reset and compose complete representative Telex words.
- `delayed_modifier_truowcs`: exercise flexible delayed modifiers.
- `backspace_recomposition`: compose a word and reconstruct the previous state after Backspace.
- `guard_protected_url` and `guard_protected_email`: type complete protected tokens and verify literal behavior.
- `valid_syllable_analysis`: analyze a valid Vietnamese syllable.
- `invalid_boundary_restore`: restore a transformed impossible token at a commit boundary.
- `context_guard_64_codepoints`: deterministic classification of the maximum active-token length.

The benchmark executable overrides allocation operators only inside the benchmark process. Common typing cases have an allocation budget of zero after engine warm-up. Rare full-token restoration transitions have a budget of one owned edit allocation. Native unit tests also run a deterministic one-million-event endurance sequence covering ordinary words, delayed modifiers, Backspace, boundaries, resets, technical punctuation, direct Unicode, and maximum-token rollover.

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

A result can pass regression comparison while failing an absolute budget; release evidence must check both. The executable exits with a non-zero status when any native allocation budget fails.

## Managed typing path

`Keyina.Host.Benchmarks` measures the Windows bridge and resident hook layers separately:

- disabled and enabled latency-profiler overhead;
- literal and transformed native-engine bridge calls;
- Unicode injection preparation;
- full literal and transformed hook decision paths with deterministic native fakes.

The Diagnostics page can also measure real local stages while the application is running: foreground context, safety guard, engine processing, input injection, and total callback time. This profiler is opt-in, records only aggregate duration buckets, and never stores typed content.
