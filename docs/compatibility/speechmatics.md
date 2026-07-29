# Speechmatics realtime dictation evidence

## Scope

Keyina speech dictation is an optional host feature. `KeyinaTsf.dll` and the ordinary Vietnamese keystroke path contain no microphone, credential, WebSocket, Speechmatics, or JSON code. Speech failure must not disable Vietnamese input.

Default provider contract:

- Endpoint: `wss://global.rt.speechmatics.com/v2`
- Language: `vi`
- Model: `enhanced`
- Audio: raw mono `pcm_s16le`, 16,000 Hz
- `max_delay`: `0.7`
- Partials: enabled for overlay only
- Final segments: one atomic IPC insertion each

The API key is read from Windows Credential Manager target `Keyina/Speechmatics/ApiKey`. It is not accepted through command-line arguments or JSON configuration.

## Offline verification

Fresh local verification on July 29, 2026:

```powershell
dotnet build Keyina.slnx -c Debug
dotnet run --project apps/host/Keyina.Host.Tests/Keyina.Host.Tests.csproj -c Debug
dotnet run --project apps/host/Keyina.Host/Keyina.Host.csproj -c Debug -- --speech-self-test
```

Result:

- Build: 0 warnings, 0 errors.
- Repository-owned host tests: 77/77 pass after resource-probe tests were added.
- Speech self-test: `speech_self_test_ok`.
- No network, microphone, or Credential Manager secret is used by the self-test.

Covered behavior includes:

- exact StartRecognition and EndOfStream JSON;
- wait for RecognitionStarted before audio;
- maximum 500 outstanding chunks;
- AudioAdded sequence acknowledgements;
- final-before-EndOfTranscript ordering;
- mutable partial and immutable final handling;
- provider error, unexpected close, cancellation, and idempotent disposal;
- Credential Manager read/write/overwrite/delete;
- streaming resampler continuity across arbitrary callback boundaries;
- a strict two-second, 64,000-byte PCM queue;
- no-device, permission denial, device removal, overflow, and cancellation;
- partial overlay-only behavior;
- final transcript deduplication and focus-generation IPC;
- speech failure isolation from native Vietnamese input.

## Release performance evidence

Machine-specific environment:

- Windows 10.0.26200 x64
- .NET 8.0.29
- 16 logical processors
- Release build

| Case | p99 | Allocation/op | Budget |
|---|---:|---:|---:|
| Speechmatics final JSON parse | 13.2 µs | 256 B | 50 µs / 512 B |
| Partial transcript aggregation | 0.6 µs | 256 B | 50 µs / 512 B |
| 30 ms 48 kHz stereo to 16 kHz mono PCM | 20.0 µs | 2,041 B | 1 ms / 4,096 B |
| Final transcript IPC encode | 0.4 µs | 72 B | 50 µs / 128 B |

All four cases passed both latency and allocation budgets. The benchmark process itself is not a resident-memory measurement because it retains sample arrays.

Separate Release host resource probe over approximately three seconds:

- working set: 22,159,360 bytes (about 21.1 MiB);
- private memory: 6,971,392 bytes (about 6.6 MiB);
- managed heap: 110,504 bytes;
- threads: 12;
- measured CPU time: 0 ms in the sample window.

This is a baseline with the host loaded but without persistent tray, keyboard hook, microphone, or live WebSocket activity. Those components require their own post-integration resource run.

## Live-provider gate

A live Vietnamese provider test is intentionally not run automatically. It requires all of the following:

1. a valid Speechmatics API key stored in Windows Credential Manager;
2. an explicit developer opt-in;
3. microphone permission;
4. confirmation that the test may send spoken audio to Speechmatics.

Until that test is run, Keyina does not claim measured end-to-end cloud transcription latency or Vietnamese recognition accuracy on the current account/region. CI uploads only synthetic benchmark and resource JSON; it never uploads transcript or audio content.
