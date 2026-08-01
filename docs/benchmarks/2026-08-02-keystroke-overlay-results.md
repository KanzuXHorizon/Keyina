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

- Native CMake/CTest: 10/10 tests passed.
- Managed host runner: 333/333 tests passed.
- Native resource, tray-resource, typing, clipboard typing, profile reload, callback latency, transform callback latency, and overlay self-tests passed.
- Overlay self-test confirms one-slot pending depth, no active animation timer after hide, and foreground focus preservation.
- Feature-disabled mode performs no overlay rendering or audio initialization.
- Producer display payload uses fixed-capacity storage: 16 visible tokens and 64 UTF-16 code units.
- Per-key sound is disabled by default, generated in memory, played outside the keyboard callback, and uses drop-if-busy playback rather than an accumulating queue.

## Residual considerations

Visual appearance should still be reviewed on representative light, dark, high-contrast, mixed-DPI, and multi-monitor desktops before a public release. The automated renderer tests verify focus, click-through/no-activate behavior, lifecycle, device-loss recovery, and idle timer teardown, but they do not replace subjective animation review on physical displays.
