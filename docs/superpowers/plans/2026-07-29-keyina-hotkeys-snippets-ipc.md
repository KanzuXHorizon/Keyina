# Keyina Hotkeys, Snippets, and IPC Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add familiar low-latency input toggles, deterministic snippet expansion, and a bounded authenticated local IPC contract that can later carry final Speechmatics transcripts into the TSF service.

**Architecture:** All policy and state machines live in `Keyina.Host.Core` and are independently benchmarkable. Windows keyboard hooks and `RegisterHotKey` are isolated in `Keyina.Host.Windows`; they post immutable commands to the host event loop and perform no network, disk, JSON, IPC, or UI work in callbacks. Snippets are matched in the host core but inserted atomically by the TSF side through a versioned named-pipe protocol restricted to the current user.

**Tech Stack:** .NET 8/C# 12, Windows P/Invoke, C++20 TSF adapter, JSON source generation or standard `System.Text.Json`, named pipes, repository-owned tests and benchmarks.

## Global Constraints

- `KeyinaTsf.dll` never blocks waiting for host IPC and never loads .NET.
- Bare `Ctrl+Shift` toggles only when the chord is pressed/released without another non-modifier key.
- Repeated keyboard events and left/right modifier variants must not double-toggle.
- Registered hotkeys must report OS conflicts explicitly.
- Hotkey callbacks perform bounded constant-time state transitions and post commands only.
- Snippets never activate in password/secure input scopes.
- Snippet trigger length ≤ 64 Unicode code points; expansion length ≤ 16 KiB.
- Snippet expansion requires an explicit delimiter and preserves the delimiter according to snippet policy.
- IPC payloads are UTF-8, versioned, length-prefixed, and ≤ 64 KiB.
- Named-pipe ACL is restricted to the current interactive user SID.
- Stale session/focus generations are rejected.
- No snippet expansion, transcript text, key sequence, or clipboard content is written to diagnostics.

---

### Task 1: Hotkey contracts and modifier-only toggle state machine

**Files:**
- Create: `apps/host/Keyina.Host.Core/Hotkeys/HotkeyChord.cs`
- Create: `apps/host/Keyina.Host.Core/Hotkeys/HotkeyCommand.cs`
- Create: `apps/host/Keyina.Host.Core/Hotkeys/ModifierToggleStateMachine.cs`
- Create: `apps/host/Keyina.Host.Tests/HotkeyStateMachineTests.cs`

**Interfaces:**

```csharp
[Flags]
public enum HotkeyModifiers { None = 0, Control = 1, Shift = 2, Alt = 4, Windows = 8 }
public readonly record struct HotkeyChord(HotkeyModifiers Modifiers, VirtualKey Key);
public enum HotkeyCommand { None, ToggleVietnamese, PushToTalkPressed, PushToTalkReleased, ToggleDictation, CancelDictation }
public sealed class ModifierToggleStateMachine
{
    public HotkeyCommand Process(in KeyboardTransition transition);
}
```

- [x] **Step 1: Add failing tests for left/right Ctrl+Shift press/release, either release order, auto-repeat, unrelated key cancellation, Alt/Windows contamination, lost-key reset, and no double-toggle.**
- [x] **Step 2: Run tests and verify missing contracts.**
- [x] **Step 3: Implement a finite-state machine with no heap allocation after construction.**
- [x] **Step 4: Add validation for default registered chords: `Ctrl+Alt+Space`, `Ctrl+Alt+V`, and `Escape`.**
- [x] **Step 5: Run tests and commit as `feat(hotkeys): add familiar input toggle state machine`.**

### Task 2: Snippet schema, matcher, and dynamic variables

**Files:**
- Create: `apps/host/Keyina.Host.Core/Snippets/SnippetDefinition.cs`
- Create: `apps/host/Keyina.Host.Core/Snippets/SnippetContext.cs`
- Create: `apps/host/Keyina.Host.Core/Snippets/SnippetMatcher.cs`
- Create: `apps/host/Keyina.Host.Core/Snippets/SnippetVariableExpander.cs`
- Create: `apps/host/Keyina.Host.Tests/SnippetMatcherTests.cs`

**Interfaces:**

```csharp
public sealed record SnippetDefinition(
    string Trigger,
    string Expansion,
    bool CaseSensitive,
    bool PreserveDelimiter,
    IReadOnlySet<char> Delimiters,
    IReadOnlySet<string> AllowedApplications,
    IReadOnlySet<string> ExcludedApplications);

public readonly record struct SnippetContext(
    string ApplicationId,
    bool SecureInput,
    DateTimeOffset Now);

public sealed record SnippetMatch(int EraseCodePoints, string InsertText, bool PreserveDelimiter);
public sealed class SnippetMatcher
{
    public SnippetMatch? Match(ReadOnlySpan<char> token, char delimiter, in SnippetContext context);
}
```

- [x] **Step 1: Add failing tests for exact trigger, delimiter, case policy, secure input, allow/deny apps, maximum lengths, duplicate triggers, Unicode triggers, built-ins, and date/time variables.**
- [x] **Step 2: Verify missing implementation.**
- [x] **Step 3: Implement indexed lookup without regex and validate all definitions at construction.**
- [x] **Step 4: Add built-ins `;kvi`, `;kvoice`, `;kdate`, `;ktime`, and `;kdatetime` as commands or expansions without hardcoding provider/network behavior.**
- [x] **Step 5: Run tests and commit as `feat(snippets): add deterministic scoped expansion`.**

### Task 3: Versioned snippet configuration and atomic storage

**Files:**
- Create: `apps/host/Keyina.Host.Core/Configuration/KeyinaConfiguration.cs`
- Create: `apps/host/Keyina.Host.Core/Configuration/ConfigurationValidator.cs`
- Create: `apps/host/Keyina.Host/Configuration/AtomicConfigurationStore.cs`
- Create: `apps/host/Keyina.Host.Tests/ConfigurationStoreTests.cs`

**Interfaces:**
- JSON schema version 1.
- Unknown properties, unknown versions, invalid enum values, duplicate triggers, secret-like fields, and oversized values are rejected.
- Save uses write-through temporary file, flush-to-disk, atomic replace, and restrictive current-user ACL where supported.

- [ ] **Step 1: Add failing tests for valid round-trip, unknown fields/version, malformed UTF-8/JSON, duplicate triggers, secret fields, interrupted temp file, and atomic replacement.**
- [ ] **Step 2: Implement strict DTO parsing and validation.**
- [ ] **Step 3: Implement async atomic load/save without exposing partial files.**
- [ ] **Step 4: Run tests and commit as `feat(config): persist snippets atomically`.**

### Task 4: Bounded IPC framing and session validation

**Files:**
- Create: `apps/host/Keyina.Host.Core/Ipc/IpcMessageType.cs`
- Create: `apps/host/Keyina.Host.Core/Ipc/IpcEnvelope.cs`
- Create: `apps/host/Keyina.Host.Core/Ipc/IpcFrameCodec.cs`
- Create: `apps/host/Keyina.Host.Core/Ipc/IpcSessionValidator.cs`
- Create: `apps/host/Keyina.Host.Tests/IpcFrameCodecTests.cs`
- Create: `platform/windows/tsf/include/keyina/ipc_protocol.h`
- Create: `platform/windows/tsf/src/ipc_protocol.cpp`
- Create: `tests/windows/ipc_protocol_test.cpp`

**Interfaces:**
- Frame header: magic `KYNA`, protocol version `1`, message type `uint16`, flags `uint16`, payload length `uint32`, 16-byte session ID, `uint64` focus generation, then UTF-8 payload.
- Maximum complete frame: 65,536 bytes.
- C# and C++ test vectors must be byte-identical.

- [x] **Step 1: Add failing C# tests for round-trip, partial buffers, invalid magic/version/type/UTF-8, oversize, stale session, and focus generation.**
- [x] **Step 2: Implement allocation-bounded C# codec using spans and explicit little-endian fields.**
- [x] **Step 3: Check in one shared golden frame vector and add failing C++ decoder tests.**
- [x] **Step 4: Implement C++ decoder/encoder and verify byte identity with the C# vector.**
- [x] **Step 5: Run .NET/native tests and commit as `feat(ipc): add bounded host TSF protocol`.**

### Task 5: Windows hotkey adapters

**Files:**
- Create: `apps/host/Keyina.Host.Windows/Keyina.Host.Windows.csproj`
- Create: `apps/host/Keyina.Host.Windows/Hotkeys/RegisteredHotkeyManager.cs`
- Create: `apps/host/Keyina.Host.Windows/Hotkeys/ModifierKeyboardHook.cs`
- Create: `apps/host/Keyina.Host.Windows/Hotkeys/HotkeyMessageWindow.cs`
- Create: `apps/host/Keyina.Host.Tests/RegisteredHotkeyManagerTests.cs`
- Modify: `Keyina.slnx`

**Interfaces:**
- `RegisterHotKey` handles non-modifier-only chords.
- `WH_KEYBOARD_LL` handles bare Ctrl+Shift release semantics only.
- Both adapters expose bounded command events and registration diagnostics without key content.

- [ ] **Step 1: Add P/Invoke contract tests and a fake native API for successful registration, conflict, duplicate configuration, dispose, and hook callback transitions.**
- [ ] **Step 2: Implement hidden message window and deterministic registration lifecycle.**
- [ ] **Step 3: Implement low-level hook that delegates only modifier transitions to `ModifierToggleStateMachine` and posts commands.**
- [ ] **Step 4: Add `--hotkey-self-test` that registers unique temporary chords, dispatches a message, unregisters, and exits without modifying user settings.**
- [ ] **Step 5: Run tests and commit as `feat(windows): register Keyina hotkeys safely`.**

### Task 6: Snippet and IPC benchmarks

**Files:**
- Create: `apps/host/Keyina.Host.Benchmarks/Keyina.Host.Benchmarks.csproj`
- Create: `apps/host/Keyina.Host.Benchmarks/Program.cs`
- Create: `tools/compare_host_benchmark.py`
- Modify: `Keyina.slnx`
- Modify: `.github/workflows/ci.yml`

- [ ] **Step 1: Add benchmark cases for 10,000-snippet lookup, hotkey transition, 4 KiB IPC encode/decode, and variable expansion.**
- [ ] **Step 2: Emit stable JSON environment metadata and median/p95/p99/max values.**
- [ ] **Step 3: Enforce p99 budgets from the design and 20% reviewed-baseline regression policy.**
- [ ] **Step 4: Add CI build/test/benchmark artifact steps and commit as `perf(host): benchmark hotkeys snippets and IPC`.**

### Task 7: Integration evidence

**Files:**
- Modify: `README.md`
- Create: `docs/compatibility/hotkeys-snippets-ipc.md`
- Modify: `docs/superpowers/specs/2026-07-29-keyina-host-speech-brand-design.md` only if implementation evidence requires a clarified contract.

- [ ] **Step 1: Run fresh Debug/Release .NET and native suites.**
- [ ] **Step 2: Run hotkey self-test, host benchmarks, shared IPC vectors, and brand regeneration.**
- [ ] **Step 3: Scan for raw keys, snippet content diagnostics, secrets, unrestricted pipe ACL, and generated user paths.**
- [ ] **Step 4: Record verified and blocked gates; do not claim full tray UI or speech completion.**
- [ ] **Step 5: Commit as `docs: record hotkey snippet and IPC evidence`.**
