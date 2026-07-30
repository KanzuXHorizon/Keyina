# Keyina Non-Intrusive Feedback Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add configurable no-focus overlay and local audio feedback for Keyina shortcuts and dictation states, with automatic audio-only behavior for full-screen applications.

**Architecture:** Add pure feedback preferences and presentation policy to `Keyina.Host.Core`, Windows probes/audio adapters to `Keyina.Host.Windows`, and a reusable no-activate WinForms overlay plus coordinator to `Keyina.Host`. Integrate semantic events at the application-context boundary and expose one compact feedback card on the existing Hotkeys settings page.

**Tech Stack:** C# 14, .NET 10 WinForms, Win32 P/Invoke (`user32.dll`, `winmm.dll`), existing custom test runner, existing Fluent UI controls.

## Global Constraints

- Never activate, focus, select, or foreground the overlay.
- Automatic mode uses overlay plus sound for ordinary windows and sound only when foreground coverage is at least 98% in both dimensions.
- No per-keystroke, Telex-edit, snippet-character, or partial-transcript sound.
- Feedback failures never fail typing, hotkeys, dictation, or `HostState`.
- Do not read or log process names, window titles, transcript text, clipboard content, or document content.
- Existing schema-version-1 configuration JSON without feedback properties must load as `Automatic`.
- Preserve all unrelated dirty working-tree changes; do not reset or commit them.

---

### Task 1: Core preferences and presentation policy

**Files:**
- Modify: `apps/host/Keyina.Host.Core/Configuration/KeyinaConfiguration.cs`
- Create: `apps/host/Keyina.Host.Core/Feedback/FeedbackModels.cs`
- Create: `apps/host/Keyina.Host.Tests/FeedbackPolicyTests.cs`
- Modify: `apps/host/Keyina.Host.Tests/ConfigurationStoreTests.cs`

**Interfaces:**
- Produces: `FeedbackMode`, `FeedbackPreferences`, `FeedbackEventKind`, `FeedbackTone`, `FeedbackSoundCue`, `FeedbackEvent`, `ForegroundPresentationState`, `FeedbackPresentation`, and `FeedbackPresentationPolicy.Resolve(FeedbackPreferences, ForegroundPresentationState)`.
- Produces: `KeyinaConfiguration.Feedback` with default `FeedbackPreferences.Default`.

- [ ] **Step 1: Write failing policy tests**

Create tests covering:

```csharp
AssertEx.Equal(
    new FeedbackPresentation(ShowOverlay: true, PlaySound: true),
    FeedbackPresentationPolicy.Resolve(
        new FeedbackPreferences(FeedbackMode.Automatic),
        ForegroundPresentationState.Windowed));

AssertEx.Equal(
    new FeedbackPresentation(ShowOverlay: false, PlaySound: true),
    FeedbackPresentationPolicy.Resolve(
        new FeedbackPreferences(FeedbackMode.Automatic),
        ForegroundPresentationState.FullscreenLike));
```

Also cover `VisualOnly`, `AudioOnly`, and `Off` overrides.

- [ ] **Step 2: Write failing configuration compatibility tests**

Add a schema-version-1 JSON fixture without a `feedback` property and assert the loaded value equals `FeedbackPreferences.Default`. Add a validation test that casts an invalid numeric `FeedbackMode` and expects `ConfigurationValidationException`.

- [ ] **Step 3: Run focused tests and confirm failure**

Run:

```bash
dotnet run --project apps/host/Keyina.Host.Tests/Keyina.Host.Tests.csproj -c Debug
```

Expected: compile failure because feedback types and configuration property do not exist.

- [ ] **Step 4: Implement minimal core types and validation**

Use immutable records and a pure switch:

```csharp
public enum FeedbackMode { Automatic, VisualOnly, AudioOnly, Off }
public sealed record FeedbackPreferences(FeedbackMode Mode)
{
    public static FeedbackPreferences Default { get; } = new(FeedbackMode.Automatic);
}

public static class FeedbackPresentationPolicy
{
    public static FeedbackPresentation Resolve(
        FeedbackPreferences preferences,
        ForegroundPresentationState foreground) => preferences.Mode switch
        {
            FeedbackMode.Automatic => new(foreground != ForegroundPresentationState.FullscreenLike, true),
            FeedbackMode.VisualOnly => new(true, false),
            FeedbackMode.AudioOnly => new(false, true),
            FeedbackMode.Off => new(false, false),
            _ => throw new ArgumentOutOfRangeException(nameof(preferences)),
        };
}
```

Extend `KeyinaConfiguration` with a nullable constructor parameter only if required for backward deserialization, then normalize missing values to `FeedbackPreferences.Default` inside validation/load behavior without incrementing `CurrentSchemaVersion`.

- [ ] **Step 5: Run focused tests until green**

Run the host test project and confirm all existing configuration and new policy tests pass.

### Task 2: Foreground presentation probe and sound synthesis

**Files:**
- Create: `apps/host/Keyina.Host.Windows/Feedback/WindowsForegroundPresentationProbe.cs`
- Create: `apps/host/Keyina.Host.Windows/Feedback/FeedbackWaveBuilder.cs`
- Create: `apps/host/Keyina.Host.Windows/Feedback/WindowsFeedbackSoundPlayer.cs`
- Create: `apps/host/Keyina.Host.Tests/ForegroundPresentationTests.cs`
- Create: `apps/host/Keyina.Host.Tests/FeedbackWaveBuilderTests.cs`

**Interfaces:**
- Produces: `IForegroundPresentationProbe.GetState()` returning `ForegroundPresentationState`.
- Produces: `WindowsForegroundPresentationProbe.Classify(Rectangle window, Rectangle monitor, double threshold = 0.98)` for deterministic tests.
- Produces: `IFeedbackSoundPlayer.Play(FeedbackSoundCue cue)` and `WindowsFeedbackSoundPlayer`.
- Produces: `FeedbackWaveBuilder.CreateCue(FeedbackSoundCue cue)` returning valid in-memory RIFF/WAVE bytes.

- [ ] **Step 1: Write failing rectangle-classification tests**

Cover exact full screen, 99% coverage, 97% coverage, zero-size rectangles, and negative-coordinate monitors.

- [ ] **Step 2: Write failing WAV-format tests**

Assert generated data begins with `RIFF`, contains `WAVE`, uses PCM format, and has duration between 40 and 180 ms for every non-`None` cue.

- [ ] **Step 3: Run tests and confirm missing types fail**

Run the host test project; expected compile failure for the new Windows feedback classes.

- [ ] **Step 4: Implement Win32 probe**

Use only:

```csharp
GetForegroundWindow();
GetWindowRect(hwnd, out RECT rect);
MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
GetMonitorInfo(monitor, ref MONITORINFO info);
```

Return `Unknown` when any call fails. `Classify` computes independent width and height coverage and returns `FullscreenLike` only when both meet the threshold.

- [ ] **Step 5: Implement deterministic in-memory cues**

Generate mono 16-bit PCM at 22,050 Hz once per cue. Use two short sine segments and a smooth attack/release envelope. Keep every static byte array alive for the process lifetime.

- [ ] **Step 6: Implement best-effort playback**

Call `PlaySound` with:

```csharp
SND_ASYNC | SND_MEMORY | SND_NODEFAULT | SND_NOSTOP | SND_SYSTEM
```

Ignore a false native return value. Never call `SystemSounds` or allow a default beep.

- [ ] **Step 7: Run tests until green**

Run the host test project and verify policy, rectangle, and WAV tests pass.

### Task 3: No-activate overlay and feedback coordinator

**Files:**
- Create: `apps/host/Keyina.Host/UI/Feedback/NoActivateFeedbackOverlay.cs`
- Create: `apps/host/Keyina.Host/UI/Feedback/FeedbackCoordinator.cs`
- Create: `apps/host/Keyina.Host.Tests/FeedbackOverlayTests.cs`
- Create: `apps/host/Keyina.Host.Tests/FeedbackCoordinatorTests.cs`

**Interfaces:**
- Produces: `IFeedbackOverlay.Show(FeedbackEvent feedbackEvent)` and `Hide()`.
- Produces: `NoActivateFeedbackOverlay` with internal style/test properties.
- Produces: `FeedbackCoordinator.Publish(FeedbackEvent feedbackEvent)` and `UpdatePreferences(FeedbackPreferences preferences)`.

- [ ] **Step 1: Write failing overlay structure tests**

Construct the form without showing it and assert:

```csharp
AssertEx.False(overlay.ShowInTaskbar, "Overlay entered the taskbar.");
AssertEx.Equal(FormBorderStyle.None, overlay.FormBorderStyle);
AssertEx.True(overlay.UsesNoActivateStyle, "Missing WS_EX_NOACTIVATE.");
AssertEx.True(overlay.UsesToolWindowStyle, "Missing WS_EX_TOOLWINDOW.");
AssertEx.True(overlay.IsClickThrough, "Overlay must not consume pointer input.");
```

Add a Windows-only integration test that captures `GetForegroundWindow`, shows the overlay, pumps events, and confirms the handle is unchanged.

- [ ] **Step 2: Write failing coordinator tests with fakes**

Cover ordinary/automatic dispatch to both channels, full-screen automatic dispatch to sound only, disabled mode no-op, duplicate suppression inside 150 ms, and independent exception isolation for overlay and sound.

- [ ] **Step 3: Run tests and confirm failure**

Run the host test project and expect compile failure for overlay/coordinator types.

- [ ] **Step 4: Implement the overlay**

Use one label/glyph composition, theme-aware painting, bottom-center monitor placement, and no focusable child control. Override `ShowWithoutActivation`, add the extended styles through `CreateParams`, return `HTTRANSPARENT` for `WM_NCHITTEST`, and call `ShowWindow(SW_SHOWNOACTIVATE)` plus `SetWindowPos(..., SWP_NOACTIVATE)`.

Use one UI timer for lifetime/fade. A new event updates copy/tone and restarts the timer instead of enqueuing another form.

- [ ] **Step 5: Implement coordinator isolation and debounce**

Resolve the policy per event, invoke overlay and sound in separate `try/catch` blocks, remember the last event kind/time, and coalesce duplicates within 150 ms. `Dispose` is idempotent.

- [ ] **Step 6: Run tests until green**

Run the host test project and verify no-focus, channel routing, debounce, and exception isolation.

### Task 4: Runtime integration

**Files:**
- Modify: `apps/host/Keyina.Host/Runtime/KeyinaRuntimeOptions.cs`
- Modify: `apps/host/Keyina.Host/Runtime/KeyinaApplicationContext.cs`
- Modify: `apps/host/Keyina.Host.Tests/KeyinaApplicationContextTests.cs`

**Interfaces:**
- Consumes: `FeedbackCoordinator`, `FeedbackEvent`, and `FeedbackPreferences`.
- Produces: runtime callbacks `SetFeedbackMode(FeedbackMode mode)` and `PreviewFeedback()` exposed through `SettingsActions`.

- [ ] **Step 1: Add failing application-context tests**

Inject fake probe, overlay, and sound player through runtime options/factories. Assert `ToggleVietnamese` publishes enabled/disabled feedback after state changes. Assert feedback exceptions do not set `CurrentState.ErrorCode`.

- [ ] **Step 2: Run focused tests and confirm failure**

Run the host test project; expected compile failure or assertion failure because runtime feedback is not wired.

- [ ] **Step 3: Add injectable feedback factories**

Extend runtime options with production defaults and test overrides without changing normal startup call sites.

- [ ] **Step 4: Create and dispose the coordinator**

Create it on the WinForms dispatcher after application initialization. Dispose it before dispatcher disposal.

- [ ] **Step 5: Publish semantic events**

Publish enabled/disabled events after `typingHook.SetEnabled`. Translate dictation model status changes to connecting/listening/finalizing/inserted/cancelled/error events, but never include `PartialText`. Publish concise failure feedback for user-actionable speech failures.

- [ ] **Step 6: Persist mode changes safely**

Update `configuration.Feedback`, call `FeedbackCoordinator.UpdatePreferences`, save through the existing atomic store, and report only configuration-save failures through existing host semantics.

- [ ] **Step 7: Run tests until green**

Run the host test project and confirm runtime integration does not regress existing hotkey/dictation tests.

### Task 5: Hotkeys settings card and preview

**Files:**
- Modify: `apps/host/Keyina.Host/UI/SettingsModels.cs`
- Modify: `apps/host/Keyina.Host/UI/SettingsForm.cs`
- Modify: `apps/host/Keyina.Host/Runtime/KeyinaApplicationContext.cs`
- Modify: `apps/host/Keyina.Host.Tests/SettingsFormTests.cs`

**Interfaces:**
- Extends: `SettingsSnapshot` with `FeedbackMode FeedbackMode`.
- Extends: `SettingsActions` with `Action<FeedbackMode> SetFeedbackMode` and `Action PreviewFeedback`.

- [ ] **Step 1: Write failing settings tests**

Assert the Hotkeys page contains controls named `feedbackMode`, `previewFeedback`, and `feedbackFullscreenNote`; the selector reflects the snapshot; changing the selector calls `SetFeedbackMode` once; preview calls `PreviewFeedback` without changing mode.

- [ ] **Step 2: Run tests and confirm failure**

Run the host test project; expected compile/assertion failure because controls and action fields do not exist.

- [ ] **Step 3: Add the Fluent card**

Insert one card between shortcut rows and registration status. Use existing typography, spacing, button, and card helpers. Use a keyboard-accessible `ComboBox` in `DropDownList` mode with Vietnamese labels:

```text
Tự động — khuyến nghị
Chỉ hình ảnh
Chỉ âm thanh
Tắt
```

Add the note: `Ở game hoặc ứng dụng toàn màn hình, chế độ Tự động chỉ phát âm thanh.`

- [ ] **Step 4: Bind snapshot and actions**

Guard selector updates with `applyingSnapshot`. Map selected indexes to enum values explicitly rather than relying on enum numeric order. Preview invokes the runtime action only.

- [ ] **Step 5: Run settings and screenshot tests**

Run the host test project and ensure screenshot rendering remains deterministic and the Hotkeys page fits without clipped content.

### Task 6: Final verification and scope review

**Files:**
- Review: all files changed by Tasks 1–5
- Update only if needed: `README.md` or `docs/compatibility/typing.md` with a concise description of non-intrusive feedback behavior.

- [ ] **Step 1: Run format and diff checks**

```bash
dotnet format Keyina.slnx --verify-no-changes
git diff --check
```

- [ ] **Step 2: Run complete host verification**

```bash
dotnet build Keyina.slnx -c Release
dotnet run --project apps/host/Keyina.Host.Tests/Keyina.Host.Tests.csproj -c Release
```

- [ ] **Step 3: Run native regression tests**

Use the repository's existing CMake presets and `ctest` commands already documented for Windows Debug and Release. The expected result is no native regression because the engine is untouched.

- [ ] **Step 4: Render the settings gallery**

```bash
dotnet run --project apps/host/Keyina.Host/Keyina.Host.csproj -c Release -- --render-settings-gallery docs/screenshots
```

Inspect `hotkeys.png` for clipping, hierarchy, contrast, and the feedback card's explanatory note.

- [ ] **Step 5: Inspect final scoped diff**

Confirm no use of `Activate`, `Focus`, `Select`, `SetForegroundWindow`, balloon notifications, toast activation, process-name game detection, raw transcript display, or per-keystroke audio. Confirm unrelated dirty files were not reverted or rewritten.

- [ ] **Step 6: Report evidence without committing**

List exact commands run, pass/fail counts, files changed for feedback, pre-existing dirty files left untouched, and any manual exclusive-fullscreen risk that could not be automated.
