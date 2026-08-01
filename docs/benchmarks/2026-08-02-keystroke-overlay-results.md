# Keystroke overlay verification results

Date: 2026-08-02

## Scope

Verification covers the adaptive keystroke overlay, bounded fixed-buffer producer, privacy suppression, no-activate renderer, adaptive placement, runtime profile v3 with v2 compatibility, Settings integration, and optional memory-resident per-key sound.

## Commands

```powershell
cmake --build build/windows-msvc-debug --config Debug --target keyina_tests KeyinaInput
ctest --test-dir build/windows-msvc-debug -C Debug --output-on-failure
dotnet build apps/host/Keyina.Host.Tests/Keyina.Host.Tests.csproj
dotnet run --project apps/host/Keyina.Host.Tests/Keyina.Host.Tests.csproj --no-build
```

## Results

- Native CMake/CTest Debug: 13/13 tests passed.
- Native CMake/CTest Release: 13/13 tests passed.
- Native unit runner: 200/200 tests passed.
- Managed host runner Debug: 333/333 tests passed.
- Managed host runner Release: 333/333 tests passed.
- Release idle resident without tray: 5,185,536 private working-set bytes, 0.000000% CPU, 5 threads, thread delta 0.
- Release idle resident with tray: 5,263,360 private working-set bytes, 0.000000% CPU, 5 threads, thread delta 0.
- Release ordinary callback latency: p50 0.262 ms, p95 0.524 ms, p99 1.049 ms, mean 0.230 ms across 8,192 callback samples.
- Release transformed callback latency: p50 0.262 ms, p95 4.194 ms, p99 4.194 ms, mean 0.380 ms across 4,096 callback samples, with 512/512 successful injections and zero failures.
- Native resource, tray-resource, typing, clipboard typing, profile reload, callback latency, transform callback latency, and overlay self-tests passed.
- Overlay self-test confirms 10 produced events coalesced to one render, pending depth capped at one, immediate privacy suppression, no active animation timer after hide, and foreground focus preservation.
- Feature-disabled mode performs no overlay rendering or audio initialization.
- Producer composition, event, and reducer state use fixed-capacity trivially-copyable storage: 16 visible tokens and 64 UTF-16 code units, without surrogate-pair splitting.
- Per-key sound is disabled by default, generated in memory, played outside the keyboard callback, and uses drop-if-busy playback rather than an accumulating queue.
- Resource measurements wait for one continuous second of stable thread count, so short-lived Windows helper threads do not create false failures while persistent resident threads still fail the one-thread-delta gate.

## Residual considerations

Visual appearance should still be reviewed on representative light, dark, high-contrast, mixed-DPI, and multi-monitor desktops before a public release. The automated renderer tests verify focus, click-through/no-activate behavior, lifecycle, device-loss recovery, and idle timer teardown, but they do not replace subjective animation review on physical displays.
