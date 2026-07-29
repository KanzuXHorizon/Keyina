# Keyina

![Keyina](brand/generated/lockup/keyina-lockup-1680x512.png)

Keyina is a privacy-first Vietnamese input platform for Windows. It is not a visual remake of UniKey or EVKey. The project focuses on measurable behavior:

1. **Reversible composition** — transformations can be undone without losing the original keystrokes.
2. **Context Guard** — deterministic protection for source code, URLs, email addresses, commands, paths, identifiers, and English-heavy tokens.
3. **Native TSF integration** — text edits use Windows Text Services Framework compositions instead of blind global Backspace injection.
4. **Isolated productivity host** — tray, hotkeys, snippets, and optional speech run outside the keystroke DLL.
5. **Evidence-driven releases** — tests, benchmarks, generated-asset hashes, compatibility evidence, and explicit blocked gates replace unsupported claims.

## Architecture

```text
Focused Windows application
        ↕ TSF composition/edit sessions
KeyinaTsf.dll — C++20, native, offline hot path
        ↕ bounded local IPC (planned next slice)
Keyina.Host.exe — .NET 8 Windows resident process
  - tray and settings lifecycle
  - familiar input-mode hotkeys
  - deterministic snippets
  - optional Speechmatics Vietnamese dictation
  - Windows Credential Manager integration
```

`KeyinaTsf.dll` does not access the network, microphone, settings UI, clipboard, or Speechmatics. A host/provider failure must not delay normal Vietnamese typing.

## Current verified capabilities

### Native input engine

- Telex letter modifiers and tone keys.
- Modern and traditional tone placement.
- Exact raw-key rollback and Backspace reconstruction.
- 64-key bounded active composition.
- Context transition recovery, such as converting an earlier `á` back to raw `as_` when the token becomes an identifier.
- UTF-32 to validated UTF-16 edit translation.
- Real local `ITfThreadMgr`/`ITfContext`/`ITextStoreACP` integration tests.
- Secure-mode pass-through.

### Host and brand foundation

- Deterministic .NET 8 host/core solution with warnings as errors.
- Immutable tray-state reducer and named-mutex single-instance guard.
- Multi-resolution Windows application icon.
- Active, inactive, and listening tray icons.
- Five vector-first SVG sources generated from one geometry model.
- 42 generated PNG/ICO assets with SHA-256 manifest verification.
- Four user-approved concept images preserved in `docs/image/` as design provenance.

## Verification snapshot

Fresh local verification on 2026-07-29:

- Native Debug: 3/3 tests pass.
- Native Release: 3/3 tests pass.
- Host/brand: 11/11 tests pass.
- Golden Telex vectors: 100 validated.
- Benchmark comparator: 4/4 tests pass.
- Brand regeneration: byte-identical output and no Git diff.
- Native p99 latency:
  - ASCII pass-through: 0.2 µs
  - letter modifier: 0.4 µs
  - tone update: 0.4 µs
  - protected URL path: 7.5 µs
  - 64-code-point Context Guard: 0.7 µs

See `docs/brand/verification.md` for exact commands and remaining blocked gates.

## Repository layout

```text
core/                       Deterministic platform-independent input engine
platform/windows/tsf/       Windows TSF/COM adapter and integration contracts
apps/host/                  .NET host, core state, and tests
brand/                      Vector sources and generated Windows assets
benchmarks/                 Native latency benchmarks
tests/                      Native unit, invariant, and TSF integration tests
tools/brand/                Deterministic brand catalog/vector/raster generator
docs/                       Specs, plans, compatibility, brand, and evidence
```

## Build and test

### Native Windows

```powershell
cmake --preset windows-msvc-debug
cmake --build --preset windows-msvc-debug
ctest --preset windows-msvc-debug --output-on-failure

cmake --preset windows-msvc-release
cmake --build --preset windows-msvc-release
ctest --preset windows-msvc-release --output-on-failure
```

### Host and brand

```powershell
dotnet build Keyina.slnx -c Debug
dotnet run --project apps/host/Keyina.Host.Tests/Keyina.Host.Tests.csproj -c Debug --no-build
dotnet build Keyina.slnx -c Release
dotnet run --project tools/brand/Keyina.BrandAssets/Keyina.BrandAssets.csproj -c Release --no-build -- generate --root F:\Keyina
git diff --exit-code -- docs/brand brand
```

## Product principles

- Clean-room Apache-2.0 implementation; no source is copied from existing Vietnamese input engines.
- No keystroke logging or cloud telemetry by default.
- Password and secure input scopes are bypassed.
- Speech is optional and isolated from ordinary typing.
- Speech credentials must live in Windows Credential Manager, never config files or the repository.
- Literal input is the fallback for inconsistent native state.
- Production readiness requires signed installation, elevated TSF registration, manual application compatibility, accessibility checks, and a live opt-in Speechmatics smoke test.

## Status

Keyina currently has a verified native engine, functional local TSF proof, deterministic brand assets, and a tested host foundation. It is **not yet a production-ready installer**. Hotkeys, snippets, IPC, Speechmatics dictation, persistent tray UI, signing, and the third-party compatibility matrix remain active implementation slices.

Design and execution documents:

- `docs/superpowers/specs/2026-07-29-keyina-vietnamese-text-platform-design.md`
- `docs/superpowers/specs/2026-07-29-keyina-host-speech-brand-design.md`
- `docs/superpowers/plans/2026-07-29-keyina-functional-tsf.md`
- `docs/superpowers/plans/2026-07-29-keyina-brand-host-foundation.md`
