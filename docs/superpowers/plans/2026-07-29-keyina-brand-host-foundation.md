# Keyina Brand and Host Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add deterministic production brand assets and a tested .NET 8 Windows host foundation without changing or slowing the existing TSF typing path.

**Architecture:** Vector brand sources are checked in and a repository-owned .NET asset generator produces PNG/ICO files plus a SHA-256 manifest. `Keyina.Host.Core` contains dependency-free state/contracts, while `Keyina.Host` is a minimal Windows resident executable that consumes generated resources; UI, hotkeys, snippets, and Speechmatics are layered in later plans.

**Tech Stack:** C++20/CMake existing build, .NET SDK 10.0.301 pinned by `global.json` while host binaries target .NET 8, C# 12, xUnit-free repository-owned test runner initially, SVG/XML, System.Drawing only inside the Windows asset generator.

## Global Constraints

- `KeyinaTsf.dll` must not reference .NET, brand generation, network, audio, UI, JSON settings, or host assemblies.
- Four concept PNGs in `docs/image/` remain unchanged and are catalogued by SHA-256.
- Runtime app/tray icons are generated from clean SVG sources, not downscaled from concept canvases.
- Generated brand assets must be byte-identical across two runs on the same SDK/runtime.
- No package dependency is introduced unless the standard library cannot meet the requirement.
- The .NET target framework is `net8.0-windows10.0.19041.0` for Windows executables and `net8.0` for platform-independent libraries/tests.
- Every project enables nullable reference types, implicit usings, deterministic builds, warnings as errors, and invariant globalization only when compatible with Vietnamese text tests.

---

### Task 1: Pin .NET SDK and create solution skeleton

**Files:**
- Create: `global.json`
- Create: `Directory.Build.props`
- Create: `Keyina.slnx`
- Create: `apps/host/Keyina.Host.Core/Keyina.Host.Core.csproj`
- Create: `apps/host/Keyina.Host/Keyina.Host.csproj`
- Create: `apps/host/Keyina.Host/Program.cs`
- Create: `apps/host/Keyina.Host.Tests/Keyina.Host.Tests.csproj`
- Create: `apps/host/Keyina.Host.Tests/Program.cs`

**Interfaces:**
- Produces solution projects `Keyina.Host.Core`, `Keyina.Host`, and `Keyina.Host.Tests`.
- `Keyina.Host.Tests` is a console test runner returning exit code 1 on any failure.

- [x] **Step 1: Create a failing host test that references `Keyina.Host.Core.BuildInfo.ProductName` and expects `"Keyina"`.**
- [x] **Step 2: Run `dotnet build Keyina.slnx -c Debug`; verify failure because `BuildInfo` is absent.**
- [x] **Step 3: Add `BuildInfo` with constants `ProductName`, `ProductVersion`, and `ProtocolVersion`, plus the minimal WinExe entry point.**
- [x] **Step 4: Run Debug build and `dotnet run --project apps/host/Keyina.Host.Tests -c Debug`; verify the test passes.**
- [x] **Step 5: Run Release build and commit as `build(host): establish deterministic .NET foundation`.**

### Task 2: Catalog concept images without modifying them

**Files:**
- Create: `docs/brand/concept-assets.json`
- Create: `docs/brand/README.md`
- Create: `tools/brand/Keyina.BrandAssets/Keyina.BrandAssets.csproj`
- Create: `tools/brand/Keyina.BrandAssets/ConceptCatalog.cs`
- Create: `tools/brand/Keyina.BrandAssets/Program.cs`
- Create: `apps/host/Keyina.Host.Tests/BrandConceptCatalogTests.cs`

**Interfaces:**
- Produces JSON schema:

```json
{
  "schemaVersion": 1,
  "assets": [
    {
      "path": "docs/image/<filename>.png",
      "sha256": "UPPERCASE_HEX",
      "width": 1536,
      "height": 1024,
      "role": "concept"
    }
  ]
}
```

- [x] **Step 1: Add a failing test that loads `docs/brand/concept-assets.json`, requires exactly four unique assets, verifies file existence, SHA-256, and dimensions 1536×1024.**
- [x] **Step 2: Run the host test runner; verify failure because the catalog is absent.**
- [x] **Step 3: Implement catalog generation using SHA256 and direct PNG IHDR metadata parsing, sorted by normalized repository-relative path.**
- [x] **Step 4: Generate `concept-assets.json`, write `docs/brand/README.md` explaining concept-only usage, and rerun tests.**
- [x] **Step 5: Run the generator twice and compare SHA-256 of the JSON output; verify identical results.**
- [x] **Step 6: Commit as `docs(brand): catalog approved Keyina concepts`.**

### Task 3: Add the vector brand model and SVG sources

**Files:**
- Create: `brand/brand-tokens.json`
- Create: `tools/brand/Keyina.BrandAssets/BrandGeometry.cs`
- Create: `tools/brand/Keyina.BrandAssets/SvgWriter.cs`
- Create: `brand/keyina-mark.svg`
- Create: `brand/keyina-lockup.svg`
- Create: `brand/keyina-tray-active.svg`
- Create: `brand/keyina-tray-inactive.svg`
- Create: `brand/keyina-tray-listening.svg`
- Create: `apps/host/Keyina.Host.Tests/BrandSourceTests.cs`

**Interfaces:**
- `brand-tokens.json` contains fixed view boxes, gradient stops, foreground colors, safe-area ratios, and asset roles.
- `BrandGeometry` is the single geometry source used by both `SvgWriter` and raster generation.
- Every SVG uses an explicit `viewBox`, contains no embedded raster image, external URL, script, filter, blur, or font dependency.

- [ ] **Step 1: Add failing tests that require five SVG sources, parse them as XML, and reject `<image>`, `<script>`, `<filter>`, external hrefs, missing `viewBox`, and missing accessible `<title>`.**
- [ ] **Step 2: Run tests; verify failure because vector sources are absent.**
- [ ] **Step 3: Implement `BrandGeometry` primitives for rounded-square mark, speech/input outline, waveform, accent, and tray states.**
- [ ] **Step 4: Implement deterministic SVG serialization and generate the five checked-in SVG files from the geometry model.**
- [ ] **Step 5: Add checks that tray assets use no gradient or shadow and remain within a 2 px safe area in a 16×16 viewBox.**
- [ ] **Step 6: Run all tests and commit as `feat(brand): add vector-first Keyina identity`.**

### Task 4: Deterministically generate PNG and ICO assets

**Files:**
- Modify: `tools/brand/Keyina.BrandAssets/Program.cs`
- Create: `tools/brand/Keyina.BrandAssets/RasterWriter.cs`
- Create: `tools/brand/Keyina.BrandAssets/IcoWriter.cs`
- Create: `tools/brand/Keyina.BrandAssets/AssetManifest.cs`
- Create: `brand/generated/manifest.json`
- Create: generated PNG/ICO files under `brand/generated/`
- Create: `apps/host/Keyina.Host.Tests/GeneratedBrandAssetTests.cs`

**Interfaces:**
- CLI: `Keyina.BrandAssets generate --root <repo-root>`.
- `RasterWriter` draws the same `BrandGeometry` with `System.Drawing` at 4× supersampling and deterministic downsampling.
- Output manifest records source hash, output hash, width, height, role, and format.
- ICO stores PNG-compressed frames at 16, 20, 24, 32, 40, 48, 64, 128, and 256 px.

- [ ] **Step 1: Add failing tests that require app PNG dimensions, tray PNG dimensions, ICO frame sizes, non-empty alpha, and manifest hashes.**
- [ ] **Step 2: Verify failure because generated assets are absent.**
- [ ] **Step 3: Implement supersampled raster drawing from `BrandGeometry`, preserving small-icon stroke limits and transparent backgrounds.**
- [ ] **Step 4: Implement ICO frame writer with stable size ordering and zero timestamps.**
- [ ] **Step 5: Generate assets, run tests, delete outputs, regenerate, and verify every output SHA-256 is identical.**
- [ ] **Step 6: Commit as `build(brand): generate deterministic Windows assets`.**

### Task 5: Host lifecycle and tray state model

**Files:**
- Create: `apps/host/Keyina.Host.Core/TrayState.cs`
- Create: `apps/host/Keyina.Host.Core/HostState.cs`
- Create: `apps/host/Keyina.Host.Core/HostReducer.cs`
- Create: `apps/host/Keyina.Host/SingleInstanceGuard.cs`
- Modify: `apps/host/Keyina.Host/Program.cs`
- Create: `apps/host/Keyina.Host.Tests/HostReducerTests.cs`

**Interfaces:**

```csharp
public enum TrayState { VietnameseOn, VietnameseOff, Listening, Error }
public sealed record HostState(bool VietnameseEnabled, bool Listening, string? ErrorCode);
public abstract record HostEvent;
public static HostState HostReducer.Reduce(HostState state, HostEvent @event);
```

- [ ] **Step 1: Add failing reducer tests for initial state, input toggle, listening start/stop, error, recovery, and precedence of listening over input mode.**
- [ ] **Step 2: Run tests and verify missing types.**
- [ ] **Step 3: Implement immutable reducer and map every state to a generated tray asset.**
- [ ] **Step 4: Add a named-mutex single-instance guard; a second instance exits with a documented code and performs no registry/network work.**
- [ ] **Step 5: Run Debug/Release tests and verify host startup exits cleanly in `--self-test` mode without creating a tray icon.**
- [ ] **Step 6: Commit as `feat(host): add lifecycle and tray state foundation`.**

### Task 6: CI and evidence

**Files:**
- Modify: `.github/workflows/ci.yml`
- Modify: `README.md`
- Create: `docs/brand/verification.md`

- [ ] **Step 1: Add Windows CI commands for `dotnet build`, host tests, brand generation, and clean-tree verification after regeneration.**
- [ ] **Step 2: Run all native Debug tests, .NET Debug/Release tests, and brand deterministic checks locally.**
- [ ] **Step 3: Inspect `git diff`, generated file sizes, hashes, binary formats, and repository secret scan.**
- [ ] **Step 4: Record exact evidence and blocked items without claiming tray UI or installer completion.**
- [ ] **Step 5: Commit as `ci: verify host and brand foundation`.**
