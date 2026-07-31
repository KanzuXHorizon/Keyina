# Keyina Reliability Hardening Results — 2026-07-31

## Scope

This report records verification performed against the current uncommitted checkout on Windows 10.0.26200, x64, with .NET SDK 10.0.302, MSVC 19.44, and optional TSF coverage enabled.

The verified scope is:

- Vietnamese Telex correctness and literal-Latin restoration.
- Backspace reconstruction and delayed Telex key orders.
- Managed/native runtime-profile parity.
- Deterministic keyboard-hook behavior without driving the shared desktop.
- Clipboard, speech-command, and selection-translation logic through headless adapters.
- Debug and Release builds, tests, resource probes, and benchmarks.
- Published Windows bundle self-tests.

The checkout contained concurrent uncommitted work during verification. The unsigned local 0.1.6 package was installed for an in-place upgrade check; no credential change or remote write was performed.

## Result summary

| Gate | Result | Evidence |
|---|---:|---|
| Native Debug build and CTest | Pass | 9/9 tests |
| Native Release build and CTest | Pass | 9/9 tests |
| .NET Debug build | Pass | 0 warnings, 0 errors |
| .NET Release build | Pass | 0 warnings, 0 errors |
| Managed Debug headless suite | Pass | 285/285 tests |
| Managed Release headless suite | Pass | 285/285 tests |
| WinForms/live-hook interactive lane | Disabled | Permanently excluded by the test runner |
| Golden Telex vectors | Pass | 108 vectors |
| Benchmark comparator tests | Pass | 7/7 tests |
| Native Release benchmark budgets | Pass | All cases within allocation budgets |
| Managed Release benchmark budgets | Pass | All cases within p99 and allocation budgets |
| Native published-bundle self-tests | Pass | Startup, typing, resource, tray-resource, profile reload |
| Managed published-bundle self-tests | Pass | Host, offline speech, hotkeys, resource |
| Linux Clang ASan/UBSan | Not executed | Linux shell did not have `cmake` installed |

## Correctness evidence

The engine and hook regression suites verify the following representative behavior:

| Raw input | Expected output | Result |
|---|---|---:|
| `nguyeenx` | `nguyễn` | Pass |
| `nguyeexn` | `nguyễn` | Pass |
| `ngueenx` | `nguễn` | Pass; no spelling autocorrection |
| `nguyenx` | `nguyẽn` | Pass; composition remains editable |
| Backspace after `nguyenx`, then `e` | `nguyên` | Pass |
| `truocws`, `truowcs`, `truwocs`, `truowsc`, `truocsw` | `trước` | Pass |
| `search`, `research`, `powershell`, `browser`, `source`, `windows` | Literal physical text | Pass |
| URLs, paths, identifiers, commands, repeated-key escape | Literal or protected behavior | Pass |

Word boundaries remain non-destructive: Space and punctuation commit the current visible composition and do not autocorrect already visible text.

## Runtime-profile parity

Managed and native tests use the same 36-byte default profile vector:

```text
4B4952500224110602030001052000055600055400055A00001B000001000000B6CD5DCA
```

Verified properties:

- `RestoreInvalidWord` is published by the managed codec.
- Native default and decoded profiles set `restore_invalid_word = true`.
- Checksum, version, reserved-byte, flag, and hotkey corruption are rejected.
- Atomic runtime reload preserves `restore_invalid_word` while changing the Vietnamese-enabled state.

## Build-system hardening

MSVC compilation now uses `/FS` for configured native targets. This serializes access to the compiler program database and prevents parallel Debug builds from failing with C1041 when multiple test sources write `vc143.pdb` concurrently. Debug and Release builds both passed after this change; warnings remain errors.

## Windows integration evidence

Interactive WinForms and live keyboard-hook tests were used during investigation, then permanently excluded from the final test runner because they can steal focus and inject input into the shared desktop. Passing the legacy `--interactive` argument cannot re-enable them. The final release gate uses deterministic headless coverage.

Historical investigation covered:

- `WH_KEYBOARD_LL` remains responsive while the owner UI thread is blocked.
- Vietnamese typing into a focused multiline WinForms `TextBox` without TSF across all five delayed-Telex variants.
- A deterministic 20-round burst matrix in the resident-hook test lane, independent of the shared desktop.
- Exact synthetic key observation through a test-only `ExtraInfo` marker and the production physical-event observer, rather than inference from a global event counter.
- Per-key UI acknowledgement based on the expected engine edit, rather than a fixed sleep as delivery proof.
- Word-sized live chunks reacquire the test target before each operation, preventing unrelated foreground activity from being misclassified as a Keyina text defect.
- Selection replacement and `Ctrl+V` behavior.
- Password-state refresh on the same focused HWND.
- Literal and transformed edits serialized through injection without dropped or duplicated keys.
- Pointer-reset behavior remains enabled in production; no test-only production switch remains.
- Live clipboard selection capture in a real focused textbox.
- Command companion speech start/cancel in a real focused textbox.
- Command companion selection translation in a real focused textbox.
- Live configured DeepL and Speechmatics production-adapter tests completed successfully without printing credentials or selected/transcript content.

The final deterministic Debug and Release suites each passed 285/285. A legacy combined interactive run lost one marked synthetic key while sharing the foreground desktop with Edge; this desktop interference is why interactive tests are no longer part of the runnable gate. No product timeout or retry budget was widened to hide it.

## Resident lifecycle evidence

The installed runtime profile contained the built-in `;kvi` and `;kvoice` commands, but both appeared as literal text when the native resident was not running. The failure was traced to two lifecycle gaps:

- Silent install or upgrade skipped the resident launch because the Inno Setup `[Run]` entry used `skipifsilent`.
- Opening the Settings companion did not repair a missing native resident.

The installer now starts the resident after silent upgrades, and the Settings companion best-effort launches its sibling `KeyinaInput.exe` when the resident mutex is absent. Contract tests cover both paths. The installed resident was restarted from the 0.1.6 installation directory after diagnosis.

## Resource evidence

### Published native resident

Clean published-bundle probes reported:

| Mode | Private memory | Thread delta | Input contamination | Result |
|---|---:|---:|---:|---:|
| No tray | 2,809,856 bytes | 0 | No | Pass |
| Tray enabled | 3,067,904 bytes | 0 | No | Pass |

The 10 MiB private-memory gate remained unchanged.

The resource harness now waits for the process thread count to complete cold startup and remain stable before taking its baseline. This prevents a delayed runtime startup thread from being misclassified as a thread created by the resident. The final measurement still requires zero resident thread growth.

### Published managed host probe

A clean published probe reported:

- Private memory: 10,166,272 bytes, below the 10,485,760-byte budget.
- Thread count: 16, delta `+1`, within the existing managed-host gate.
- Hook running: true.
- Processed physical events: 0.
- Measurement contamination: false.
- Result: pass.

Repeated local probes also produced clean passes around 9.55–9.67 MiB. Samples containing physical keyboard activity were rejected as contaminated rather than counted as passes.

## Performance evidence

### Native engine

Representative Release results from 100,000 measured iterations:

| Case | Median | p95 | p99 | Allocation result |
|---|---:|---:|---:|---:|
| ASCII pass-through | 100 ns | 100 ns | 100 ns | 0 allocations |
| Complete `tieengs` | 1.3 µs | 2.2 µs | 2.6 µs | 0 allocations |
| Delayed `truowcs` | 2.5 µs | 4.0 µs | 4.7 µs | 0 allocations |
| Backspace recomposition | 1.7 µs | 2.8 µs | 3.6 µs | 0 allocations |
| 64-codepoint Context Guard | 200 ns | 300 ns | 300 ns | 0 allocations |

`invalid_boundary_restore` showed stable median/p95 near 12.5/22 µs but noisy p99 outliers. A detached `HEAD` worktree on the same machine showed equal or larger p99 outliers, while median and p95 were equivalent. This does not indicate a regression in the current engine change.

### Managed typing path

Representative Release results:

| Case | p99 | Allocated bytes/op | Budget | Result |
|---|---:|---:|---:|---:|
| Native engine literal | 600 ns | 0 | 20 µs / 0 B | Pass |
| Native engine transform | 1.1 µs | 0 | 30 µs / 0 B | Pass |
| Hook disabled | 100 ns | 0 | 1 µs / 0 B | Pass |
| Hook literal | 700 ns | 0 | 20 µs / 0 B | Pass |
| Hook transform | 1.6 µs | 0 | 30 µs / 0 B | Pass |

All managed benchmark cases passed their p99 and allocation budgets.

## Published bundle

`scripts/windows/publish.ps1` completed and produced `artifacts/publish/win-x64` containing the native resident, managed companion, engine DLL, dependencies, and tray assets.

The following commands passed from the published directory:

```text
KeyinaInput.exe --self-test
KeyinaInput.exe --typing-self-test
KeyinaInput.exe --resource-self-test
KeyinaInput.exe --tray-resource-self-test
KeyinaInput.exe --profile-reload-self-test
Keyina.Host.exe --self-test
Keyina.Host.exe --speech-self-test
Keyina.Host.exe --hotkey-self-test
Keyina.Host.exe --resource-self-test
```

## Commands executed

```powershell
cmake --preset windows-msvc-debug -DKEYINA_BUILD_TSF=ON
cmake --build --preset windows-msvc-debug
ctest --preset windows-msvc-debug --output-on-failure

dotnet build Keyina.slnx -c Debug
apps/host/Keyina.Host.Tests/bin/Debug/net10.0-windows10.0.19041.0/Keyina.Host.Tests.exe

python tools/check_vectors.py
python tools/test_compare_benchmark.py

cmake --preset windows-msvc-release -DKEYINA_BUILD_TSF=ON
cmake --build --preset windows-msvc-release
ctest --preset windows-msvc-release --output-on-failure

dotnet build Keyina.slnx -c Release
apps/host/Keyina.Host.Tests/bin/Release/net10.0-windows10.0.19041.0/Keyina.Host.Tests.exe

build/windows-msvc-release/benchmarks/Release/keyina_bench.exe
dotnet run --project apps/host/Keyina.Host.Benchmarks/Keyina.Host.Benchmarks.csproj -c Release --no-build

powershell -NoProfile -ExecutionPolicy Bypass -File scripts/windows/publish.ps1
```

## Not executed and residual risk

The following targets were not marked pass because this session did not have safe, deterministic desktop automation for them:

- Chromium or Edge editable controls.
- VS Code editor and integrated terminal.
- Windows Terminal, PowerShell console, and Command Prompt windows.
- Microsoft Office applications.
- Elevated applications, Remote Desktop, accessibility software, and clean-VM installer/upgrade/uninstall scenarios.
- Linux Clang ASan/UBSan, because the available Linux shell lacked `cmake`.

Literal `powershell`, command switches, paths, identifiers, URLs, paste, focus changes, password bypass, Backspace, and pointer-reset behavior are covered by engine, hook, and real Win32-control tests, but application-specific compatibility remains a release-candidate manual matrix requirement.

No trusted code-signing identity was configured. Published local artifacts remain unsigned and must not be represented as trusted public release artifacts.
