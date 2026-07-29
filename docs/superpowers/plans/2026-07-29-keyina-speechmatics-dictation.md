# Keyina Speechmatics Dictation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add optional low-latency Vietnamese dictation using Speechmatics Realtime while preserving ordinary TSF typing performance, privacy, and fail-open behavior.

**Architecture:** `Keyina.Speechmatics` is a platform-independent .NET 8 protocol/session library over an injectable WebSocket transport. `Keyina.Host.Windows` stores the API key in Windows Credential Manager and captures bounded microphone audio. Partials update only a non-activating overlay model; finals are encoded as `FinalTranscript` IPC frames and committed atomically by TSF. No Speechmatics, audio, credential, or WebSocket code is linked into `KeyinaTsf.dll`.

**Tech Stack:** .NET 8, `ClientWebSocket`, `System.Text.Json`, Windows Credential Manager P/Invoke, NAudio 2.3.0 stable for WASAPI capture, bounded `Channel<T>`, existing C#/C++ IPC protocol.

## Global Constraints

- Default endpoint: `wss://global.rt.speechmatics.com/v2`.
- Default language: `vi`; model: `enhanced`; max delay: `0.7`; partials: enabled.
- Audio format: mono raw `pcm_s16le`, 16,000 Hz, even-sized chunks.
- StartRecognition is sent exactly once and audio is not sent before `RecognitionStarted`.
- Maximum 500 outstanding audio chunks and maximum two seconds of locally queued audio.
- Partials are never inserted into the target application.
- Finals are immutable and inserted once per provider final segment.
- Stop sends `EndOfStream` with the exact final sequence number and waits for `EndOfTranscript` with a bounded timeout.
- No audio is written to disk by default.
- API key is never accepted through command-line arguments, JSON config, environment templates, logs, or crash evidence.
- Live tests require explicit opt-in and a Credential Manager secret; default CI uses a fake transport.

---

### Task 1: Protocol options and deterministic JSON

**Files:**
- Create: `apps/host/Keyina.Speechmatics/Keyina.Speechmatics.csproj`
- Create: `apps/host/Keyina.Speechmatics/SpeechmaticsOptions.cs`
- Create: `apps/host/Keyina.Speechmatics/SpeechmaticsProtocol.cs`
- Create: `apps/host/Keyina.Speechmatics/SpeechEvent.cs`
- Create: `apps/host/Keyina.Host.Tests/SpeechmaticsProtocolTests.cs`
- Modify: `Keyina.slnx`
- Modify: `apps/host/Keyina.Host.Tests/Keyina.Host.Tests.csproj`

- [x] **Step 1: Add failing tests for exact StartRecognition JSON, EndOfStream JSON, Vietnamese defaults, option validation, partial/final parsing, error parsing, malformed JSON, and unknown messages.**
- [x] **Step 2: Verify missing project/contracts.**
- [x] **Step 3: Implement deterministic UTF-8 JSON writer and strict server-message parser.**
- [x] **Step 4: Run tests and commit as `feat(speech): add Speechmatics realtime protocol`.**

### Task 2: Injectable WebSocket session

**Files:**
- Create: `apps/host/Keyina.Speechmatics/ISpeechmaticsTransport.cs`
- Create: `apps/host/Keyina.Speechmatics/ClientWebSocketTransport.cs`
- Create: `apps/host/Keyina.Speechmatics/SpeechmaticsRealtimeSession.cs`
- Create: `apps/host/Keyina.Host.Tests/FakeSpeechmaticsTransport.cs`
- Create: `apps/host/Keyina.Host.Tests/SpeechmaticsSessionTests.cs`

- [x] **Step 1: Add failing scripted tests for connect/auth header, wait-for-start, binary audio, sequence acknowledgements, outstanding-chunk limit, EndOfStream, final-before-end ordering, provider error, connection close, cancellation, and disposal.**
- [x] **Step 2: Implement transport abstraction and production `ClientWebSocket` adapter.**
- [x] **Step 3: Implement session state machine with bounded semaphore and no transcript logging.**
- [x] **Step 4: Run repeated fake-server tests and commit as `feat(speech): stream Speechmatics sessions safely`.**

### Task 3: Transcript aggregation and dictation state

**Files:**
- Create: `apps/host/Keyina.Host.Core/Speech/DictationState.cs`
- Create: `apps/host/Keyina.Host.Core/Speech/DictationReducer.cs`
- Create: `apps/host/Keyina.Host.Core/Speech/TranscriptAggregator.cs`
- Create: `apps/host/Keyina.Host.Tests/DictationReducerTests.cs`
- Create: `apps/host/Keyina.Host.Tests/TranscriptAggregatorTests.cs`

- [x] **Step 1: Add failing tests for connecting/listening/finalizing/inserted/error/cancelled states and invalid transitions.**
- [x] **Step 2: Add partial revision tests proving partials replace overlay text while finals append once and clear the partial.**
- [x] **Step 3: Implement immutable reducer and transcript aggregator.**
- [x] **Step 4: Map each final segment to one existing `FinalTranscript` IPC envelope and commit as `feat(speech): aggregate stable dictation finals`.**

### Task 4: Windows Credential Manager vault

**Files:**
- Create: `apps/host/Keyina.Host.Windows/Keyina.Host.Windows.csproj`
- Create: `apps/host/Keyina.Host.Windows/Credentials/ICredentialVault.cs`
- Create: `apps/host/Keyina.Host.Windows/Credentials/WindowsCredentialVault.cs`
- Create: `apps/host/Keyina.Host.Tests/WindowsCredentialVaultTests.cs`
- Modify: `Keyina.slnx`

- [x] **Step 1: Add argument/lifecycle tests using a unique test target and random non-production secret.**
- [x] **Step 2: Implement `CredWriteW`, `CredReadW`, `CredDeleteW`, `CredFree` with `CRED_TYPE_GENERIC` and current-user persistence.**
- [x] **Step 3: Verify read/write/delete and ensure test cleanup in `finally`.**
- [x] **Step 4: Scan process arguments/config output for the test secret and commit as `feat(security): store Speechmatics key in Credential Manager`.**

### Task 5: Bounded microphone capture

**Files:**
- Modify: `apps/host/Keyina.Host.Windows/Keyina.Host.Windows.csproj`
- Create: `apps/host/Keyina.Host.Windows/Audio/IAudioCapture.cs`
- Create: `apps/host/Keyina.Host.Windows/Audio/WasapiMicrophoneCapture.cs`
- Create: `apps/host/Keyina.Host.Windows/Audio/Pcm16MonoConverter.cs`
- Create: `apps/host/Keyina.Host.Tests/Pcm16MonoConverterTests.cs`
- Create: `apps/host/Keyina.Host.Tests/AudioQueueTests.cs`

- [x] **Step 1: Pin stable `NAudio` 2.3.0 and record license/source in dependency documentation.**
- [x] **Step 2: Add pure conversion tests for float/PCM source formats, clipping, channel mixing, resampling continuity, and even chunk boundaries.**
- [x] **Step 3: Implement WASAPI capture with 20–100 ms chunks and a two-second bounded channel.**
- [x] **Step 4: Add no-device, permission denial, device removal, overflow-cancel, and stop/dispose tests using adapters/fakes.**
- [x] **Step 5: Commit as `feat(audio): capture bounded Speechmatics PCM`.**

### Task 6: Host orchestration and IPC finals

**Files:**
- Create: `apps/host/Keyina.Host/Speech/DictationCoordinator.cs`
- Create: `apps/host/Keyina.Host/Speech/DictationOverlayModel.cs`
- Create: `apps/host/Keyina.Host.Tests/DictationCoordinatorTests.cs`
- Modify: `apps/host/Keyina.Host/Program.cs`

- [ ] **Step 1: Add integration tests with fake audio and fake Speechmatics transport proving partials stay overlay-only and finals produce exactly one IPC frame.**
- [ ] **Step 2: Implement push-to-talk and toggle-session lifecycle with cancellation and finalization timeout.**
- [ ] **Step 3: Ensure provider/network failure changes only host speech state and does not disable native input.**
- [ ] **Step 4: Add `--speech-self-test` using fake transport, no microphone, no credential, no network.**
- [ ] **Step 5: Commit as `feat(host): orchestrate optional Vietnamese dictation`.**

### Task 7: Speech benchmarks and evidence

**Files:**
- Modify: `apps/host/Keyina.Host.Benchmarks/Program.cs` when available
- Modify: `.github/workflows/ci.yml`
- Create: `docs/compatibility/speechmatics.md`
- Modify: `README.md`

- [ ] **Step 1: Benchmark protocol parse, transcript aggregation, audio conversion, and IPC final encode.**
- [ ] **Step 2: Run fake-server contract tests repeatedly and publish no transcript/audio artifacts.**
- [ ] **Step 3: Add opt-in live Vietnamese smoke test that reads only Credential Manager and redacts all content.**
- [ ] **Step 4: Record live test as blocked until the developer supplies a valid credential and explicitly runs it.**
- [ ] **Step 5: Commit as `docs: record Speechmatics dictation evidence`.**
