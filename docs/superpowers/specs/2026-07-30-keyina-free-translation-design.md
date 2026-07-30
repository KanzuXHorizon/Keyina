# Keyina free selection translation design

## Goal

Add an opt-in global action that translates the text currently selected in another Windows application and replaces that selection without activating a Keyina window.

## Product decision

DeepL API Free is the first provider because it supports Vietnamese, automatic source-language detection, a documented low-latency translation API, and a 500,000-character monthly free allowance. The provider is isolated behind `ITranslationProvider` so later cloud or offline engines do not require changes to hotkeys, selection capture, configuration, or UI.

Translation remains disabled by default. Keyina ships no shared API key. The user must configure a personal DeepL key in Windows Credential Manager and explicitly enable the feature. The settings UI warns that selected content is sent to DeepL and that the Free API must not be used for personal, confidential, or sensitive material.

## User flow

1. The user selects text in the foreground application.
2. The user presses `Ctrl + Alt + T` or chooses the tray command.
3. Keyina snapshots the clipboard, issues a marked `Ctrl+C`, reads the Unicode selection, and restores the clipboard.
4. Keyina sends one bounded DeepL request for the configured target language.
5. Keyina verifies that both the foreground window and focused control are still the original capture targets.
6. Keyina replaces the selection using the existing marked Unicode injection path.

`Escape` cancels an active translation. A newer translation command supersedes the older one. No partial result is inserted. Start, completion, cancellation, and failure use the existing no-focus feedback coordinator. Feedback carries only status and target-language names; automatic mode suppresses visual overlay for fullscreen-like applications.

## Architecture

### Core contracts

`Keyina.Host.Core/Translation` owns:

- translation request and result records;
- stable failure codes;
- supported target-language metadata;
- request validation;
- the provider interface.

It has no dependency on HTTP, Win32, clipboard, or WinForms.

### Technical-token protection

`TranslationTextProtector` detects fenced and inline code, URLs, email addresses, Windows paths, template placeholders, command flags, method calls, and path-like identifiers. It emits deterministic XML `keep` tags and records the original values in memory. DeepL XML tag handling v2 ignores those tags during translation. Restoration requires every token ID exactly once and substitutes the original value rather than trusting provider output. Missing, duplicate, unknown, or malformed tags reject the result. Token-only selections skip the network entirely.

### DeepL adapter

`DeepLTranslationProvider`:

- selects `https://api-free.deepl.com/v2/translate` for keys ending in `:fx` and the Pro endpoint otherwise;
- sends JSON with `Authorization: DeepL-Auth-Key ...`;
- uses `model_type=prefer_quality_optimized`;
- enforces an eight-second timeout;
- limits response JSON to 256 KiB;
- enables XML tag handling v2 and `ignore_tags` when technical tokens are present;
- preserves source formatting and rejects protected payloads beyond the provider request bound;
- maps HTTP 403, 429, and 456 to authentication, rate-limit, and quota failures;
- never retries automatically;
- never exposes source text, translated text, or credentials in errors.

### Selection adapter

`ClipboardSelectionAccessor` captures both the foreground window and focused-control handles, snapshots and restores the clipboard, and reads Unicode selection text after a marked `Ctrl+C` injection. Clipboard access retries briefly for transient Windows clipboard contention. It does not clear the clipboard before copying.

Replacement succeeds only if both handles still match the capture. Result insertion uses `UnicodeInputInjector`, whose private marker prevents the Vietnamese keyboard hook from reprocessing injected text.

### Coordinator

`TranslationCoordinator` owns cancellation and sequencing. It captures, translates, validates, focus-checks, and replaces in that order. It converts all provider and selection failures into structured outcomes without retaining content.

### Runtime and settings

`KeyinaApplicationContext` owns one shared HTTP client, provider, accessor, and coordinator. It registers `Ctrl + Alt + T` as a best-effort optional hotkey, exposes a tray command, reads the DeepL key only when a translation starts, and disposes all translation resources with the host. A translation-hotkey conflict does not roll back the required hotkeys or prevent the Vietnamese typing hook from starting; settings expose the conflict and the tray command remains available.

Schema version remains `1`. Additive settings use safe defaults:

- `TranslationEnabled = false`
- `TranslationTargetLanguage = "EN-US"`

The credential target is `Keyina/DeepL/ApiKey`; no secret is serialized into `settings.json`.

## Limits and failure behavior

- Empty selection: no network request and no replacement.
- More than 20,000 Unicode characters: reject before network access.
- Missing or rejected key: stable credential/authentication error.
- Rate limit or quota exhaustion: stable provider error.
- Timeout, malformed response, clipboard failure, or network failure: stable unavailable/invalid response error.
- Foreground window or focused control changed: discard the result and do not type.
- Cancellation: no error and no replacement.
- `Ctrl + Alt + T` conflict: keep required hotkeys and typing active; expose shortcut status and retain the tray action.

## Privacy and security

- Ordinary Vietnamese typing remains offline.
- Translation is disabled until explicitly enabled.
- Only the explicitly selected text is sent.
- The clipboard is restored in `finally`.
- Source text, translated text, clipboard contents, and credentials are never logged or persisted.
- Endpoints are fixed HTTPS DeepL endpoints in the first release.
- The API key is stored only in Windows Credential Manager.

## Verification

Automated tests cover:

- language and configuration validation;
- schema-one backward compatibility;
- DeepL endpoint selection, headers, JSON, parsing, limits, and status mapping through an in-memory HTTP handler;
- cancellation, supersession, foreground/focused-control changes, provider failure, unexpected clipboard failure, and successful replacement;
- clipboard restoration, no pre-copy clearing, and transient contention;
- hotkey, credential target, tray, runtime dispatch, settings controls, privacy copy, and screenshot gallery.

Automated tests never contact DeepL or require a real credential. A live Notepad smoke test requires a user-owned non-production key and non-sensitive sample text.
