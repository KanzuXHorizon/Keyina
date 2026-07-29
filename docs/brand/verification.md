# Brand and host foundation verification

**Verified:** 2026-07-29  
**Reference environment:** Windows build 26200, Intel x64, MSVC 19.44, .NET SDK 10.0.301 targeting .NET 10 LTS

## Brand evidence

- Four approved concept PNGs are preserved unchanged in `docs/image/`.
- `docs/brand/concept-assets.json` verifies exactly four files, SHA-256, and 1536×1024 dimensions.
- Five production SVG sources are generated from one repository-owned geometry model.
- SVG tests reject raster embedding, scripts, filters, external resources, missing titles, and missing view boxes.
- Tray SVGs use a 16×16 view box, no gradients or shadows, and enforce a 2 px geometry safe area.
- The generated tree contains 42 assets:
  - 10 app-icon PNG sizes from 16 to 512 px;
  - 27 tray PNGs across active, inactive, and listening states;
  - one lockup PNG;
  - one multi-resolution app ICO;
  - three multi-resolution tray ICO files.
- Every ICO contains ordered PNG-compressed frames at 16, 20, 24, 32, 40, 48, 64, 128, and 256 px.
- Two consecutive Release generations produced the same complete-tree SHA-256:

```text
bbbbd613bfd093cebb9764781a715ee5c8ed4e634beaa4e7d73d116fea707680
```

## Host evidence

- `Keyina.Host.Core` targets `net8.0` and contains immutable host/tray state.
- `Keyina.Host` targets `net8.0-windows10.0.19041.0` and embeds the generated multi-resolution application icon.
- Active, inactive, and listening tray ICO files are copied to the host output.
- Named-mutex ownership rejects a concurrent host instance and releases cleanly.
- `--self-test` starts no tray UI and prints the product/version contract.

Fresh commands:

```powershell
dotnet build Keyina.slnx -c Debug --no-restore
dotnet run --project apps/host/Keyina.Host.Tests/Keyina.Host.Tests.csproj -c Debug --no-build
dotnet run --project apps/host/Keyina.Host/Keyina.Host.csproj -c Debug --no-build -- --self-test
dotnet build Keyina.slnx -c Release --no-restore
dotnet run --project tools/brand/Keyina.BrandAssets/Keyina.BrandAssets.csproj -c Release --no-build -- generate --root F:\Keyina
git diff --exit-code -- docs/brand brand
```

Results:

- Debug .NET build: pass, 0 warnings, 0 errors.
- Host/brand tests: 11/11 pass.
- Host self-test: pass, output `Keyina 0.1.0`.
- Release .NET build: pass, 0 warnings, 0 errors.
- Brand regeneration: pass, no repository diff.

## Native regression evidence

Fresh native Debug and Release builds both passed:

- `keyina.windows.tsf_dll_smoke`
- `keyina.windows.tsf_integration`
- `keyina.unit`

Additional verification:

- 100 golden vectors validated.
- Benchmark comparator: 4/4 pass.
- Secret and user-specific absolute-path scan: no findings.

Release benchmark p99 values:

| Case | p99 |
|---|---:|
| ASCII pass-through | 0.2 µs |
| Letter modifier | 0.4 µs |
| Tone update | 0.4 µs |
| Guard-protected URL | 7.5 µs |
| Context Guard, 64 code points | 0.7 µs |

All absolute budgets in the native design remain satisfied.

## Explicitly not yet verified

This foundation does not claim the following are complete:

- a persistent notification-area UI and settings surface;
- global hotkey and modifier-only keyboard-hook behavior;
- snippet expansion and host/TSF IPC;
- Speechmatics authentication, microphone capture, or live Vietnamese transcription;
- installer, signing, upgrade, repair, or uninstall;
- elevated global TSF registration;
- third-party application compatibility matrix;
- high-contrast, 200% DPI, and manual 16 px taskbar visual review.

These remain release gates rather than assumed passes.
