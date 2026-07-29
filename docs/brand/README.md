# Keyina brand sources

The four PNG files in `docs/image/` are the user-approved visual concepts captured on 2026-07-29. They establish the intended Keyina identity: a blue-violet-red mark combining a Vietnamese accent, a speech/input shape, and an audio waveform.

These concept canvases are retained unchanged for provenance and design review. They are not shipped directly as taskbar, tray, installer, or executable icons because their large-canvas lighting, glow, and fine detail do not remain clear at Windows icon sizes.

Production assets are generated from the vector-first geometry under `brand/`. The generator records source and output hashes and creates dedicated simplified tray states for light and dark Windows taskbars.

`concept-assets.json` is generated with:

```powershell
dotnet run --project tools/brand/Keyina.BrandAssets -c Release -- catalog --root F:\Keyina
```

The catalog must contain exactly four PNG files and match their SHA-256 hashes and 1536×1024 dimensions.
