# Typing compatibility

This document describes the intended safety boundaries of Keyina's default resident keyboard-hook backend. It is not yet a stable compatibility guarantee.

## Default behavior

Keyina listens for supported physical key events through `WH_KEYBOARD_LL`, sends ordinary text keys to the native engine, and applies only the minimal required edit using Backspace plus Unicode `SendInput`.

Injected events carry a private marker and must bypass Keyina to prevent recursion. Composition state is bounded and reset whenever continuing would be unsafe or ambiguous. Physical Backspace always resets active Telex state and passes through unchanged, so the target application deletes exactly one visible character; Keyina never uses Backspace to remove a tone or vowel-shape modifier. With invalid-Latin restoration enabled, repeated `ss` remains literal in words such as `loss`, `lossless`, `classless`, and `assessment`; the short Telex escape `ass` still produces `as`.

The default backend does not require a Windows language profile, TSF registration, or `Win + Space`.

## Expected support

The primary target is a normal unelevated text field that accepts standard Windows keyboard input, including common Win32, WinForms, WPF, WinUI, browser, and editor controls.

Automated coverage exercises engine behavior, injection planning, modifier bypass, injected-event filtering, focus changes, navigation boundaries, disable/enable transitions, and representative live-hook integration. Normal local CTest and managed-test runs exclude foreground/real-hook tests. Desktop-interactive native tests are registered only with `KEYINA_ENABLE_INTERACTIVE_DESKTOP_TESTS=ON`; their state guard restores the prior foreground control and cursor, and cleans up partially sent modifier or mouse-button events. Manual application coverage remains an active release gate.

## Literal pass-through and reset boundaries

Keyina should reset active composition and pass input through unchanged when it detects or cannot safely exclude:

- Ctrl, Alt, or Windows-key shortcuts.
- Navigation, selection, focus, desktop, or foreground-window changes.
- Password and secure input controls.
- A target process running at a higher integrity level.
- Raw-input, game, remote, or otherwise incompatible input paths.
- Native-engine, hook, injection, or context-detection failure.
- An event already injected by Keyina.
- Physical Backspace, including key repeat while the key is held.

Failing open means the physical key reaches the target application even when Vietnamese transformation is skipped.

## Known limitations

- Unsigned development builds can be blocked or flagged by Windows security controls.
- A non-elevated Keyina process cannot safely inject into a higher-integrity application.
- Games, anti-cheat software, remote desktops, virtual machines, terminal emulators, accessibility tools, and applications with custom text stacks need broader manual validation.
- Selection-aware replacement is intentionally conservative; navigation or uncertain selection state resets composition.
- Some applications may handle synthetic Unicode or Backspace differently from standard Windows controls.
- The optional legacy TSF backend can be built with `KEYINA_BUILD_TSF=ON`, but it is not the default path and is not required for ordinary operation.

## Manual verification checklist

For every application added to the compatibility matrix, verify at least:

1. Start Keyina without selecting a separate Windows input language.
2. Type representative Telex words and typo corrections, including `nhaatso` → `nhấto` → Backspace → `nhất`; confirm Backspace deletes the visible character and never rolls back a Telex tone or vowel-shape key.
3. Type URLs, email addresses, source identifiers, paths, commands, mixed English/Vietnamese tokens, and repeated-`s` Latin words such as `loss`, `lossless`, and `assessment`.
4. Exercise Ctrl/Alt/Windows shortcuts, arrows, Home/End, selection, mouse focus changes, and undo.
5. Confirm password fields receive literal input and do not expose transformed text.
6. Toggle Vietnamese mode repeatedly and confirm no stale composition remains.
7. Confirm exiting or crashing Keyina does not leave the keyboard blocked.

Record the Windows build, Keyina commit, application version, architecture, privilege level, test result, and any workaround.

## Reporting compatibility defects

Use the repository bug form and include:

- Exact physical keystrokes.
- Expected and actual text.
- Target application and version.
- Windows version and architecture.
- Whether the issue reproduces in Notepad.
- Whether the target is elevated, remote, sandboxed, or using raw input.

Never post passwords, API keys, private documents, or other sensitive text.
