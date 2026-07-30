# Keyina free selection translation implementation plan

**Goal:** Add opt-in DeepL selection translation on `Ctrl + Alt + T` while preserving focus, clipboard contents, typing-hook isolation, and existing schema compatibility.

**Architecture:** Provider-neutral core contracts feed a bounded DeepL HTTP adapter. A coordinator combines a focus-safe Windows selection adapter with cancellation and structured failures. The resident runtime integrates the command through existing hotkey, tray, credential-vault, configuration, and Fluent settings surfaces.

## Completed implementation slices

### 1. Contracts and configuration

- [x] Add immutable translation request/result types and stable failure codes.
- [x] Add a fixed target-language catalog including Vietnamese.
- [x] Reject blank text, unknown targets, and selections over 20,000 characters.
- [x] Add opt-in translation configuration without changing schema version 1.
- [x] Prove older schema-one files receive disabled/`EN-US` defaults.

### 2. DeepL adapter

- [x] Select Free or Pro endpoint from the key suffix.
- [x] Send documented JSON and authentication headers.
- [x] Prefer the quality-optimized model when available.
- [x] Protect code, URLs, email, paths, placeholders, flags, and identifiers through deterministic XML keep tags.
- [x] Use DeepL XML tag handling v2 and reject missing, duplicate, or unknown protected tokens.
- [x] Skip network and quota use for token-only selections.
- [x] Enforce an eight-second timeout and bounded request/response payloads.
- [x] Map authentication, rate-limit, quota, malformed response, timeout, and network failures.
- [x] Cover the adapter with an in-memory HTTP handler; make no live API calls in tests.

### 3. Selection transaction and coordinator

- [x] Snapshot and restore the clipboard around marked `Ctrl+C` selection capture.
- [x] Retry transient clipboard contention briefly.
- [x] Capture and verify the foreground window identity.
- [x] Insert translated Unicode through the existing injection marker.
- [x] Cancel superseded or Escape-cancelled operations.
- [x] Convert unexpected clipboard/provider failures into content-free stable outcomes.

### 4. Resident runtime integration

- [x] Add `TranslateSelection` and `VirtualKey.T`.
- [x] Register `Ctrl + Alt + T` as best-effort global hotkey ID 4.
- [x] Preserve required hotkeys and the typing hook when the optional translation chord conflicts.
- [x] Expose translation-hotkey conflict status while retaining a target-aware tray command.
- [x] Publish start, completion, cancellation, and localized failure feedback through the existing no-focus overlay/audio coordinator without content leakage.
- [x] Store the DeepL key at `Keyina/DeepL/ApiKey` in Windows Credential Manager.
- [x] Inject provider/accessor/vault dependencies for deterministic runtime tests.
- [x] Dispose coordinator and shared HTTP client with the host.

### 5. Settings and documentation

- [x] Add a localized **Dịch nhanh** settings page.
- [x] Add enable toggle, target-language selector, masked key input, save/delete actions, quota copy, shortcut copy, and sensitive-data warning.
- [x] Add translation to the deterministic settings screenshot gallery.
- [x] Document setup, use, safety limits, key storage, cancellation, and privacy in `docs/translation.md` and `README.md`.

## Verification commands

```powershell
dotnet build apps/host/Keyina.Host.Tests/Keyina.Host.Tests.csproj -c Debug --no-restore
apps/host/Keyina.Host.Tests/bin/Debug/net10.0-windows10.0.19041.0/Keyina.Host.Tests.exe
git diff --check
git status --short
git diff --stat
```

Expected translation-specific result: all translation, hotkey, credential, runtime, settings, clipboard, and provider tests pass with zero compiler warnings. Repository-root asset tests may require a normal checkout instead of a managed worktree; unrelated feedback-overlay focus failures remain outside this feature scope.
