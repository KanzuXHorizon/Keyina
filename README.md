<p align="center">
  <img src="brand/generated/lockup/keyina-lockup-1680x512.png" alt="Keyina" width="760">
</p>

<p align="center">
  A privacy-first Vietnamese input method for Windows, built around a native C++ engine and a safe resident keyboard hook.
</p>

<p align="center">
  <a href="https://github.com/KanzuXHorizon/Keyina/actions/workflows/ci.yml"><img alt="CI" src="https://github.com/KanzuXHorizon/Keyina/actions/workflows/ci.yml/badge.svg"></a>
  <a href="LICENSE"><img alt="Apache-2.0 license" src="https://img.shields.io/badge/license-Apache--2.0-blue.svg"></a>
  <img alt="Windows 10 and 11" src="https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D4">
  <img alt="C++20" src="https://img.shields.io/badge/C%2B%2B-20-00599C">
  <img alt=".NET 10" src="https://img.shields.io/badge/.NET-10-512BD4">
  <img alt="Development status" src="https://img.shields.io/badge/status-public%20preview-orange">
</p>

> [!IMPORTANT]
> Keyina is an active **public preview**. The source, tests, benchmarks, and Windows host are available, but a signed installer and stable compatibility promise are not. Build from source for evaluation; do not treat the current branch as a production release.

## Why Keyina

Keyina is a clean-room Vietnamese input platform focused on behavior that can be measured and tested rather than recreating another application's interface.

- **No `Win + Space` requirement** — the default path is a resident keyboard hook, similar to familiar Vietnamese input utilities.
- **Reversible composition** — transformations preserve enough state to reconstruct the original physical keystrokes.
- **Context Guard** — deterministic protection for source code, URLs, email addresses, commands, paths, identifiers, and English-heavy tokens.
- **Offline ordinary typing** — the native typing path does not need a network connection, cloud model, clipboard replacement, or telemetry.
- **Fail-open safety** — secure, elevated, unsupported, and uncertain contexts fall back to literal physical input.
- **Optional productivity layer** — tray controls, hotkeys, snippets, diagnostics, and opt-in Vietnamese speech input remain outside the native engine.

## Interface

<table>
  <tr>
    <td><img src="docs/screenshots/overview.png" alt="Keyina overview screen"></td>
    <td><img src="docs/screenshots/typing.png" alt="Keyina typing settings"></td>
  </tr>
  <tr>
    <td><img src="docs/screenshots/hotkeys.png" alt="Keyina hotkey settings"></td>
    <td><img src="docs/screenshots/snippets.png" alt="Keyina snippet settings"></td>
  </tr>
  <tr>
    <td><img src="docs/screenshots/speech.png" alt="Keyina speech settings"></td>
    <td><img src="docs/screenshots/diagnostics.png" alt="Keyina diagnostics screen"></td>
  </tr>
</table>

## Architecture

```text
Physical keyboard events
        ↓ WH_KEYBOARD_LL, strict bypass rules
Keyina.Host.exe — .NET 10 Windows resident process
  ├─ input mode and hotkey state
  ├─ compatibility boundaries and diagnostics
  ├─ snippets and optional speech
  └─ minimal Backspace + Unicode SendInput edits
        ↕ narrow C ABI
KeyinaEngine.dll — C++20, native, deterministic, offline
  ├─ Telex composition
  ├─ tone placement and syllable validation
  ├─ raw-key rollback
  └─ Context Guard
```

The keyboard hook processes only supported plain-text events. Injected events carry a private marker and are never reprocessed. Modifier shortcuts, password fields, elevated targets, navigation or selection-risk boundaries, raw-input software, and unknown failures reset state and pass literal input through.

The older Windows Text Services Framework implementation remains optional behind `KEYINA_BUILD_TSF=ON`; it is not required for the default typing flow.

## Current capabilities

### Native Vietnamese engine

- Telex letter modifiers and tone keys.
- Modern and traditional tone placement.
- Vietnamese syllable validation with flexible recovery for practical typing.
- Exact raw-key rollback and Backspace reconstruction.
- Bounded active composition and UTF-32 to validated UTF-16 translation.
- Context transitions that recover literal identifiers, paths, URLs, and mixed-language tokens.
- Native unit, invariant, integration, golden-vector, and latency tests.

### Windows host

- Resident WinForms host on .NET 10 with a Fluent-inspired light, dark, and high-contrast interface.
- Familiar `Ctrl + Shift` Vietnamese input toggle.
- Native-engine bridge and low-level keyboard hook with injection-loop protection.
- Deterministic snippets and secure-input bypass.
- Tray lifecycle, startup registration, diagnostics, and self-tests.
- Optional Speechmatics Vietnamese dictation with Windows Credential Manager storage.
- Deterministic generated icons, lockups, and screenshot gallery.

## Privacy and security model

Ordinary Vietnamese typing stays local. `KeyinaEngine.dll` has no responsibility for network access, microphone capture, cloud credentials, telemetry, settings UI, or clipboard replacement.

Speech input is explicitly optional and isolated in the host. Its API credential must be stored in Windows Credential Manager and must never be committed to the repository or written to normal configuration files.

See [SECURITY.md](SECURITY.md) for private vulnerability reporting and [docs/compatibility/typing.md](docs/compatibility/typing.md) for compatibility boundaries.

## Build from source

### Requirements

- Windows 10 version 2004 or newer, or Windows 11, x64.
- Visual Studio 2022 with **Desktop development with C++**.
- CMake 3.25 or newer.
- The .NET SDK selected by [`global.json`](global.json).
- Python 3 for validation scripts.

### Clone and configure

```powershell
git clone https://github.com/KanzuXHorizon/Keyina.git
cd Keyina
cmake --preset windows-msvc-debug
```

### Build and test

```powershell
cmake --build --preset windows-msvc-debug
ctest --preset windows-msvc-debug --output-on-failure

dotnet build Keyina.slnx -c Debug
dotnet run --project apps/host/Keyina.Host.Tests/Keyina.Host.Tests.csproj -c Debug --no-build
```

### Run the development host

```powershell
dotnet run --project apps/host/Keyina.Host/Keyina.Host.csproj -c Debug --no-build -- --show-settings
```

The development host is unsigned. Windows or security software may warn about locally built keyboard-hook software. Review the source and build it yourself; do not bypass organizational security policy.

## Verification lanes

### Native Debug and Release

```powershell
cmake --preset windows-msvc-debug
cmake --build --preset windows-msvc-debug
ctest --preset windows-msvc-debug --output-on-failure

cmake --preset windows-msvc-release
cmake --build --preset windows-msvc-release
ctest --preset windows-msvc-release --output-on-failure

python tools/check_vectors.py
python tools/test_compare_benchmark.py
```

### Host, speech, resources, and benchmarks

```powershell
dotnet build Keyina.slnx -c Debug
dotnet run --project apps/host/Keyina.Host.Tests/Keyina.Host.Tests.csproj -c Debug --no-build
dotnet run --project apps/host/Keyina.Host/Keyina.Host.csproj -c Debug --no-build -- --self-test
dotnet run --project apps/host/Keyina.Host/Keyina.Host.csproj -c Debug --no-build -- --speech-self-test
dotnet run --project apps/host/Keyina.Host/Keyina.Host.csproj -c Debug --no-build -- --hotkey-self-test

dotnet build Keyina.slnx -c Release
dotnet run --project apps/host/Keyina.Host/Keyina.Host.csproj -c Release --no-build -- --resource-self-test
dotnet run --project apps/host/Keyina.Host.Benchmarks/Keyina.Host.Benchmarks.csproj -c Release --no-build
```

### Deterministic brand assets

```powershell
dotnet run --project tools/brand/Keyina.BrandAssets/Keyina.BrandAssets.csproj -c Release --no-build -- generate --root $PWD
git diff --exit-code -- docs/brand brand
```

Performance results are hardware-specific. Reproducible commands, comparison thresholds, and evidence live in [`docs/benchmarks/`](docs/benchmarks/) and [`docs/brand/`](docs/brand/).

## Repository map

```text
core/                       Platform-independent C++ input engine
platform/windows/hook/      Native C ABI bridge for the default hook backend
platform/windows/tsf/       Optional legacy TSF/COM backend
apps/host/                  .NET host, Windows adapters, speech, UI, tests, benchmarks
brand/                      Vector sources and deterministic generated assets
benchmarks/                 Native latency benchmarks
tests/                      Native unit, invariant, and Windows integration tests
tools/                      Brand and verification utilities
docs/                       Architecture, compatibility, evidence, specs, and plans
```

## Release gates

The project is public before it is production-ready. A stable release still requires:

- A signed, reproducible installer and uninstall path.
- Wider manual compatibility coverage across browsers, editors, terminals, Office applications, accessibility tools, Remote Desktop, and elevated applications.
- Accessibility and keyboard-navigation verification for the settings interface.
- Live opt-in speech-provider validation without exposing credentials.
- Release artifact provenance, checksums, and a documented update strategy.

Open work is tracked through GitHub issues and the design/implementation documents under [`docs/superpowers/`](docs/superpowers/).

## Contributing

Read [CONTRIBUTING.md](CONTRIBUTING.md) before opening a pull request. The project accepts focused, tested contributions that preserve privacy, literal fallback, deterministic behavior, and clean-room licensing boundaries.

- Bug reports and feature requests use the structured GitHub issue forms.
- Security vulnerabilities use the private process in [SECURITY.md](SECURITY.md).
- Community behavior follows [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md).
- General support scope is documented in [SUPPORT.md](SUPPORT.md).

## Clean-room notice

Keyina is not affiliated with UniKey, EVKey, OpenKey, Microsoft, or Speechmatics. No source code from existing Vietnamese input engines is copied into this repository. Product and company names are used only to explain compatibility or user expectations.

## License

Copyright 2026 Keyina contributors.

Licensed under the [Apache License 2.0](LICENSE).
