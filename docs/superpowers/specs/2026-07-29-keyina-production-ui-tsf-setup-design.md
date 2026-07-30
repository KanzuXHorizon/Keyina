# Keyina Production UI and TSF Setup Design

## Goal

Transform the current prototype settings window into a polished, professional Windows desktop experience and make the app truthfully verify that Vietnamese typing works end to end before reporting a ready state.

## Product principles

- The interface must look and behave like a deliberate Windows desktop product, not a dashboard template.
- The app must never claim that Vietnamese input is ready unless the TSF profile is installed, enabled, connected to the resident host, and has passed a real typing-path health check.
- Typing remains offline and privacy-first. Speech remains optional and isolated.
- Existing WinForms and native TSF architecture are retained to reduce delivery risk.
- Every visible status must map to a measurable runtime state.

## Scope

### Included

- Redesign the WinForms settings shell, navigation, typography, spacing, cards, states, icons, and responsive behavior.
- Add a guided first-run setup and repair flow for native TSF installation and registration.
- Add truthful readiness diagnostics covering host, TSF registration, active profile, IPC, focused application compatibility, and typing test status.
- Add an in-app typing test page with Telex examples and raw/result diagnostics.
- Improve tray menu, localization, keyboard accessibility, DPI scaling, and error handling.
- Add automated tests for UI models, state mapping, setup flow, diagnostics, and critical layout behavior.

### Excluded

- Migration to WinUI 3, WPF, Electron, or WebView.
- Cloud accounts, telemetry, analytics, or online dependency for ordinary typing.
- Installer signing and public release packaging beyond preparing the app for those later gates.
- Replacing the existing native TSF engine or composition algorithm.

## Information architecture

The settings application uses six primary sections:

1. **Overview** — overall readiness, current input state, focused app, quick actions, and setup/repair CTA.
2. **Typing** — input toggle, Telex configuration, tone placement, Context Guard, typing test, and composition diagnostics.
3. **Speech-to-text** — provider state, credentials, microphone, language, hotkeys, and opt-in privacy explanation.
4. **Hotkeys** — editable shortcuts with conflict detection and reset actions.
5. **Snippets** — deterministic snippet list, search, enable/disable, edit, and validation.
6. **Diagnostics** — component health, logs, version information, copy-report action, and repair actions.

The app opens on Overview. First-run or broken installations elevate Setup as the dominant action without creating a separate permanent navigation item.

## Visual direction

- Native Windows 11 visual language with restrained surfaces, clear hierarchy, rounded cards, subtle separators, and minimal decorative chrome.
- Window minimum size of 900×620 logical pixels and a preferred initial size around 1080×720.
- Sidebar width between 208 and 232 logical pixels. Content uses a maximum readable width while still filling large windows.
- Segoe UI Variable or Segoe UI fallback. Headings, body, labels, and status text use a consistent type scale.
- One accent color derived from the Keyina brand. Status colors are reserved for success, warning, and error semantics.
- Dark and light themes follow Windows. High-contrast mode remains usable.
- No clipped cards, hidden text, or fixed-position content at 100%, 125%, 150%, 175%, or 200% DPI.

## Overview design

The header contains the page title, a concise product statement, and a global readiness badge.

The primary readiness panel has four measurable states:

- **Ready** — TSF registered, profile enabled, host healthy, IPC connected, and typing test passed.
- **Needs setup** — one or more required components are absent or unregistered.
- **Needs attention** — installation exists but runtime connection or activation is broken.
- **Unavailable** — unsupported OS, missing required runtime, or unrecoverable validation failure.

The panel presents one primary action appropriate to the state: Set up, Repair, Test typing, or Open Windows language settings.

Secondary status cards cover:

- Vietnamese input mode.
- Dictation availability.
- Resident host state.
- Focused application and TSF compatibility.

Cards must show status, one-sentence explanation, and an actionable control when appropriate. They must never show stale optimistic labels.

## Setup and repair flow

The guided setup flow is state-driven and resumable:

1. Validate operating system and architecture.
2. Locate the native TSF DLL and verify expected build metadata.
3. Request elevation only when registration requires it.
4. Register COM and TSF language profile.
5. Verify registration through Windows APIs or registry-backed system state.
6. Activate or guide the user to activate the profile in Windows language settings.
7. Start or reconnect the resident host and named-pipe endpoint.
8. Run a focused typing-path health check.
9. Present success only when all required checks pass.

Each step exposes pending, running, success, warning, and failure states. Failures include a plain-language explanation, technical detail, retry action, and repair guidance. Partial success is preserved so reruns continue from the first incomplete step.

Destructive registration changes require explicit confirmation. The app does not silently alter unrelated language profiles.

## Typing page

The Typing page includes:

- Vietnamese input on/off control.
- Telex input method selector with future-safe model boundaries.
- Modern versus traditional tone placement.
- Context Guard toggle and explanation.
- Inline typing test field with example prompts such as `tieengs Vieetj` and expected output `tiếng Việt`.
- Live result banner indicating pass, partial, or fail.
- Optional diagnostics drawer showing raw keys, normalized composition, emitted edit, TSF connection, and focused control capability.

The test must exercise the real TSF path rather than calling the engine directly. A direct-engine self-test may be shown separately as a diagnostic and must not count as end-to-end readiness.

## Diagnostics model

A single immutable health snapshot drives UI state. It includes:

- Host process and version.
- Native DLL presence and version.
- COM registration state.
- TSF profile registration and activation state.
- IPC endpoint state.
- Focused application identity and compatibility classification.
- Last end-to-end typing test result and timestamp.
- Speech provider, credential, microphone, and network-independent typing status.

Health checks run asynchronously, support cancellation, and never block the UI thread. Slow or privileged checks report progress. Sensitive values such as credentials and raw typed content are excluded from logs and copied reports.

## Tray experience

The tray menu mirrors the same runtime truth model. It contains:

- Current Vietnamese input state.
- Toggle Vietnamese input.
- Start or stop dictation when available.
- Open settings.
- Run setup or repair when required.
- Start with Windows.
- Exit.

Double-click opens Overview. Tray icon and tooltip reflect enabled, disabled, listening, warning, and error states.

## Localization and copy

Vietnamese is the default UI language for the first production-ready build, with English available through a language selector. All user-facing strings are extracted from controls into localization resources. Technical identifiers remain unchanged.

Copy must be direct and truthful. Terms such as "Ready", "Enabled", and "Connected" are used only when their associated checks pass.

## Accessibility

- Full keyboard navigation and visible focus indicators.
- Logical tab order per page.
- Accessible names and descriptions for controls and status badges.
- Minimum target size appropriate for desktop interaction.
- Contrast suitable for normal, dark, and high-contrast themes.
- Reduced-motion behavior for progress and state transitions.
- Screen-reader announcements for setup progress and final results.

## Error handling

- UI-thread exceptions are surfaced through a recoverable error boundary and written to a local diagnostic log.
- Setup operations produce structured error codes and human-readable messages.
- Privilege cancellation is treated as a user decision, not a crash.
- IPC loss degrades typing status and offers reconnect or repair.
- Speech failures never disable ordinary Vietnamese input.
- Configuration writes remain atomic and recover from invalid files using explicit backup and reset behavior.

## Testing and verification

### Automated

- State-to-UI mapping tests for every readiness state.
- Setup step transition tests, including elevation cancellation and partial completion.
- TSF registration and health-check abstraction tests using fakes.
- End-to-end typing-test coordinator tests.
- Localization key completeness tests.
- Keyboard navigation and accessible-name assertions where WinForms APIs permit.
- Screenshot-renderer checks at multiple window sizes and DPI scales.
- Existing host, speech, native engine, IPC, and benchmark suites remain green.

### Manual Windows matrix

- Windows 10 22H2 and supported Windows 11 versions.
- DPI 100%, 125%, 150%, 175%, and 200%.
- Light, dark, and high-contrast themes.
- Notepad, Word, Chrome, Edge, VS Code, Windows Terminal, and at least one Electron chat application.
- Sleep/resume, restart, host crash/restart, and switching input languages.
- Keyboard-only navigation and screen-reader smoke test.

## Acceptance criteria

1. No visible clipping or overlap at supported window sizes and DPI scales.
2. The UI is fully usable in Vietnamese and English.
3. Overview reports Ready only after all required health checks and a real TSF typing test pass.
4. A clean machine can complete setup through the guided flow with elevation requested only when necessary.
5. A broken registration or IPC state can be detected and repaired from the app.
6. The in-app typing test confirms that `tieengs Vieetj` can become `tiếng Việt` through the real focused TSF path.
7. Ordinary typing remains offline, responsive, and independent of speech-provider availability.
8. Existing native and host tests continue to pass with no new warnings.
9. Tray, settings, setup, and diagnostics share the same health model and cannot disagree about readiness.
10. The final UI looks cohesive, branded, professional, and intentionally Windows-native rather than generic or web-like.
