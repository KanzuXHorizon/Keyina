# Adaptive Keystroke Overlay Design

## Goal

Add an optional, modern keystroke overlay that visualizes Vietnamese composition without adding work to Keyina's critical keyboard callback, stealing focus, exposing sensitive input, or consuming resources while idle.

The first release prioritizes the keystroke overlay only. Cursor customization and cursor trails remain separate future work.

## Product decisions

The selected experience is:

- adaptive placement near the text caret with a safe screen-corner fallback;
- privacy-first hybrid content;
- adaptive motion that degrades gracefully under load and reduced-motion settings;
- hybrid visual treatment: individual key tokens transition into one composed-result pill;
- ordinary mode shows text composition only;
- Presentation Mode may additionally show selected function keys and shortcuts;
- per-key sound is optional, disabled by default, and never allowed to delay typing.

## User experience

### Ordinary typing

For a raw sequence such as `n g u y e n`, the overlay presents compact tokens in a single lightweight row. Tokens use a subtle opacity and 3-5 px positional transition rather than exaggerated bounce.

When Keyina transforms the composition, the token row retargets directly into a single composed-result pill. The transition must preserve spatial continuity rather than hiding one view and showing another.

Examples:

```text
n  g  u  y  e  n  ->  nguyên
nguyên             ->  nguyễn
```

Only affected glyphs may receive a brief emphasis. The full pill must not flash, jump, or restart its entrance animation on every transformation.

### Timing

- New tokens appear immediately after the display event reaches the UI worker.
- Active animations retarget to the newest state; they never queue behind old states.
- During rapid typing, durations shorten automatically and intermediate visual-only frames may be dropped.
- After 700-1100 ms without a meaningful update, the pill fades and moves by no more than 2 px before becoming fully hidden.
- Once hidden, no animation timer, composition tick, or continuous rendering loop remains active.

### Adaptive placement

Placement uses this order:

1. reliable TSF caret geometry;
2. reliable Win32 or UI Automation caret geometry;
3. the last stable caret position for the current composition, when still inside the active monitor working area;
4. the configured fallback corner, defaulting to bottom-right.

The positioner chooses above or below the caret based on available space, clamps the overlay to the monitor working area, and avoids covering the current text line when possible.

Small caret movements do not reposition the overlay. A composition keeps a stable visual anchor unless movement crosses a threshold or the active monitor changes.

### Visual language

- compact, rounded pill with a 10-12 px corner radius;
- system font and DirectWrite text rendering;
- restrained solid-translucent surface rather than mandatory acrylic blur;
- one soft shadow that can be disabled in low-power mode;
- no decorative icons, looping gradients, particle effects, or permanent chrome;
- light, dark, high-contrast, and DPI-aware rendering;
- token text is visually subordinate to the composed result;
- no bundled font files.

## Privacy and safety

The renderer must not receive unrestricted raw global keystroke logs.

The typing layer emits a bounded display model containing only the current visible composition state and semantic event kind. The model may include:

- physical token intended for display;
- composition updated;
- composition committed;
- composition cleared;
- presentation shortcut token.

The overlay is suppressed immediately when any of the following applies:

- password or protected input field;
- secure desktop;
- unknown input context that fails the privacy policy;
- explicitly excluded application;
- user-disabled overlay.

Suppression clears pending visual and audio state. It does not reveal password length or substitute mask characters.

No overlay text is persisted to logs, diagnostics, history, clipboard, telemetry, crash metadata, or settings.

Presentation Mode cannot override secure-input suppression.

## Architecture

### 1. Display event producer

The native typing path creates only a minimal, bounded value and posts it to the existing resident message infrastructure. It performs no rendering, sound playback, text measurement, allocation-heavy formatting, or synchronous cross-process call.

Posting failure is fail-open: typing continues and the overlay simply misses that update.

### 2. Overlay state reducer

A pure reducer owns the current visual state:

- visible token sequence;
- composed result;
- semantic transition type;
- privacy state;
- presentation-mode state;
- animation generation number;
- requested anchor.

Every incoming event produces one latest-state snapshot. Older generations can be discarded without replay.

### 3. Privacy policy

A pure policy decides whether text, shortcut tokens, and sound are allowed for the current context. It is evaluated before data reaches the renderer or sound player.

Unknown or contradictory context resolves to suppression.

### 4. Positioning service

A bounded service resolves and stabilizes caret geometry. It does not poll continuously. Geometry is refreshed only when the overlay becomes visible, the foreground context changes, the active monitor changes, or the caret moves beyond the stability threshold.

### 5. Native overlay renderer

Use one reusable Win32 no-activate, non-taskbar overlay window with Direct2D and DirectWrite rendering. Windows composition may be used where it simplifies opacity and transform animation without introducing a heavyweight UI framework.

The renderer owns cached resources:

- device-independent text formats;
- brushes;
- rounded-rectangle geometry;
- measured layouts for the current bounded state;
- device resources recreated only after device loss or DPI/theme change.

The window never takes focus and remains input-transparent in ordinary mode.

### 6. Animation scheduler

Rendering is event-driven. A short-lived scheduler exists only while an animation is active.

Rules:

- newest state supersedes the previous target;
- no unbounded animation queue;
- no fixed 60 FPS loop while idle;
- frame pacing may reduce under load;
- a stale frame may be skipped;
- final state must still be presented;
- reduced-motion mode replaces morph and translation with a short cross-fade or immediate update.

### 7. Settings integration

Add a dedicated Keystroke Overlay settings section with:

- enable overlay;
- preview;
- size;
- surface opacity;
- animation level: Adaptive, Full, Reduced, Off;
- hide delay;
- fallback corner;
- Presentation Mode;
- per-key sound;
- sound theme;
- sound volume;
- per-application exclusion.

Defaults favor normal daily use: overlay off until explicitly enabled, Adaptive motion, bottom-right fallback, Presentation Mode off, and per-key sound off.

## Optional per-key sound

Per-key sound is a complementary sensory feature, not part of the typing contract.

### Behavior

- disabled by default;
- never plays in password, secure, suppressed, or excluded contexts;
- ordinary keys may use one quiet transient;
- composition transformation may use a distinct, softer confirmation transient;
- Backspace, Enter, and shortcuts are available only in Presentation Mode or an explicit expanded sound profile;
- rapid input is rate-limited and coalesced so sound does not become a delayed audio queue;
- sound events can be dropped under load;
- volume is independent from Keyina's command-feedback volume where practical.

### Implementation direction

Do not route high-frequency per-key audio through `MessageBeep` or create a new player/process for every key.

Use a reusable low-latency local audio engine or a dedicated extension of the existing feedback sound infrastructure with:

- predecoded, memory-resident short samples;
- a fixed small voice pool;
- non-blocking enqueue;
- bounded queue or latest-event coalescing;
- no disk access after initialization;
- no network audio;
- deterministic teardown;
- best-effort failure isolation.

The first implementation should include only one restrained built-in sound theme. Additional themes are future scope and must not require bundling large assets.

### Audio acceptance limits

- no keyboard callback waits on audio;
- no accumulating playback lag during sustained typing;
- no sound after privacy suppression becomes active;
- idle audio engine may release expensive resources after a grace period;
- failure to initialize or play sound silently disables sound without affecting overlay or typing.

## Resource and latency constraints

- zero rendering work when the overlay is disabled;
- no continuous caret polling;
- no continuous frame loop while hidden or static;
- no synchronous UI or audio work in the keyboard callback;
- bounded token count and bounded text length;
- state snapshots use fixed limits and latest-state replacement;
- cache reusable render resources;
- overlay and sound failures are isolated from input processing;
- performance instrumentation must distinguish producer, queue, reducer, positioning, rendering, and audio stages.

Target behavior is qualitative until measured on supported hardware: no observable typing latency regression, no visible backlog under rapid typing, and near-zero idle CPU attributable to the feature.

## Error handling

- Event-post failure: drop the visual update.
- Renderer initialization failure: disable visual overlay for the session and retain typing.
- Device loss: recreate renderer resources and present the latest state.
- Caret lookup failure: use the configured fallback corner.
- Theme or DPI transition failure: preserve legibility with default metrics.
- Audio initialization/playback failure: disable audio for the session and retain visual overlay.
- Privacy classification uncertainty: suppress content and sound.

No error path may inject text, change the active window, block input, or display raw diagnostic content.

## Testing

### Pure unit tests

- reducer transitions from tokens to composed result;
- latest-state supersession and generation handling;
- privacy policy for password, secure, unknown, excluded, and normal contexts;
- adaptive animation policy for rapid input, reduced motion, low power, and normal load;
- stable-anchor threshold and monitor clamping;
- sound rate limiting, coalescing, suppression, and queue bounds.

### Native integration tests

- overlay window does not become foreground or active;
- overlay is click-through in ordinary mode;
- hide leaves no active animation scheduler;
- DPI/theme changes preserve bounds and legibility;
- device-loss recovery presents the latest state;
- failure to create the renderer does not affect typing;
- audio failure does not affect overlay or typing.

### Input regression tests

- exact Telex/VNI sequences remain unchanged with overlay on and off;
- Backspace, rollback, repeated vowel, literal boundary, URL, code, and WinForms compatibility paths remain unchanged;
- secure and password contexts emit no displayable text or sound event;
- rapid typing produces no unbounded queue.

### Performance tests

- callback timing comparison with feature disabled and enabled;
- event-post overhead;
- reducer and render-update latency;
- rapid 10-20 keys/second stress without visual or audio backlog;
- idle CPU after the overlay hides;
- steady and peak working-set measurements;
- caret-position refresh frequency;
- audio queue depth and dropped-event count under stress.

### Visual review

Capture matched screenshots at supported DPI levels and themes for:

- token stream;
- token-to-composed transition final state;
- caret-above and caret-below placement;
- fallback corner;
- long composition clamping;
- high contrast;
- reduced motion final states.

Animations require a short recording or frame-sequence review in addition to screenshots.

## Scope

### Included in the first implementation

- optional keystroke overlay;
- Vietnamese token and composed-result visualization;
- adaptive placement and safe fallback;
- privacy suppression;
- adaptive/reduced motion;
- light/dark/high-contrast and DPI handling;
- settings and preview;
- Presentation Mode foundation;
- one optional restrained per-key sound theme;
- latency, resource, privacy, regression, and visual tests.

### Excluded

- cursor customization or cursor trails;
- mouse click visualization;
- keystroke history;
- sentence history;
- cloud synchronization;
- network telemetry;
- marketplace or downloadable themes;
- large audio packs;
- per-application visual themes;
- full shortcut visualizer beyond the Presentation Mode foundation.

## Self-review decisions

The design intentionally rejects these tempting but costly choices:

- acrylic blur as a hard requirement;
- a permanent 60 FPS renderer;
- one UI object per key token;
- raw global keystroke logging;
- synchronous caret queries in the keyboard callback;
- unbounded animation or audio queues;
- sound enabled by default;
- framework migration solely for this overlay.

The optional sound feature remains in scope because it can share the same privacy policy and bounded event stream, but it is isolated behind its own setting and failure boundary. The visual overlay remains complete and useful without audio.

## Completion criteria

The feature is complete when users can enable a polished token-to-composed keystroke overlay that follows the caret when reliable, falls back safely, never steals focus, suppresses sensitive input, remains visually current during rapid typing, consumes no active rendering resources while hidden, and causes no measurable regression in Keyina's typing behavior.

Optional per-key sound must remain synchronized enough to feel intentional, never accumulate playback lag, and never compromise privacy, latency, or typing reliability.
