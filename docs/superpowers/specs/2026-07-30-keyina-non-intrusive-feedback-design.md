# Keyina Non-Intrusive Shortcut Feedback Design

## Purpose

Give users immediate, trustworthy confirmation that Keyina received a shortcut or changed state without stealing focus, interrupting typing, or degrading games and full-screen applications.

The selected design is adaptive multimodal feedback: a compact no-activate overlay plus a short local sound in normal desktop use, automatically reduced to audio-only when the foreground application occupies essentially the full monitor. The user can override this with visual-only, audio-only, or disabled modes.

## Product requirements

- Toggling Vietnamese input provides clear enabled or disabled feedback.
- Dictation start, listening, finalizing, success, cancellation, and failure states can be represented without opening Settings or a notification balloon.
- Feedback must never call `Activate`, `Focus`, `Select`, `SetForegroundWindow`, or otherwise change the foreground window.
- Overlay windows are click-through, absent from the taskbar and Alt+Tab, and shown with no activation.
- Borderless or exclusive full-screen applications default to audio-only to avoid visual interference and unreliable overlay behavior.
- Feedback failures are best-effort diagnostics only. They must not fail a hotkey, typing operation, or dictation command.
- No sound is played for individual keystrokes, Telex transformations, snippet characters, or partial transcript updates.
- All feedback is generated locally. No content, transcript, foreground title, or application name is recorded or uploaded.
- The user can choose `Automatic`, `Visual only`, `Audio only`, or `Off` from the Hotkeys settings page and preview the result.
- Existing schema-version-1 configuration files remain loadable without migration prompts.

## Selected interaction

### Automatic mode

- Normal desktop window: show the overlay and play a short sound.
- Foreground window covering at least 98% of its monitor: play sound only.
- Foreground detection failure: play sound only.
- Windows high-contrast or reduced-motion conditions: keep the overlay static and prioritize legibility.

### Event presentation

| Event | Overlay copy | Sound | Lifetime |
|---|---|---|---:|
| Vietnamese enabled | `Tiếng Việt đã bật` | rising soft cue | 900 ms |
| Vietnamese disabled | `Tiếng Việt đã tắt` | falling soft cue | 900 ms |
| Dictation connecting | `Đang kết nối…` | start cue | persistent state |
| Dictation listening | `Đang nghe` | none after entry | persistent state |
| Dictation finalizing | `Đang hoàn tất…` | none | persistent state |
| Dictation inserted | `Đã nhập nội dung` | success cue | 1,200 ms |
| Dictation cancelled | `Đã hủy nhập giọng nói` | cancel cue | 1,200 ms |
| Recoverable command failure | short localized recovery copy | low error cue | 2,400 ms |

This slice does not display raw transcript text. That avoids accidental content exposure over games, streams, screen sharing, and presentations. Transcript overlay can be designed separately with explicit privacy controls.

## Architecture

### `FeedbackPreferences`

A core configuration value containing the selected mode. The enum is serialized as a stable numeric value by the existing configuration store. Deserialization of older schema-version-1 files uses the default `Automatic` value when the property is absent.

### `FeedbackEvent`

A small immutable value with event kind, localized message, visual tone, and sound cue. It contains no keystroke, transcript, process title, or document content.

### `FeedbackPresentationPolicy`

A pure policy maps preferences and foreground presentation state to two booleans: show overlay and play sound. It is independently unit-tested and contains no UI calls.

### `WindowsForegroundPresentationProbe`

Uses `GetForegroundWindow`, `GetWindowRect`, `MonitorFromWindow`, and `GetMonitorInfo` to compare the foreground rectangle with the monitor rectangle. It reports `FullscreenLike` when width and height coverage are each at least 98%, allowing a small tolerance for borders and scaling.

The probe never reads process names, executable paths, window titles, or input content.

### `WindowsFeedbackSoundPlayer`

Uses `PlaySound` with in-memory PCM WAV data, `SND_ASYNC`, `SND_MEMORY`, `SND_NODEFAULT`, `SND_NOSTOP`, and `SND_SYSTEM`. Four short cues are generated once in memory using simple sine pairs and a smooth fade envelope. Playback is fire-and-forget and never awaited by the shortcut path.

A failed native call is ignored and surfaced only through optional diagnostics. It must not fall back to a Windows default beep.

### `NoActivateFeedbackOverlay`

A single reusable WinForms form with:

- `ShowWithoutActivation = true`;
- `WS_EX_NOACTIVATE`, `WS_EX_TOOLWINDOW`, `WS_EX_LAYERED`, and `WS_EX_TRANSPARENT`;
- `ShowInTaskbar = false`, no border, no caption, no input controls;
- `WM_NCHITTEST` returning `HTTRANSPARENT`;
- display via `ShowWindow(SW_SHOWNOACTIVATE)` and reposition via `SetWindowPos(..., SWP_NOACTIVATE)`;
- placement near the bottom center of the monitor containing the foreground window;
- one timer-driven lifetime and fade, with latest-event replacement rather than an unbounded queue.

The visual language extends the existing Fluent settings UI: compact 12–16 px radius, one elevation treatment, Segoe UI Variable, status glyph plus concise copy, and theme-aware neutral/accent surfaces. Motion is limited to one fast ease-out entrance and a short fade; reduced-motion disables movement.

### `FeedbackCoordinator`

Receives semantic events from the application context, resolves the policy, and dispatches overlay and sound independently inside exception boundaries.

- Duplicate events inside 150 ms are coalesced.
- A newer transient event replaces an older one.
- Persistent dictation state replaces the previous state without stacking.
- Overlay and sound exceptions are swallowed after optional trace reporting.
- Disposal closes the overlay and stops playback without touching the foreground application.

## Runtime integration

- `SetVietnameseEnabledAsync` publishes enabled or disabled feedback after state and hook configuration are updated.
- Dictation overlay model state changes are translated into semantic feedback on the UI dispatcher.
- Command validation failures such as disabled speech, missing credentials, or no focused target publish a recoverable failure event without changing the existing host-state semantics.
- Settings changes update `FeedbackPreferences` immediately and persist them through the existing atomic configuration store.
- The preview button invokes the coordinator with a preview event and does not save unrelated settings.

## Settings UX

The Hotkeys page gains one feedback card below the shortcut list and above registration status.

- Heading: `Phản hồi khi dùng phím tắt`
- Description: `Xác nhận lệnh bằng lớp phủ không chiếm focus và âm thanh ngắn.`
- Mode selector: `Tự động`, `Chỉ hình ảnh`, `Chỉ âm thanh`, `Tắt`
- Preview action: `Thử phản hồi`
- Context note: `Ở game hoặc ứng dụng toàn màn hình, chế độ Tự động chỉ phát âm thanh.`

The controls use existing Fluent components, expose accessible names, remain keyboard navigable, and do not create a separate settings page.

## Error handling and safety

- Feedback never changes `HostState` to failed.
- Feedback never blocks the keyboard-hook callback or waits for audio playback.
- Native API failure returns a conservative audio-only policy or no-op presentation.
- Overlay creation is lazy; startup remains functional when graphics initialization fails.
- Disposal is idempotent and does not throw during process shutdown.
- No balloon notifications, toast activation, clipboard access, global input capture, process-name heuristics, or game-specific allowlists are introduced.

## Testing

### Unit tests

- Automatic mode chooses overlay plus sound for a normal window.
- Automatic mode chooses sound only for a full-screen-like window.
- Explicit visual, audio, and disabled modes override foreground state.
- Foreground coverage tolerance handles exact, near-full, and ordinary rectangles.
- Duplicate feedback inside the debounce window is suppressed.
- Older schema-version-1 JSON without feedback settings loads as `Automatic`.
- Invalid feedback enum values are rejected by configuration validation.
- Sound generation produces valid RIFF/WAVE PCM data with bounded duration.

### Windows integration tests

- Showing the overlay does not change `GetForegroundWindow`.
- Overlay extended styles include no-activate and tool-window flags.
- Overlay is not in the taskbar and is click-through by hit testing.
- Repeated show calls reuse one form and replace content.
- Disposing the coordinator closes the overlay safely.

### UI tests

- Hotkeys page contains the mode selector, preview action, explanatory note, and accessible names.
- Applying a settings snapshot selects the persisted feedback mode.
- Changing the selector calls the settings action exactly once.
- Preview calls the preview action and leaves the persisted mode unchanged.

## Verification gates

- Focused feedback tests fail before implementation and pass afterward.
- `dotnet build Keyina.slnx -c Release` succeeds with zero errors.
- All host tests pass.
- Existing native tests remain green because the native typing engine is untouched.
- Settings screenshot gallery renders successfully.
- Final diff contains no raw transcript display, per-keystroke sound, activation API, process-name game detection, or unrelated refactoring.

## Success criteria

The feature is complete when shortcut and dictation state changes receive a polished local confirmation, normal applications never lose focus, full-screen applications avoid overlays by default, users can configure or disable feedback, old settings files remain compatible, and feedback failure cannot break typing or hotkeys.
