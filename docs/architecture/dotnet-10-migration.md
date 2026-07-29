# .NET 10 migration evidence

**Date:** 2026-07-29

**Environment:** Windows build 26200, x64, 16 logical processors, .NET SDK 10.0.301, .NET runtime 10.0.10

## Decision

Keyina host projects target .NET 10 LTS. The native TSF DLL remains C++20 and does not load the CLR.

The migration keeps the minimum Windows target at `10.0.19041.0`; it does not reduce the supported Windows application surface. .NET 11 preview is intentionally excluded from production builds.

Reasons:

- .NET 10 is the current LTS line and receives support through November 2028.
- .NET 8 reaches end of support in November 2026.
- The .NET 10 runtime provides current JIT, stack-allocation, devirtualization, networking, JSON, diagnostics, WinForms accessibility, and dark-mode improvements.
- Keyina's measured speech, audio, transcript, and IPC paths improved or stayed neutral.
- The native typing hot path is unchanged and remains isolated from .NET, WebSocket, microphone, UI, and package dependencies.

## Before and after

The following values were collected with the same repository benchmark and machine immediately before and after changing target frameworks.

| Case | .NET 8 p99 | .NET 10 p99 | Result |
|---|---:|---:|---|
| Speechmatics final JSON parse | 7.9 µs | 5.0 µs | Improved |
| Partial transcript aggregation | 0.6 µs | 0.5 µs | Improved |
| 30 ms 48 kHz stereo audio conversion | 15.0 µs | 12.0 µs | Improved |
| Final transcript IPC encode | 0.3 µs | 0.3 µs | Neutral |

Allocations remained within the existing budgets:

- Protocol parse: approximately 256 bytes/operation.
- Partial aggregation: 256 bytes/operation.
- Audio conversion: approximately 2,041 bytes/30 ms input block.
- IPC encode: approximately 72 bytes/operation.

### Idle host process

| Metric | .NET 8 | .NET 10 |
|---|---:|---:|
| Working set | 21,450,752 bytes | 22,081,536 bytes |
| Private memory | 6,871,184 bytes | 6,860,800 bytes |
| Managed heap | 110,504 bytes | 102,424 bytes |
| Threads | 12 | 12 |
| Average CPU over ~3 seconds | 0% | 0% |

The approximately 0.6 MiB working-set increase is accepted because private memory did not increase materially, managed memory decreased, CPU remained idle, and measured hot paths improved.

## Verification

Fresh post-migration verification:

```powershell
dotnet restore Keyina.slnx --force-evaluate
dotnet build Keyina.slnx -c Debug
dotnet run --project apps/host/Keyina.Host.Tests/Keyina.Host.Tests.csproj -c Debug --no-build
dotnet build Keyina.slnx -c Release
dotnet run --project apps/host/Keyina.Host.Tests/Keyina.Host.Tests.csproj -c Release --no-build
dotnet run --project apps/host/Keyina.Host/Keyina.Host.csproj -c Release --no-build -- --speech-self-test
dotnet run --project apps/host/Keyina.Host/Keyina.Host.csproj -c Release --no-build -- --hotkey-self-test
dotnet run --project apps/host/Keyina.Host/Keyina.Host.csproj -c Release --no-build -- --resource-self-test
dotnet run --project apps/host/Keyina.Host.Benchmarks/Keyina.Host.Benchmarks.csproj -c Release --no-build
```

Result: 87/87 host tests passed, both self-tests passed, all benchmark budgets passed, and builds completed with zero warnings and zero errors.

## Deliberate exclusions

- No preview runtime is used.
- NativeAOT and trimming are not enabled for the resident WinForms host until WinForms, NAudio, COM/PInvoke, reflection, localization, updater, and accessibility paths have dedicated publish tests.
- Framework-dependent development builds remain available. Release packaging must explicitly compare self-contained and framework-dependent artifacts before choosing the installer payload.
