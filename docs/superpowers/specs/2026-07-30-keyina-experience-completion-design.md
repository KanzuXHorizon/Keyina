# Keyina Experience Completion Design

## Goal

Complete the highest-impact missing daily-use capabilities without adding another resident hook or weakening privacy: configurable shortcuts, first-run guidance, safe settings portability, per-application controls, reversible translation, provider resilience, and a final accessibility/DPI hardening pass.

## Scope and delivery order

The work is split into independently shippable slices. Each slice must build and pass focused tests before the next begins.

1. Configurable shortcuts.
2. First-run setup and health guidance.
3. Safe settings import/export.
4. Per-application exclusions.
5. Reversible translation.
6. Translation provider fallback.
7. Accessibility, DPI, and UX hardening.

## 1. Configurable shortcuts

### Model

`HotkeyPreferences` stores one binding per user-facing command:

- Toggle Vietnamese input: modifier gesture, default `Ctrl + Shift`.
- Push to talk: hold gesture, default `Ctrl + Alt + Space`.
- Toggle dictation: press gesture, default `Ctrl + Alt + V`.
- Translate selection: press gesture, default `Ctrl + Alt + T`.
- Cancel active command: press gesture, default `Escape`.

Bindings are additive optional configuration fields. Existing schema-one files that do not contain them load the defaults. No credential or user text is involved.

### Validation

- Windows-key combinations are rejected.
- Plain letter/digit keys without a modifier are rejected.
- Modifier-only gestures are accepted only for Toggle Vietnamese.
- Hold gestures require a non-modifier key and at least one modifier.
- Chords must be unique across all commands.
- Escape remains valid only for cancellation.
- Formatting and parsing are deterministic and culture-independent.

### Runtime transaction

A shortcut edit is applied as one transaction:

1. Validate the complete candidate set.
2. Unregister the old registered-hotkey set.
3. Attempt to register the complete candidate registered-hotkey set.
4. Reconfigure the shared modifier hook for the modifier gesture and hold release tracking.
5. Persist only after successful runtime application.
6. On failure, restore the previous registrations and hook configuration.

No additional low-level keyboard hook is installed. The existing shared physical-event subscription remains the only source for modifier-only and release events.

### UI

The Phím tắt page shows each command as an editable row with current chord, status, Change, and Restore. A compact capture dialog records the next chord, supports Escape to cancel, validates inline, and returns focus to the edited row. A Restore all action restores defaults. Tray and feature pages read display strings from the same configuration snapshot.

## 2. First-run setup and health guidance

`FirstRunCompleted` defaults to false for new installations and true for existing configuration files. The first-run page is shown only when there is no prior configuration. It explains three optional capabilities without blocking Vietnamese typing:

- Verify typing health.
- Configure speech.
- Configure translation.

The user can skip and return later. Completion is persisted. Health cards link to the exact settings section that resolves the issue.

## 3. Safe settings import/export

Export produces a versioned UTF-8 JSON document containing preferences, snippets, exclusions, hotkeys, language, theme, and feedback settings. It never contains Credential Manager values, transcripts, selected text, paths outside the configuration document, or diagnostics.

Import validates the whole document before replacing configuration. The current configuration is retained when validation fails. Runtime-dependent settings, including shortcuts, are applied transactionally before persistence. The UI explicitly states that API keys are not exported.

## 4. Per-application exclusions

`ApplicationPreferences` stores normalized executable names for:

- Disable Vietnamese typing.
- Disable speech.
- Disable translation.
- Suppress visual feedback.

Matching is case-insensitive and based only on executable file name, never full path. Password/secure-field bypass remains authoritative. The Settings UI provides bounded lists with Add, Remove, and current-application assistance when available. Invalid names, duplicates, wildcards, and paths are rejected.

## 5. Reversible translation

Successful replacement creates an in-memory `TranslationUndoEntry` containing the original and translated text plus foreground/focus identity and a short expiration. It is never persisted or logged.

A configurable Undo translation command defaults to `Ctrl + Alt + Z`. Undo succeeds only when the same foreground window and focused control still own the translated selection or caret context. It restores the original text once, then clears the entry. New translations replace the previous undo entry. Expired or focus-mismatched entries fail safely with no insertion.

Preview mode is optional and defaults off. When enabled, translation is shown in a no-activate preview overlay with Replace, Copy, and Cancel actions reachable from Settings/tray; the global shortcut still avoids stealing focus. Because the existing feedback overlay is intentionally click-through, preview uses a separate explicitly activated window only when the user enables preview mode.

## 6. Translation provider fallback

DeepL remains the default. `TranslationProviderPreferences` supports an ordered list:

1. DeepL API.
2. Optional LibreTranslate-compatible endpoint.

Fallback occurs only for network unavailable, rate limit, or quota exhaustion. Authentication failures and invalid protected-token responses never fall back silently. The LibreTranslate endpoint must be HTTPS, must not resolve to loopback/private/link-local addresses unless the user explicitly enables local endpoint mode, and has the same request/response limits and token-protection validation as DeepL. Credentials remain in Credential Manager. No shared key is shipped.

Offline Argos model embedding is excluded from this delivery because shipping and updating language models would materially change installer size and supply-chain risk. The provider interface remains extensible for a later signed model package.

## 7. Accessibility, DPI, and UX hardening

- Every new control has an accessible name and concise description.
- All actions are keyboard reachable with a logical tab order.
- Dynamic statuses are announced through accessible status controls.
- Settings remains usable at 900×620, 125–200% DPI, and long Vietnamese copy.
- Error copy names the problem and the recovery action.
- Empty, disabled, conflict, success, and validation states are represented.
- Screenshot gallery coverage is extended for first-run and shortcut capture surfaces.

## Security and privacy invariants

- No API key is stored in JSON or exported.
- No selected, translated, dictated, or typed content is logged.
- Import is validate-before-replace and atomic.
- URL fallback validation blocks SSRF by default.
- Secure fields continue to bypass typing and snippets.
- Optional feature failure never disables Vietnamese typing.

## Verification

Each slice receives focused red-green tests. Completion requires:

- Full Debug solution build.
- Full host test suite.
- Release solution build.
- Release benchmarks within existing budgets.
- Live hook tests passing repeatedly.
- Screenshot gallery generation.
- `git diff --check` and secret-shape scan.
