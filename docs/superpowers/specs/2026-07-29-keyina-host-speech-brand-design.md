# Keyina Host, Speech, Hotkeys, Snippets, and Brand Design

**Date:** 2026-07-29  
**Status:** Approved from the user's product direction  
**License:** Apache-2.0 for source code; generated Keyina brand assets are project assets under the same repository license unless replaced before public launch

## 1. Purpose

Keyina expands from a deterministic Vietnamese TSF engine into a daily-use Windows input product without compromising the keystroke hot path.

The product has five user-facing capabilities:

1. Reliable Vietnamese Telex input through the existing native TSF service.
2. Familiar global enable/disable shortcuts compatible with UniKey/EVKey habits.
3. Fast text snippets and dynamic expansions.
4. Push-to-talk and toggle dictation using Speechmatics Realtime transcription for Vietnamese.
5. A recognizable Keyina brand with clear app, tray, installer, and documentation assets.

The speech, tray, configuration, and network layers must never run inside `KeyinaTsf.dll`. A network outage, slow microphone, Speechmatics error, or settings UI crash must not delay normal typing.

## 2. Product decisions

### 2.1 Process model

Keyina uses two production processes:

- `KeyinaTsf.dll`: C++20 COM in-process TSF service. It owns text composition in the focused application. It remains deterministic, offline, and allocation-conscious.
- `Keyina.Host.exe`: .NET 10 LTS Windows resident process. It owns the notification-area icon, global hotkeys, snippet configuration, microphone capture, Speechmatics WebSocket session, credentials, overlay state, diagnostics, and local IPC.

A third executable is allowed only for packaging or tests. Settings initially live in `Keyina.Host`; a separate WinUI 3 settings process may be introduced later only if startup/resource evidence justifies it.

### 2.2 Speechmatics integration

Speechmatics Realtime is accessed over WebSocket using the documented production endpoint `global.rt.speechmatics.com` by default. Vietnamese uses language code `vi`.

Default transcription configuration:

```json
{
  "language": "vi",
  "model": "enhanced",
  "max_delay": 0.7,
  "enable_partials": true
}
```

Speechmatics documents partial transcripts as revisable and typically below 500 ms, while final transcripts are stable and commonly arrive in roughly 0.7–2 seconds. Keyina therefore:

- displays partial text only in a non-activating overlay;
- never inserts partial text into the target application;
- commits only final transcript segments through the active TSF composition;
- ends the composition after the server's final transcript and local stop policy agree;
- preserves dictated text already finalized if the connection fails later;
- never retries a failed session by replaying recorded microphone audio unless the user explicitly enables an opt-in recording mode in a future release.

The long-lived API key is stored in Windows Credential Manager. It is never stored in JSON, environment templates, logs, crash reports, binaries, command-line arguments, or the repository. Direct API-key authentication is supported for this local desktop application. A future managed distribution may use temporary Realtime JWTs from a Keyina service; the desktop architecture must not require that service for the initial release.

Primary Speechmatics references:

- https://docs.speechmatics.com/speech-to-text/realtime/quickstart
- https://docs.speechmatics.com/get-started/authentication
- https://docs.speechmatics.com/speech-to-text/languages

### 2.3 Hotkey compatibility

Users should not need to relearn basic input toggling.

Default shortcuts:

- `Ctrl+Shift`: toggle Vietnamese input, matching the most familiar UniKey/EVKey convention.
- `Alt+Z`: optional alternate Vietnamese toggle; disabled by default to avoid duplicate accidental toggles.
- `Ctrl+Alt+Space`: push-to-talk dictation. Hold to record; release requests finalization.
- `Ctrl+Alt+V`: toggle dictation for accessibility and long-form speech.
- `Escape`: cancel the current uncommitted dictation segment while the overlay is visible.

Rules:

- Shortcuts are configurable and conflict-checked before registration.
- A failed `RegisterHotKey` operation is visible in settings and diagnostics; Keyina never silently claims the shortcut works.
- Modifier-only toggle behavior is implemented with a low-level keyboard state machine because `RegisterHotKey` cannot represent bare `Ctrl+Shift` release semantics.
- The keyboard hook performs no network, file, JSON, registry, UI, or snippet work. It only records bounded state and posts a command to the host event loop.
- Password, secure desktop, elevated-integrity mismatch, and excluded-application policies bypass snippet and speech activation.

### 2.4 Snippets

Keyina snippets are explicit, local, deterministic, and bounded.

Default command prefix: `;k`

Built-in commands:

- `;kvi` toggles Vietnamese input.
- `;kvoice` toggles dictation.
- `;kdate` inserts the local date in the configured format.
- `;ktime` inserts local time.
- `;kdatetime` inserts both.

User snippets may use shorter triggers such as `;mail`, `;addr`, or `;commit`.

Expansion contract:

- A snippet activates only after an explicit delimiter: Space, Tab, Enter, or a configured punctuation delimiter.
- The trigger is replaced atomically through the TSF-owned range; Keyina does not synthesize repeated Backspace events.
- Password and secure input scopes are always excluded.
- Per-application allow/deny scopes are supported.
- Static snippets have a maximum trigger length of 64 Unicode code points and expansion length of 16 KiB.
- Dynamic variables are limited to an allowlist: date, time, datetime, clipboard text after explicit user opt-in, and selected environment-independent formatting values.
- Snippets are stored in a versioned UTF-8 JSON file using atomic replace. API keys and secrets are forbidden fields.
- Snippet matching is case-sensitive by default and can be configured per snippet.

### 2.5 Brand system

The four user-approved concept images in `docs/image/` are retained as visual provenance and are not used directly as tiny runtime icons because they contain large-canvas lighting and raster detail that becomes unclear in the Windows tray.

The production brand source is vector-first:

- `brand/keyina-mark.svg`: main rounded-square mark.
- `brand/keyina-lockup.svg`: horizontal icon and wordmark.
- `brand/keyina-tray-active.svg`: simplified monochrome/brand-active tray glyph.
- `brand/keyina-tray-inactive.svg`: muted tray glyph.
- `brand/keyina-tray-listening.svg`: voice-listening state.

Visual language:

- The mark combines a speech bubble/input key shape, an audio waveform, and a Vietnamese acute accent.
- Primary gradient: electric blue → violet → warm red.
- Small icons remove glow, shadow, and fine internal strokes.
- The 16 px tray icon uses at most two stroke weights and a single high-contrast foreground shape.
- Disabled state must remain recognizable in Windows light and dark taskbars.
- Speech-listening state may animate only in the overlay; tray icons remain static to avoid distracting continuous motion.

Generated assets:

- PNG: 16, 20, 24, 32, 40, 48, 64, 128, 256, and 512 px where applicable.
- ICO: multi-resolution 16/20/24/32/40/48/64/128/256 px.
- Separate active, inactive, and listening tray ICO files.
- Asset generation is deterministic and checks source SHA-256 plus dimensions.

### 2.6 Local IPC and dictation ownership

The host and TSF communicate through a local named-pipe protocol with explicit versioning.

Production message families:

- `hello`: protocol version, process ID, thread ID, and capability flags.
- `begin_dictation`: session ID and requested language.
- `partial_transcript`: overlay-only text; never applied by TSF.
- `final_transcript`: stable text to insert through the owned TSF composition.
- `end_dictation`: normal completion, cancellation, timeout, or provider failure.
- `toggle_input`: requested input-mode change.
- `configuration_changed`: immutable configuration generation number.

Security and reliability:

- Named pipes are restricted to the current interactive user SID.
- Every message has a maximum serialized size of 64 KiB.
- Transcript text is UTF-8 and validated before conversion.
- Session IDs are 128-bit random values.
- Stale messages from an earlier focus/session are rejected.
- The TSF side never blocks waiting for the host. It polls or consumes already-available commands during normal TSF callbacks and fails open to literal input.
- Host disconnect resets only speech/snippet auxiliary state, never the ordinary Telex engine.

## 3. Host architecture

`Keyina.Host` is split into focused libraries:

```text
apps/host/
  Keyina.Host/                 WinExe entry point, tray, lifecycle
  Keyina.Host.Core/            state machines and dependency-free contracts
  Keyina.Host.Windows/         hotkeys, keyboard hook, credential manager, audio
  Keyina.Speechmatics/         WebSocket protocol and transcript aggregation
  Keyina.BrandAssets/          generated resource manifest consumed by host
  Keyina.Host.Tests/           unit and contract tests
```

Key interfaces:

```csharp
public interface IInputModeController
{
    bool IsVietnameseEnabled { get; }
    ValueTask SetVietnameseEnabledAsync(bool enabled, CancellationToken cancellationToken);
}

public interface ISnippetMatcher
{
    SnippetMatch? Match(ReadOnlySpan<char> token, char delimiter, AppIdentity app);
}

public interface ISpeechSession : IAsyncDisposable
{
    IAsyncEnumerable<SpeechEvent> RunAsync(
        ChannelReader<ReadOnlyMemory<byte>> audio,
        SpeechSessionOptions options,
        CancellationToken cancellationToken);
}

public interface ICredentialVault
{
    ValueTask<string?> ReadSecretAsync(string target, CancellationToken cancellationToken);
    ValueTask WriteSecretAsync(string target, string secret, CancellationToken cancellationToken);
    ValueTask DeleteSecretAsync(string target, CancellationToken cancellationToken);
}
```

The core project must not reference Windows UI, NAudio, WebSocket concrete classes, registry APIs, or Speechmatics-specific JSON types.

## 4. Audio pipeline

Initial capture implementation uses WASAPI through a maintained Windows audio library. Audio is converted to mono PCM signed 16-bit little-endian at 16 kHz before transmission.

Pipeline:

```text
WASAPI capture
  → bounded audio channel
  → optional resampler
  → 20–100 ms PCM chunks
  → Speechmatics WebSocket
  → partial/final event parser
  → overlay/final TSF IPC
```

Budgets:

- Hotkey-to-capture-start p95 ≤ 80 ms after host warm-up.
- Audio chunk queue depth ≤ 2 seconds; overflow cancels the session instead of growing memory indefinitely.
- Host working set at idle ≤ 80 MiB target and ≤ 120 MiB release gate until WinUI is introduced.
- Idle CPU average < 0.2% over five minutes on the reference machine.
- No audio file is written by default.
- Stop-to-final-request ≤ 30 ms local processing time, excluding provider/network latency.

## 5. Overlay and tray UX

The overlay is non-activating, keyboard-accessible, DPI-aware, and anchored near the text caret when reliable caret geometry exists; otherwise it appears above the notification area.

States:

- Ready: microphone available, no active session.
- Connecting: Speechmatics authentication/WebSocket negotiation.
- Listening: waveform meter and elapsed time.
- Finalizing: microphone stopped; waiting for stable final transcript.
- Inserted: brief success confirmation, then auto-dismiss.
- Error: concise reason and one recovery action.
- Offline: ordinary Vietnamese typing remains fully available.

The tray menu exposes:

- Vietnamese input on/off.
- Start/stop dictation.
- Current microphone.
- Speech provider status.
- Snippets on/off.
- Settings.
- Diagnostics.
- Exit host; ordinary TSF behavior follows the configured fail-open mode.

## 6. Diagnostics and privacy

Allowed diagnostic fields:

- monotonic timestamps and duration buckets;
- state transitions;
- provider message type without transcript content;
- audio queue depth;
- WebSocket close code;
- microphone device identifier hash;
- hotkey registration result;
- IPC protocol/error code;
- application executable hash/name according to privacy setting.

Forbidden diagnostic fields:

- raw or final transcript text;
- audio bytes;
- API key or JWT;
- clipboard content;
- window title, document path, URL, or email address;
- snippet expansion content unless the user explicitly exports a redacted settings bundle.

## 7. Tests and benchmarks

### 7.1 Unit tests

- Modifier-only hotkey state transitions.
- Registered-hotkey conflict reporting.
- Snippet delimiter, scope, length, case, and secure-field rules.
- Atomic configuration migration and invalid-schema rejection.
- Speechmatics StartRecognition JSON.
- Partial/final transcript parsing and revision behavior.
- WebSocket error, cancellation, close, and timeout mapping.
- Credential Manager P/Invoke argument validation without storing real secrets in CI.
- IPC framing, maximum size, stale session, and malformed UTF-8 rejection.
- Brand manifest and generated dimension checks.

### 7.2 Contract tests

A fake Speechmatics WebSocket server replays:

- normal partial → revised partial → final flow;
- final-only flow;
- authentication failure;
- provider error;
- delayed finalization;
- malformed JSON;
- connection loss after one final segment.

No live Speechmatics credential is required for default CI. A manual `speechmatics-live` test lane runs only when a developer explicitly provides a credential through Windows Credential Manager and opts in.

### 7.3 Benchmarks

- Snippet lookup median ≤ 5 µs and p99 ≤ 25 µs for 10,000 snippets.
- Hotkey state transition p99 ≤ 10 µs excluding OS callback overhead.
- IPC frame encode/decode p99 ≤ 50 µs for a 4 KiB final transcript.
- Transcript aggregation p99 ≤ 100 µs per provider message.
- Brand asset generation is deterministic across two consecutive runs.
- Existing Telex and TSF benchmarks remain release gates and must not regress more than 20%.

## 8. Production-readiness gates

Keyina may be called production-ready only after all applicable gates pass:

- Signed x64 installer and signed binaries.
- Reversible install, upgrade, repair, and uninstall.
- Global TSF registration verified in an elevated test environment.
- Compatibility matrix for supported Windows applications.
- API-key entry/read/delete verified against Windows Credential Manager.
- Live Vietnamese Speechmatics smoke test run manually without exposing the credential.
- Microphone permission denial and device removal handled.
- Network loss and provider outage do not affect ordinary typing.
- Hotkey conflicts are visible and recoverable.
- Tray icons pass 16 px light/dark taskbar visual checks.
- Accessibility, keyboard navigation, 200% DPI, reduced motion, and high contrast checked.
- Debug, Release, sanitizer, unit, contract, integration, and benchmark lanes pass.
- No generated secret, audio capture, transcript, or user-specific absolute path is committed.

Code signing certificates, Speechmatics account entitlement, real API credentials, and third-party application manual tests are external release inputs. Their absence must be reported as a blocked gate, never silently treated as passed.

## 9. Delivery slices

### Slice A: Brand and host foundation

- Catalog four concept images.
- Add vector source assets and deterministic PNG/ICO generator.
- Add .NET solution and dependency-free host core.
- Add tray state model and resource manifest.

### Slice B: Hotkeys and snippets

- Implement hotkey state machines and registration adapters.
- Implement snippet schema, matcher, and atomic storage.
- Add local IPC contracts and tests.
- Connect ordinary input toggle through an explicit host/TSF command contract.

### Slice C: Speechmatics dictation

- Implement credential vault adapter.
- Implement WASAPI audio capture and bounded audio channel.
- Implement Speechmatics WebSocket client and fake-server contract tests.
- Implement dictation session state machine and overlay model.
- Connect final transcript to TSF-owned dictation composition.
- Add live opt-in smoke test and latency benchmark harness.

### Slice D: Packaging and compatibility

- Settings and tray UI hardening.
- Installer, signing hooks, upgrade/repair/uninstall.
- Elevated TSF registration and application compatibility matrix.
- Final performance, privacy, accessibility, and release evidence.
