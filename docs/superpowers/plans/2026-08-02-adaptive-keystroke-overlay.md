# Adaptive Keystroke Overlay Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a polished, privacy-first Vietnamese keystroke overlay whose producer adds negligible keyboard-callback overhead, whose renderer is fully idle when hidden, and whose visual state always converges to the newest composition.

**Architecture:** Add a bounded native display model and pure reducer in `keyina_windows_input`, publish only the newest state through the resident message-only window, and render it in one reusable no-activate Direct2D/DirectWrite overlay owned by `Win32InputRuntime`. Persist optional overlay preferences through the existing managed configuration and runtime-profile path. Phase 1 contains no audio engine; per-key sound is a separate follow-up plan after visual performance gates pass.

**Tech Stack:** C++20, Win32, Direct2D, DirectWrite, DWM, existing native runtime profile/config bridge, .NET 10 WinForms settings host, CMake/CTest, existing custom .NET test runner.

## Global Constraints

- Overlay defaults to disabled.
- Maximum visible state is 16 tokens and 64 UTF-16 code units.
- Producer-to-reducer buffering is one latest-state slot; updates overwrite and never accumulate.
- Default hide delay is 900 ms; valid range is 500-2000 ms.
- Valid size range is 75-150%; valid opacity range is 25-100%.
- Feature-disabled callback p99 must remain within 2% of the current baseline.
- Feature-enabled producer/posting overhead must add no more than 5 microseconds at p99 on benchmark hardware.
- Hidden overlay must have no active animation timer or rendering loop.
- Unknown, password, protected, secure-desktop, and excluded contexts suppress and clear all displayable state.
- No overlay text may enter logs, diagnostics, history, clipboard, telemetry, crash metadata, or settings.
- No synchronous rendering, caret lookup, text measurement, sound, disk I/O, or managed IPC occurs in the keyboard callback.
- Preserve all unrelated working-tree changes.

---

## File structure

### Native core and runtime

- Create `platform/windows/input/include/keyina/windows/keystroke_overlay_model.h` — bounded event/state types, preferences, reducer, motion policy, and privacy decision inputs.
- Create `platform/windows/input/keystroke_overlay_model.cpp` — pure reducer, truncation, latest-generation handling, hide-state transition, and policy helpers.
- Create `platform/windows/input/include/keyina/windows/keystroke_overlay_positioner.h` — caret/monitor geometry contracts and stable-anchor API.
- Create `platform/windows/input/keystroke_overlay_positioner.cpp` — placement, clamping, above/below selection, fallback corner, and movement threshold.
- Create `platform/windows/input/include/keyina/windows/keystroke_overlay_window.h` — reusable no-activate renderer interface.
- Create `platform/windows/input/keystroke_overlay_window.cpp` — HWND lifecycle, Direct2D/DirectWrite resources, event-driven animation, device-loss recovery, and idle teardown.
- Modify `platform/windows/input/include/keyina/windows/runtime_profile.h` — add version-tolerant overlay preferences to the native profile.
- Modify `platform/windows/input/runtime_profile.cpp` — parse, validate, and default overlay profile fields.
- Modify `platform/windows/input/include/keyina/windows/win32_input_runtime.h` — own latest-state slot, overlay window, scheduler state, and request/update methods.
- Modify `platform/windows/input/win32_input_runtime.cpp` — produce bounded semantic updates, post one update message, apply privacy suppression, resolve position, and drive renderer outside the callback.
- Modify `platform/windows/input/CMakeLists.txt` — compile new sources and link `d2d1`, `dwrite`, and `dwmapi` only into `KeyinaInput` where possible.

### Managed configuration and settings

- Create `apps/host/Keyina.Host.Core/Overlay/KeystrokeOverlayPreferences.cs` — enums, defaults, validation, and immutable preference record.
- Modify `apps/host/Keyina.Host.Core/Configuration/KeyinaConfiguration.cs` — optional `KeystrokeOverlay` property and validation without schema-version increment.
- Modify `apps/host/Keyina.Host/UI/SettingsModels.cs` — expose preferences and actions.
- Create `apps/host/Keyina.Host/UI/SettingsForm.KeystrokeOverlay.cs` — focused settings card and preview wiring.
- Modify `apps/host/Keyina.Host/UI/SettingsForm.cs` — register the new section and synchronize controls.
- Modify `apps/host/Keyina.Host/Runtime/KeyinaApplicationContext.cs` — persist settings and include them in the native runtime profile.

### Tests and benchmarks

- Create `tests/windows/keystroke_overlay_model_test.cpp`.
- Create `tests/windows/keystroke_overlay_positioner_test.cpp`.
- Create `tests/windows/keystroke_overlay_window_test.cpp`.
- Modify `tests/CMakeLists.txt`.
- Modify `tests/windows/input_injection_test.cpp` only for end-to-end no-regression and secure-context assertions.
- Create `apps/host/Keyina.Host.Tests/KeystrokeOverlayPreferencesTests.cs`.
- Modify `apps/host/Keyina.Host.Tests/ConfigurationStoreTests.cs`.
- Modify `apps/host/Keyina.Host.Tests/SettingsFormTests.cs`.
- Modify `apps/host/Keyina.Host.Tests/KeyinaApplicationContextTests.cs`.
- Modify `platform/windows/input/native_resident.cpp` — add overlay self-test and benchmark modes.
- Create `docs/benchmarks/2026-08-02-keystroke-overlay-results.md` after measurements.

---

### Task 1: Define bounded overlay preferences and visual-state reducer

**Files:**
- Create: `platform/windows/input/include/keyina/windows/keystroke_overlay_model.h`
- Create: `platform/windows/input/keystroke_overlay_model.cpp`
- Create: `tests/windows/keystroke_overlay_model_test.cpp`
- Modify: `platform/windows/input/CMakeLists.txt`
- Modify: `tests/CMakeLists.txt`

**Interfaces:**
- Produces:
  - `enum class KeystrokeOverlayEventKind : std::uint8_t { Token, CompositionUpdated, CompositionCommitted, Cleared, Suppressed }`
  - `enum class KeystrokeOverlayMotionLevel : std::uint8_t { Adaptive, Full, Reduced, Off }`
  - `enum class KeystrokeOverlayFallbackCorner : std::uint8_t { BottomRight, BottomLeft, TopRight, TopLeft }`
  - `struct KeystrokeOverlayPreferences`
  - `struct KeystrokeOverlayEvent`
  - `struct KeystrokeOverlayState`
  - `class KeystrokeOverlayReducer { KeystrokeOverlayState Apply(const KeystrokeOverlayState&, const KeystrokeOverlayEvent&) const noexcept; }`
- Consumers: Tasks 2, 3, 4, 5, and 6.

- [ ] **Step 1: Write reducer tests for bounded state**

Add tests proving:

```cpp
KeystrokeOverlayEvent event{};
event.kind = KeystrokeOverlayEventKind::CompositionUpdated;
event.text.assign(80, u'x');
const auto state = reducer.Apply({}, event);
EXPECT_EQ(state.text.size(), 64u);
EXPECT_TRUE(state.truncated);
```

Also prove token history retains only the newest 16 tokens, `Suppressed` clears text/tokens immediately, and a newer generation supersedes an older one without replay.

- [ ] **Step 2: Run the focused native test and verify failure**

Run:

```bash
cmake --build --preset windows-debug --target keyina_tests
ctest --preset windows-debug -R keystroke_overlay_model --output-on-failure
```

Expected: compile failure because overlay model types do not exist.

- [ ] **Step 3: Implement the minimal bounded model and reducer**

Implement fixed limits as constants:

```cpp
inline constexpr std::size_t kMaximumOverlayTokens = 16;
inline constexpr std::size_t kMaximumOverlayCodeUnits = 64;
```

Use fixed-capacity or bounded value storage already accepted by the repository. Do not introduce heap-backed queues. `Apply` must be deterministic, `noexcept`, and must set `visible=false` and clear displayable text on suppression.

- [ ] **Step 4: Run focused tests**

Run the same CMake build and CTest command. Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add platform/windows/input/include/keyina/windows/keystroke_overlay_model.h platform/windows/input/keystroke_overlay_model.cpp platform/windows/input/CMakeLists.txt tests/windows/keystroke_overlay_model_test.cpp tests/CMakeLists.txt
git commit -m "feat(overlay): add bounded visual state reducer"
```

### Task 2: Add deterministic privacy gating and adaptive motion policy

**Files:**
- Modify: `platform/windows/input/include/keyina/windows/keystroke_overlay_model.h`
- Modify: `platform/windows/input/keystroke_overlay_model.cpp`
- Modify: `tests/windows/keystroke_overlay_model_test.cpp`

**Interfaces:**
- Consumes: Task 1 model types.
- Produces:
  - `struct KeystrokeOverlayPrivacyContext`
  - `enum class KeystrokeOverlayPrivacyDecision : std::uint8_t { Allow, Suppress }`
  - `KeystrokeOverlayPrivacyDecision EvaluateKeystrokeOverlayPrivacy(const KeystrokeOverlayPrivacyContext&) noexcept`
  - `struct KeystrokeOverlayMotionDecision { std::chrono::milliseconds duration; bool translate; bool emphasize_changed_glyphs; }`
  - `ResolveKeystrokeOverlayMotion(...) noexcept`

- [ ] **Step 1: Add failing policy tests**

Cover normal editable text, password, protected, secure desktop, unknown classification, excluded app, reduced motion, motion off, rapid input, and low-power mode. Unknown or contradictory context must return `Suppress`.

- [ ] **Step 2: Run focused tests and verify failure**

Run:

```bash
ctest --preset windows-debug -R keystroke_overlay_model --output-on-failure
```

Expected: compile failure for missing policy functions.

- [ ] **Step 3: Implement pure policies**

Use explicit booleans rather than process-name heuristics. Adaptive motion must shorten duration under rapid input and return zero-duration/no-translation for motion `Off`; reduced motion allows cross-fade only.

- [ ] **Step 4: Run focused tests**

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add platform/windows/input/include/keyina/windows/keystroke_overlay_model.h platform/windows/input/keystroke_overlay_model.cpp tests/windows/keystroke_overlay_model_test.cpp
git commit -m "feat(overlay): add privacy and motion policies"
```

### Task 3: Implement stable caret placement with safe fallback

**Files:**
- Create: `platform/windows/input/include/keyina/windows/keystroke_overlay_positioner.h`
- Create: `platform/windows/input/keystroke_overlay_positioner.cpp`
- Create: `tests/windows/keystroke_overlay_positioner_test.cpp`
- Modify: `platform/windows/input/CMakeLists.txt`
- Modify: `tests/CMakeLists.txt`

**Interfaces:**
- Produces:
  - `struct OverlayRectangle { int left; int top; int right; int bottom; }`
  - `struct KeystrokeOverlayPlacementInput`
  - `struct KeystrokeOverlayPlacement`
  - `KeystrokeOverlayPlacement ResolveKeystrokeOverlayPlacement(const KeystrokeOverlayPlacementInput&) noexcept`
- Consumers: Task 5 runtime integration.

- [ ] **Step 1: Add placement tests**

Test above-caret, below-caret, all four fallback corners, monitor clamping, long overlay width, monitor change, and movement below/above the stability threshold. Use exact pixel rectangles and assert output bounds remain within the working area.

- [ ] **Step 2: Run focused tests and verify failure**

```bash
cmake --build --preset windows-debug --target keyina_tests
ctest --preset windows-debug -R keystroke_overlay_positioner --output-on-failure
```

Expected: compile failure for missing positioner.

- [ ] **Step 3: Implement pure placement**

Keep Win32 queries outside this module. The input carries candidate caret bounds, last stable anchor, monitor work area, overlay size, margin, threshold, and fallback corner. Prefer below caret when enough space exists; otherwise above; otherwise clamp.

- [ ] **Step 4: Run focused tests**

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add platform/windows/input/include/keyina/windows/keystroke_overlay_positioner.h platform/windows/input/keystroke_overlay_positioner.cpp platform/windows/input/CMakeLists.txt tests/windows/keystroke_overlay_positioner_test.cpp tests/CMakeLists.txt
git commit -m "feat(overlay): add adaptive caret placement"
```

### Task 4: Build the reusable no-activate Direct2D/DirectWrite window

**Files:**
- Create: `platform/windows/input/include/keyina/windows/keystroke_overlay_window.h`
- Create: `platform/windows/input/keystroke_overlay_window.cpp`
- Create: `tests/windows/keystroke_overlay_window_test.cpp`
- Modify: `platform/windows/input/CMakeLists.txt`
- Modify: `tests/CMakeLists.txt`

**Interfaces:**
- Consumes: `KeystrokeOverlayState`, `KeystrokeOverlayMotionDecision`, `KeystrokeOverlayPlacement`.
- Produces:
  - `class KeystrokeOverlayWindow`
  - `bool Initialize(HINSTANCE) noexcept`
  - `void Present(const KeystrokeOverlayState&, const KeystrokeOverlayPlacement&, const KeystrokeOverlayMotionDecision&) noexcept`
  - `void HideAndReleaseTransientState() noexcept`
  - `bool IsVisibleForTesting() const noexcept`
  - `bool HasActiveAnimationForTesting() const noexcept`

- [ ] **Step 1: Add window-contract tests**

Create the window on an STA-capable test thread and assert:

```cpp
EXPECT_NE(GetWindowLongPtrW(hwnd, GWL_EXSTYLE) & WS_EX_NOACTIVATE, 0);
EXPECT_NE(GetWindowLongPtrW(hwnd, GWL_EXSTYLE) & WS_EX_TRANSPARENT, 0);
EXPECT_EQ(GetForegroundWindow(), foreground_before);
```

Present, retarget, hide, and assert no active scheduler remains. Add a deterministic test hook that simulates `D2DERR_RECREATE_TARGET` and verifies the latest state is presented after recreation.

- [ ] **Step 2: Run focused tests and verify failure**

```bash
cmake --build --preset windows-debug --target keyina_tests
ctest --preset windows-debug -R keystroke_overlay_window --output-on-failure
```

Expected: compile failure for missing renderer.

- [ ] **Step 3: Implement minimal native renderer**

Create one topmost tool window with `WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW`. Cache factories, text formats, brushes, and current text layouts. Use a short-lived timer only while opacity/translation changes. On hide, stop and destroy the timer, clear transient layouts, and hide the HWND. Do not add acrylic as a requirement.

- [ ] **Step 4: Run focused tests and native self-test**

```bash
ctest --preset windows-debug -R "keystroke_overlay_window|keyina.windows.input_self_test" --output-on-failure
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add platform/windows/input/include/keyina/windows/keystroke_overlay_window.h platform/windows/input/keystroke_overlay_window.cpp platform/windows/input/CMakeLists.txt tests/windows/keystroke_overlay_window_test.cpp tests/CMakeLists.txt
git commit -m "feat(overlay): add native no-activate renderer"
```

### Task 5: Integrate one-slot event delivery into `Win32InputRuntime`

**Files:**
- Modify: `platform/windows/input/include/keyina/windows/win32_input_runtime.h`
- Modify: `platform/windows/input/win32_input_runtime.cpp`
- Modify: `tests/windows/input_injection_test.cpp`
- Modify: `platform/windows/input/native_resident.cpp`

**Interfaces:**
- Consumes: Tasks 1-4.
- Produces runtime methods:
  - `void PublishKeystrokeOverlayEvent(const KeystrokeOverlayEvent&) noexcept`
  - `void RequestKeystrokeOverlayUpdate() noexcept`
  - `void UpdateKeystrokeOverlay() noexcept`
  - `void SuppressKeystrokeOverlay() noexcept`

- [ ] **Step 1: Add failing runtime tests**

Prove overlay-disabled typing does not post overlay messages; enabled normal composition posts at most one pending message; ten rapid updates leave one newest generation; suppression clears pending text; all exact Telex/VNI output remains byte-for-byte unchanged.

- [ ] **Step 2: Run focused tests and verify failure**

```bash
cmake --build --preset windows-debug --target keyina_tests KeyinaInput
ctest --preset windows-debug -R "input_injection|keystroke_overlay" --output-on-failure
```

Expected: failures for missing runtime integration.

- [ ] **Step 3: Add a dedicated runtime message and latest-state slot**

Reserve the next `WM_APP` value after existing runtime messages. In the callback, build only the bounded event, overwrite the slot, and call `PostMessageW` only when no overlay update is already pending. The message handler clears the pending flag, reduces the newest event, performs caret/monitor queries, and presents outside the callback.

- [ ] **Step 4: Add privacy suppression at the earliest reliable boundary**

Map existing typing context/security evidence into `KeystrokeOverlayPrivacyContext`. When evidence is unknown, publish only `Suppressed`; never copy display text into the slot. Foreground/context changes must synchronously clear the slot on the message thread.

- [ ] **Step 5: Add native self-test mode**

Add `--keystroke-overlay-self-test` in `native_resident.cpp` that emits deterministic JSON counters without raw text: produced, overwritten, rendered, suppressed, pending depth, timer active after hide, and focus preserved.

- [ ] **Step 6: Run regression and self-tests**

```bash
ctest --preset windows-debug -R "keyina.windows.input_typing|keyina.windows.input_clipboard_typing|input_injection|keystroke_overlay" --output-on-failure
build/windows-debug/platform/windows/input/KeyinaInput.exe --keystroke-overlay-self-test
```

Expected: all tests pass; JSON reports `pending_depth_max:1`, `timer_active_after_hide:false`, and `focus_preserved:true`.

- [ ] **Step 7: Commit**

```bash
git add platform/windows/input/include/keyina/windows/win32_input_runtime.h platform/windows/input/win32_input_runtime.cpp platform/windows/input/native_resident.cpp tests/windows/input_injection_test.cpp
git commit -m "feat(overlay): connect bounded runtime updates"
```

### Task 6: Persist version-tolerant preferences and expose them in the runtime profile

**Files:**
- Create: `apps/host/Keyina.Host.Core/Overlay/KeystrokeOverlayPreferences.cs`
- Modify: `apps/host/Keyina.Host.Core/Configuration/KeyinaConfiguration.cs`
- Modify: `apps/host/Keyina.Host.Tests/KeystrokeOverlayPreferencesTests.cs`
- Modify: `apps/host/Keyina.Host.Tests/ConfigurationStoreTests.cs`
- Modify: `platform/windows/input/include/keyina/windows/runtime_profile.h`
- Modify: `platform/windows/input/runtime_profile.cpp`
- Modify: native profile serialization site in `apps/host/Keyina.Host/Runtime/KeyinaApplicationContext.cs`

**Interfaces:**
- Produces managed record:

```csharp
public sealed record KeystrokeOverlayPreferences(
    bool Enabled,
    KeystrokeOverlayMotionLevel Motion,
    int SizePercent,
    int OpacityPercent,
    int HideDelayMilliseconds,
    KeystrokeOverlayFallbackCorner FallbackCorner,
    bool PresentationMode)
```

- Produces equivalent native `RuntimeKeystrokeOverlayProfile` fields.
- Consumers: Tasks 5, 7, and 8.

- [ ] **Step 1: Add failing managed validation tests**

Assert missing JSON property resolves to defaults, round-trip preserves values, and invalid size/opacity/hide delay/enums are rejected. Assert `CurrentSchemaVersion` remains `1`.

- [ ] **Step 2: Add failing native profile parser tests**

Test missing keys, valid profile, invalid bounds, invalid enums, and no change to existing typing/snippet profile fields.

- [ ] **Step 3: Run focused tests and verify failure**

```bash
dotnet test apps/host/Keyina.Host.Tests/Keyina.Host.Tests.csproj --filter "KeystrokeOverlay|ConfigurationStore"
ctest --preset windows-debug -R profile --output-on-failure
```

Expected: failures because preference/profile fields do not exist.

- [ ] **Step 4: Implement managed preferences and configuration validation**

Keep `KeystrokeOverlay` optional in serialized configuration and normalize null to `KeystrokeOverlayPreferences.Default`. Do not place composition text in configuration.

- [ ] **Step 5: Extend native profile parsing and serialization**

Use explicit keys and bounded integers. Invalid profile reload must retain the last known-good profile, matching existing profile behavior.

- [ ] **Step 6: Run focused tests**

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add apps/host/Keyina.Host.Core/Overlay/KeystrokeOverlayPreferences.cs apps/host/Keyina.Host.Core/Configuration/KeyinaConfiguration.cs apps/host/Keyina.Host.Tests/KeystrokeOverlayPreferencesTests.cs apps/host/Keyina.Host.Tests/ConfigurationStoreTests.cs apps/host/Keyina.Host/Runtime/KeyinaApplicationContext.cs platform/windows/input/include/keyina/windows/runtime_profile.h platform/windows/input/runtime_profile.cpp
git commit -m "feat(overlay): persist bounded preferences"
```

### Task 7: Add a focused settings section and safe preview

**Files:**
- Modify: `apps/host/Keyina.Host/UI/SettingsModels.cs`
- Create: `apps/host/Keyina.Host/UI/SettingsForm.KeystrokeOverlay.cs`
- Modify: `apps/host/Keyina.Host/UI/SettingsForm.cs`
- Modify: `apps/host/Keyina.Host/Runtime/KeyinaApplicationContext.cs`
- Modify: `apps/host/Keyina.Host.Tests/SettingsFormTests.cs`
- Modify: `apps/host/Keyina.Host.Tests/KeyinaApplicationContextTests.cs`
- Modify: `apps/host/Keyina.Host/UI/SettingsScreenshotRenderer.cs`

**Interfaces:**
- Consumes: `KeystrokeOverlayPreferences` from Task 6.
- Produces `SettingsActions.SetKeystrokeOverlayPreferences` and `SettingsActions.PreviewKeystrokeOverlay`.

- [ ] **Step 1: Add failing form and persistence tests**

Assert the section contains enable, motion, size, opacity, hide delay, fallback corner, Presentation Mode, and preview controls. Assert changing a control invokes one immutable preference update and persists it. Assert preview uses fixed sample text such as `nguyên → nguyễn` and never the user's current input.

- [ ] **Step 2: Run focused managed tests and verify failure**

```bash
dotnet test apps/host/Keyina.Host.Tests/Keyina.Host.Tests.csproj --filter "SettingsForm|KeystrokeOverlay|KeyinaApplicationContext"
```

Expected: failures for missing controls/actions.

- [ ] **Step 3: Implement the section in a partial class**

Reuse existing Fluent controls and responsive patterns. Do not add an always-running live preview. Enable preview only through an explicit button; disable or explain unsupported settings when the overlay is off.

- [ ] **Step 4: Wire persistence and native profile refresh**

Update the current immutable configuration, save through the existing store, refresh settings snapshot, and request native profile reload using the existing mechanism.

- [ ] **Step 5: Capture settings screenshots**

Use the existing screenshot renderer at standard and narrow widths. Check light, dark, high contrast, 100%, 150%, and 200% DPI where supported.

- [ ] **Step 6: Run tests**

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add apps/host/Keyina.Host/UI/SettingsModels.cs apps/host/Keyina.Host/UI/SettingsForm.KeystrokeOverlay.cs apps/host/Keyina.Host/UI/SettingsForm.cs apps/host/Keyina.Host/Runtime/KeyinaApplicationContext.cs apps/host/Keyina.Host.Tests/SettingsFormTests.cs apps/host/Keyina.Host.Tests/KeyinaApplicationContextTests.cs apps/host/Keyina.Host/UI/SettingsScreenshotRenderer.cs
git commit -m "feat(settings): add keystroke overlay controls"
```

### Task 8: Add latency, resource, privacy, and visual release gates

**Files:**
- Modify: `platform/windows/input/native_resident.cpp`
- Modify: `platform/windows/input/CMakeLists.txt`
- Modify: `apps/host/Keyina.Host.Benchmarks/ResidentBenchmarks.cs` if managed benchmark orchestration is needed
- Create: `docs/benchmarks/2026-08-02-keystroke-overlay-results.md`
- Modify: relevant CI workflow under `.github/workflows/` only if the existing benchmark job has a stable Windows runner

**Interfaces:**
- Consumes all previous tasks.
- Produces repeatable commands and a benchmark report containing raw distributions and pass/fail decisions.

- [ ] **Step 1: Add disabled/enabled producer benchmark modes**

Measure callback baseline, bounded event construction, one-slot overwrite, and `PostMessageW` request cost separately. Emit count, min, median, p95, p99, max, and dropped/overwritten count. Never emit typed text.

- [ ] **Step 2: Add hidden-idle resource probe**

Show and hide the overlay, wait 30 seconds, and report timer state, render count after hide, process CPU delta, private bytes, and working set. The test must confirm no render/timer activity after hide even if OS CPU rounding is noisy.

- [ ] **Step 3: Add rapid-input stress**

Drive 10, 20, and burst 50 events/second. Assert maximum pending depth is one, final rendered generation equals final produced generation, and no focus change occurs.

- [ ] **Step 4: Run full native and managed verification**

```bash
cmake --build --preset windows-release
ctest --preset windows-release --output-on-failure
dotnet test apps/host/Keyina.Host.Tests/Keyina.Host.Tests.csproj
build/windows-release/platform/windows/input/KeyinaInput.exe --callback-latency-self-test
build/windows-release/platform/windows/input/KeyinaInput.exe --keystroke-overlay-self-test
build/windows-release/platform/windows/input/KeyinaInput.exe --keystroke-overlay-resource-self-test
```

Expected: all functional tests pass; disabled callback p99 remains within 2% of baseline; enabled posting overhead is at most 5 microseconds p99 or the report documents timer resolution and demonstrates no statistically clear regression.

- [ ] **Step 5: Perform matched visual review**

Capture token stream, composed pill, above/below caret, fallback corner, long-text truncation, reduced motion final state, dark/light/high contrast, and 100/150/200% DPI. Record any platform-specific limitation explicitly.

- [ ] **Step 6: Write benchmark report**

Document hardware, Windows build, compiler preset, exact commands, raw distributions, pass/fail thresholds, screenshots/recording locations, skipped checks, and residual risk. Do not claim zero cost; report measured cost.

- [ ] **Step 7: Inspect final diff and commit**

```bash
git diff --check
git status --short
git diff --stat
git add platform/windows/input/native_resident.cpp platform/windows/input/CMakeLists.txt apps/host/Keyina.Host.Benchmarks/ResidentBenchmarks.cs docs/benchmarks/2026-08-02-keystroke-overlay-results.md .github/workflows
git commit -m "test(overlay): add release performance gates"
```

Only add files that actually changed; do not stage unrelated working-tree edits.

---

## Phase 2 handoff: optional per-key sound

Do not implement sound inside this plan. After Task 8 passes, create a separate plan that consumes only privacy-filtered semantic events, uses a fixed predecoded sample pool, bounds voices and pending events, drops stale sounds under load, performs no callback or per-key disk work, and remains disabled by default. This separation keeps the visual feature releasable even if low-latency audio requires further platform work.

## Final verification checklist

- [ ] Overlay disabled path performs no render, caret, or timer work.
- [ ] Native callback output remains identical for existing typing tests.
- [ ] Maximum pending visual depth is one.
- [ ] Password/protected/secure/unknown contexts expose no displayable text.
- [ ] Overlay never becomes foreground or active.
- [ ] Hidden overlay has no active timer or render activity.
- [ ] Final visual generation catches up after rapid input and device loss.
- [ ] Configuration remains backward compatible with schema version 1.
- [ ] Settings preview uses synthetic sample text only.
- [ ] Benchmarks and screenshots are recorded with exact commands.
- [ ] Final diff contains no raw keystroke logging, telemetry, clipboard use, audio engine, cursor work, or unrelated refactoring.
