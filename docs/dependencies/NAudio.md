# NAudio dependency record

Keyina pins `NAudio.Wasapi` version `2.3.0` for Windows microphone capture.

## Scope

- Used only by `apps/host/Keyina.Host.Windows`.
- Not linked into `KeyinaTsf.dll` or the native keystroke path.
- Provides the Windows WASAPI capture adapter. Resampling, buffering, overflow policy, and Speechmatics protocol remain repository-owned code.

## License and source

- License: MIT.
- Package: `NAudio.Wasapi` 2.3.0.
- NuGet package family: `https://www.nuget.org/packages/NAudio/2.3.0`.
- Upstream repository: `https://github.com/naudio/NAudio`.

Keyina intentionally uses the latest stable 2.x release rather than the available 3.0 prerelease line. Package restore is lock-file based, and CI uses locked mode.
