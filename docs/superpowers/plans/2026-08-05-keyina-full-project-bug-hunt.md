# Keyina Full-Project Bug Hunt Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove confirmed contract/UI and test-isolation defects, add durable audit guards, then run full project verification and fix every additional reproducible failure discovered by those guards.

**Architecture:** Preserve the existing native-engine/native-resident/managed-host boundaries. Add pure, independently testable policy helpers for theme resolution and resource-test preconditions; wire persisted configuration through `SettingsSnapshot` and `SettingsActions`; add explicit contract-parity and accessibility regression tests; keep desktop-sensitive tests fail-closed and diagnostically distinct from deterministic product failures.

**Tech Stack:** C++20, Win32, CMake/CTest, .NET 10 WinForms, custom `KeyinaTest` runner, PowerShell release scripts, Python verification scripts.

## Global Constraints

- Preserve unrelated user changes in `tests/engine_tone_test.cpp`, `tests/windows/resident_input_controller_test.cpp`, and `benchmark-result.json`.
- Do not terminate the installed `KeyinaInput.exe --background` process without explicit user authorization.
- Do not commit, push, publish, sign, install, or modify credentials without explicit user authorization.
- Keep ordinary typing offline, fail-open, bounded, and free of file/network/UI/process-launch work inside the low-level keyboard callback.
- Keep WinForms on `net10.0-windows10.0.19041.0`; do not add a second UI framework.
- Windows high contrast overrides any stored System/Light/Dark preference.
- Every behavior change must begin with a focused failing test and end with adjacent regression verification.

---

### Task 1: Preserve Baseline and Prove the Repeated-S Regression

**Files:**
- Preserve: `tests/engine_tone_test.cpp`
- Preserve: `tests/windows/resident_input_controller_test.cpp`
- Inspect: `core/src/engine.cpp`
- Inspect: `platform/windows/input/resident_input_controller.cpp`

**Interfaces:**
- Consumes: `keyina::Engine::Process`, `ResidentInputController::Handle` through existing test helpers.
- Produces: a verified decision that the existing `russt` tests are already green or a minimal structural engine fix if they fail.

- [ ] **Step 1: Record the exact existing diff**

Run:

```powershell
git diff -- tests/engine_tone_test.cpp tests/windows/resident_input_controller_test.cpp
```

Expected: only the two `russt` regression tests are shown.

- [ ] **Step 2: Build and run the two focused native test groups**

Run:

```powershell
cmake --build --preset windows-msvc-debug
.\build\windows-msvc-debug\tests\Debug\keyina_tests.exe
```

Expected: `latin_word_russt_preserves_repeated_s` and `resident_input_controller_preserves_double_s_in_russt` pass.

- [ ] **Step 3: If either test fails, isolate the structural predicate**

Add no word list. Change only the repeated-tone/repeated-`s` restoration predicate in `core/src/engine.cpp` so a second physical `s` remains literal when the current token cannot validly consume it as a Vietnamese tone transition.

The implementation must preserve this shape:

```cpp
if (IsRepeatedToneEscape(input, active_) &&
    ShouldPreserveLiteralRepeatedTone(active_, input)) {
  return ApplyLiteralInput(input);
}
```

- [ ] **Step 4: Re-run native unit tests**

Run:

```powershell
ctest --preset windows-msvc-debug -R "^keyina.unit$" --output-on-failure
```

Expected: PASS.

- [ ] **Step 5: Review only; do not commit without authorization**

Run:

```powershell
git diff --check
git diff -- tests/engine_tone_test.cpp tests/windows/resident_input_controller_test.cpp core/src/engine.cpp
```

Expected: no whitespace errors and no word-specific exception.

---

### Task 2: Add a Pure Theme Resolution Contract

**Files:**
- Modify: `apps/host/Keyina.Host/UI/Fluent/FluentTheme.cs`
- Test: `apps/host/Keyina.Host.Tests/FluentThemeTests.cs`

**Interfaces:**
- Consumes: `KeyinaTheme` from `Keyina.Host.Core.Configuration`.
- Produces: `FluentTheme.Resolve(KeyinaTheme preference, bool highContrast, bool systemDark) -> FluentThemePalette` and `FluentTheme.Describe(KeyinaTheme preference, FluentThemePalette palette) -> string`.

- [ ] **Step 1: Write failing theme resolution tests**

Create `FluentThemeTests.cs` with these tests:

```csharp
using Keyina.Host.Core.Configuration;
using Keyina.Host.UI.Fluent;

namespace Keyina.Host.Tests;

internal static class FluentThemeTests
{
    [KeyinaTest("stored light and dark themes override the Windows app theme")]
    private static void StoredThemeOverridesSystemTheme()
    {
        AssertEx.Equal(
            FluentThemeMode.Light,
            FluentTheme.Resolve(KeyinaTheme.Light, highContrast: false, systemDark: true).Mode);
        AssertEx.Equal(
            FluentThemeMode.Dark,
            FluentTheme.Resolve(KeyinaTheme.Dark, highContrast: false, systemDark: false).Mode);
    }

    [KeyinaTest("system theme follows Windows and high contrast always wins")]
    private static void SystemAndHighContrastResolutionIsDeterministic()
    {
        AssertEx.Equal(
            FluentThemeMode.Dark,
            FluentTheme.Resolve(KeyinaTheme.System, highContrast: false, systemDark: true).Mode);
        AssertEx.Equal(
            FluentThemeMode.Light,
            FluentTheme.Resolve(KeyinaTheme.System, highContrast: false, systemDark: false).Mode);
        AssertEx.Equal(
            FluentThemeMode.HighContrast,
            FluentTheme.Resolve(KeyinaTheme.Dark, highContrast: true, systemDark: false).Mode);
    }
}
```

- [ ] **Step 2: Run the focused managed tests to verify failure**

Run:

```powershell
dotnet run --project apps/host/Keyina.Host.Tests/Keyina.Host.Tests.csproj -c Debug --no-build
```

Expected: build/test failure because `FluentTheme.Resolve` does not exist.

- [ ] **Step 3: Implement pure resolution**

In `FluentTheme.cs`, import the configuration namespace and add:

```csharp
public static FluentThemePalette Resolve(
    KeyinaTheme preference,
    bool highContrast,
    bool systemDark)
{
    if (highContrast)
    {
        return CreateHighContrastPalette();
    }

    return preference switch
    {
        KeyinaTheme.Light => LightPalette,
        KeyinaTheme.Dark => DarkPalette,
        KeyinaTheme.System => systemDark ? DarkPalette : LightPalette,
        _ => throw new ArgumentOutOfRangeException(nameof(preference), preference, null),
    };
}
```

Keep `Current` as the System preference wrapper:

```csharp
public static FluentThemePalette Current => Resolve(
    KeyinaTheme.System,
    SystemInformation.HighContrast,
    IsSystemDarkMode());
```

- [ ] **Step 4: Run the focused tests**

Run:

```powershell
dotnet build Keyina.slnx -c Debug
dotnet run --project apps/host/Keyina.Host.Tests/Keyina.Host.Tests.csproj -c Debug --no-build
```

Expected: all managed tests pass.

- [ ] **Step 5: Review only; do not commit without authorization**

Run:

```powershell
git diff --check
git diff -- apps/host/Keyina.Host/UI/Fluent/FluentTheme.cs apps/host/Keyina.Host.Tests/FluentThemeTests.cs
```

---

### Task 3: Wire Persisted Theme Through Runtime and Settings UI

**Files:**
- Modify: `apps/host/Keyina.Host/UI/SettingsModels.cs`
- Modify: `apps/host/Keyina.Host/Runtime/KeyinaApplicationContext.cs`
- Modify: `apps/host/Keyina.Host/UI/SettingsForm.cs`
- Test: `apps/host/Keyina.Host.Tests/KeyinaApplicationContextTests.cs`
- Test: `apps/host/Keyina.Host.Tests/SettingsFormTests.cs` or the existing settings UI test file that owns snapshot/action behavior.

**Interfaces:**
- Consumes: `KeyinaConfiguration.Theme`, `FluentTheme.Resolve`.
- Produces: `SettingsSnapshot.Theme`, `SettingsActions.SetTheme`, a keyboard-accessible `themeSelector`, and persisted theme updates.

- [ ] **Step 1: Add failing runtime snapshot/action tests**

Add assertions that a configuration with `Theme = KeyinaTheme.Dark` produces `CurrentSettingsSnapshot.Theme == KeyinaTheme.Dark`, and invoking `SetTheme(KeyinaTheme.Light)` persists Light through the fake configuration store.

Use the exact action signature:

```csharp
public Action<KeyinaTheme> SetTheme { get; init; } = _ => { };
```

- [ ] **Step 2: Add failing UI binding test**

Construct `SettingsForm` with a Dark snapshot and assert:

```csharp
var selector = (ComboBox)form.Controls.Find("themeSelector", true).Single();
AssertEx.Equal(KeyinaTheme.Dark, selector.SelectedValue);
AssertEx.Equal("Chủ đề giao diện", selector.AccessibleName);
AssertEx.True(selector.TabStop, "Theme selector must be keyboard reachable.");
```

Change the selection to Light and assert the supplied `SetTheme` callback receives `KeyinaTheme.Light` exactly once.

- [ ] **Step 3: Run managed tests to verify failure**

Run:

```powershell
dotnet build Keyina.slnx -c Debug
dotnet run --project apps/host/Keyina.Host.Tests/Keyina.Host.Tests.csproj -c Debug --no-build
```

Expected: failures for missing snapshot/action/UI theme paths.

- [ ] **Step 4: Add snapshot and action contract**

In `SettingsModels.cs` add:

```csharp
public KeyinaTheme Theme { get; init; } = KeyinaTheme.System;
```

and:

```csharp
public Action<KeyinaTheme> SetTheme { get; init; } = _ => { };
```

- [ ] **Step 5: Add runtime persistence**

In `KeyinaApplicationContext.cs` add:

```csharp
private void SetTheme(KeyinaTheme theme)
{
    if (!Enum.IsDefined(theme))
    {
        throw new ArgumentOutOfRangeException(nameof(theme), theme, null);
    }

    configuration = configuration with { Theme = theme };
    _ = SaveConfigurationSafelyAsync();
    RefreshVisualState();
}
```

Wire `SetTheme = SetTheme` in `CreateSettingsActions()` and `Theme = configuration.Theme` in `CreateSettingsSnapshot()`.

- [ ] **Step 6: Add the Settings selector and apply selected palette**

Create a `ComboBox` named `themeSelector` with values System, Light, Dark and Vietnamese labels `Theo Windows`, `Sáng`, `Tối`. Place it in the Overview/System appearance area using the existing `FluentSettingRow` pattern.

In `ApplySnapshot`:

```csharp
themeSelector.SelectedValue = snapshot.Theme;
```

In the change handler:

```csharp
if (!applyingSnapshot && themeSelector.SelectedValue is KeyinaTheme theme)
{
    actions.SetTheme(theme);
}
```

In `ApplySystemTheme`, resolve from the current snapshot:

```csharp
palette = FluentTheme.Resolve(
    currentSnapshot.Theme,
    SystemInformation.HighContrast,
    FluentTheme.IsSystemDarkModeForCurrentUser());
```

Expose an internal system-dark helper rather than duplicating registry logic. Update the status text to distinguish `Theo Windows`, forced `Sáng`, forced `Tối`, and high contrast.

- [ ] **Step 7: Apply the selected preference to transient windows**

Pass the resolved palette or `KeyinaTheme` preference to settings-owned transient forms created after the settings window: first run, translation preview, dictation overlay, snippet editor, hotkey capture, and snippet suggestion overlay. Existing high-contrast behavior remains authoritative.

- [ ] **Step 8: Run managed tests**

Run:

```powershell
dotnet build Keyina.slnx -c Debug
dotnet run --project apps/host/Keyina.Host.Tests/Keyina.Host.Tests.csproj -c Debug --no-build
```

Expected: all tests pass and no warnings.

- [ ] **Step 9: Review only; do not commit without authorization**

Run:

```powershell
git diff --check
git diff -- apps/host/Keyina.Host/UI apps/host/Keyina.Host/Runtime/KeyinaApplicationContext.cs apps/host/Keyina.Host.Tests
```

---

### Task 4: Add a Configuration-to-UI Contract Parity Guard

**Files:**
- Create: `apps/host/Keyina.Host.Tests/SettingsContractParityTests.cs`
- Modify only if failures prove an omission: `apps/host/Keyina.Host/UI/SettingsModels.cs`, `apps/host/Keyina.Host/Runtime/KeyinaApplicationContext.cs`, relevant settings UI partials.

**Interfaces:**
- Consumes: reflection metadata for `KeyinaConfiguration`, `SettingsSnapshot`, and `SettingsActions`.
- Produces: a regression test that fails when a user-facing persisted field is orphaned from snapshot/action/UI ownership.

- [ ] **Step 1: Create the explicit parity test**

Use an explicit exclusion set for non-setting metadata:

```csharp
private static readonly HashSet<string> NonUiConfigurationMembers =
[
    nameof(KeyinaConfiguration.SchemaVersion),
    nameof(KeyinaConfiguration.Snippets),
    nameof(KeyinaConfiguration.FirstRunCompleted),
];
```

Map persisted properties to snapshot/action names:

```csharp
private static readonly IReadOnlyDictionary<string, (string Snapshot, string Action)> Bindings =
    new Dictionary<string, (string, string)>(StringComparer.Ordinal)
    {
        [nameof(KeyinaConfiguration.VietnameseEnabled)] =
            (nameof(SettingsSnapshot.VietnameseEnabled), nameof(SettingsActions.SetVietnameseEnabled)),
        [nameof(KeyinaConfiguration.SpeechEnabled)] =
            (nameof(SettingsSnapshot.SpeechEnabled), nameof(SettingsActions.SetSpeechEnabled)),
        [nameof(KeyinaConfiguration.Theme)] =
            (nameof(SettingsSnapshot.Theme), nameof(SettingsActions.SetTheme)),
        [nameof(KeyinaConfiguration.SpeechLanguage)] =
            (nameof(SettingsSnapshot.SpeechLanguage), nameof(SettingsActions.SetSpeechLanguage)),
        [nameof(KeyinaConfiguration.TranslationEnabled)] =
            (nameof(SettingsSnapshot.TranslationEnabled), nameof(SettingsActions.SetTranslationEnabled)),
        [nameof(KeyinaConfiguration.TranslationPreviewEnabled)] =
            (nameof(SettingsSnapshot.TranslationPreviewEnabled), nameof(SettingsActions.SetTranslationPreviewEnabled)),
        [nameof(KeyinaConfiguration.TraditionalTonePlacement)] =
            (nameof(SettingsSnapshot.TraditionalTonePlacement), nameof(SettingsActions.SetTraditionalTonePlacement)),
        [nameof(KeyinaConfiguration.QuickTelexLetters)] =
            (nameof(SettingsSnapshot.QuickTelexLetters), nameof(SettingsActions.SetQuickTelexLetters)),
        [nameof(KeyinaConfiguration.StandaloneWToUHorn)] =
            (nameof(SettingsSnapshot.StandaloneWToUHorn), nameof(SettingsActions.SetStandaloneWToUHorn)),
        [nameof(KeyinaConfiguration.ClipboardCompatibilityEnabled)] =
            (nameof(SettingsSnapshot.ClipboardCompatibilityEnabled), nameof(SettingsActions.SetClipboardCompatibilityEnabled)),
        [nameof(KeyinaConfiguration.TranslationTargetLanguage)] =
            (nameof(SettingsSnapshot.TranslationTargetLanguage), nameof(SettingsActions.SetTranslationTargetLanguage)),
        [nameof(KeyinaConfiguration.TranslationProviders)] =
            (nameof(SettingsSnapshot.TranslationProviders), nameof(SettingsActions.SetTranslationProviders)),
        [nameof(KeyinaConfiguration.Hotkeys)] =
            (nameof(SettingsSnapshot.Hotkeys), nameof(SettingsActions.SetHotkey)),
        [nameof(KeyinaConfiguration.Applications)] =
            (nameof(SettingsSnapshot.Applications), nameof(SettingsActions.SetApplicationPreferences)),
        [nameof(KeyinaConfiguration.KeystrokeOverlay)] =
            (nameof(SettingsSnapshot.KeystrokeOverlay), nameof(SettingsActions.SetKeystrokeOverlayPreferences)),
        [nameof(KeyinaConfiguration.Feedback)] =
            (nameof(SettingsSnapshot.FeedbackMode), nameof(SettingsActions.SetFeedbackMode)),
    };
```

Assert every public instance property outside the exclusion set has a binding, and every named snapshot/action member exists.

- [ ] **Step 2: Run the parity test**

Run:

```powershell
dotnet build Keyina.slnx -c Debug
dotnet run --project apps/host/Keyina.Host.Tests/Keyina.Host.Tests.csproj -c Debug --no-build
```

Expected: PASS after Task 3; any failure identifies a concrete additional omission to fix before continuing.

- [ ] **Step 3: Fix each additional failure at the owning boundary**

For each failing field, add a focused behavior test, then wire load → snapshot → UI → action → persistence. Do not add a binding entry merely to silence the test unless the field is intentionally non-UI and documented in `NonUiConfigurationMembers` with a reason comment.

- [ ] **Step 4: Review only; do not commit without authorization**

Run:

```powershell
git diff --check
git diff -- apps/host/Keyina.Host.Tests/SettingsContractParityTests.cs apps/host/Keyina.Host/UI apps/host/Keyina.Host/Runtime/KeyinaApplicationContext.cs
```

---

### Task 5: Make Native Resource Tests Detect Existing Residents and Contamination

**Files:**
- Create: `platform/windows/input/include/keyina/windows/resource_self_test_policy.h`
- Create: `platform/windows/input/resource_self_test_policy.cpp`
- Modify: `platform/windows/input/CMakeLists.txt`
- Modify: `platform/windows/input/native_resident.cpp`
- Create: `tests/windows/resource_self_test_policy_test.cpp`
- Modify: `tests/CMakeLists.txt`

**Interfaces:**
- Produces:

```cpp
namespace keyina::windows {

enum class ResourceSelfTestDisposition {
  Pass,
  RetryContaminated,
  BlockedByExistingResident,
  FailBudget,
  FailRuntime,
};

ResourceSelfTestDisposition ClassifyResourceSelfTestAttempt(
    bool existing_resident,
    bool runtime_started,
    bool contaminated_by_input,
    bool budget_pass) noexcept;

}
```

- [ ] **Step 1: Write failing policy tests**

Add cases:

```cpp
KEYINA_TEST(resource_self_test_blocks_when_production_resident_exists) {
  KEYINA_EXPECT_EQ(
      ClassifyResourceSelfTestAttempt(true, true, false, true),
      ResourceSelfTestDisposition::BlockedByExistingResident);
}

KEYINA_TEST(resource_self_test_retries_only_input_contamination) {
  KEYINA_EXPECT_EQ(
      ClassifyResourceSelfTestAttempt(false, true, true, false),
      ResourceSelfTestDisposition::RetryContaminated);
}

KEYINA_TEST(resource_self_test_fails_clean_budget_regression) {
  KEYINA_EXPECT_EQ(
      ClassifyResourceSelfTestAttempt(false, true, false, false),
      ResourceSelfTestDisposition::FailBudget);
}
```

- [ ] **Step 2: Run the focused native test to verify failure**

Run:

```powershell
cmake --build --preset windows-msvc-debug
ctest --preset windows-msvc-debug -R "^keyina.unit$" --output-on-failure
```

Expected: compile failure because the policy does not exist.

- [ ] **Step 3: Implement the pure classifier**

```cpp
ResourceSelfTestDisposition ClassifyResourceSelfTestAttempt(
    bool existing_resident,
    bool runtime_started,
    bool contaminated_by_input,
    bool budget_pass) noexcept {
  if (existing_resident) {
    return ResourceSelfTestDisposition::BlockedByExistingResident;
  }
  if (!runtime_started) {
    return ResourceSelfTestDisposition::FailRuntime;
  }
  if (budget_pass) {
    return ResourceSelfTestDisposition::Pass;
  }
  return contaminated_by_input
      ? ResourceSelfTestDisposition::RetryContaminated
      : ResourceSelfTestDisposition::FailBudget;
}
```

- [ ] **Step 4: Detect the production resident before launching attempts**

In `RunResourceSelfTest`, call `OpenMutexW(SYNCHRONIZE, FALSE, kMutexName)`. If a handle is returned, close it, emit exactly:

```json
{"error":"resource_self_test_blocked_by_existing_resident"}
```

and return a distinct nonzero exit code. Do not send `--exit` and do not terminate the process.

- [ ] **Step 5: Preserve contamination semantics**

Use the pure classifier in parent/child handling. Retry only `RetryContaminated`; stop immediately for budget/runtime failures. Emit stable error JSON for timeout, child start failure, existing resident, and clean budget failure.

- [ ] **Step 6: Serialize desktop-sensitive CTest entries**

Assign a CTest resource lock to hook/resource/interactive tests in `platform/windows/input/CMakeLists.txt`:

```cmake
RESOURCE_LOCK keyina_windows_desktop_input
```

Apply it to `keyina.windows.input_resource`, `keyina.windows.input_tray_resource`, interactive typing, clipboard typing, callback latency, and transform callback latency tests. Keep unit tests outside this lock.

- [ ] **Step 7: Run focused verification**

With the installed resident still running:

```powershell
.\build\windows-msvc-debug\platform\windows\input\Debug\KeyinaInput.exe --tray-resource-self-test
```

Expected: deterministic `resource_self_test_blocked_by_existing_resident`, not a misleading memory regression.

Run:

```powershell
ctest --preset windows-msvc-debug -R "^keyina.unit$" --repeat until-fail:5 --output-on-failure
```

Expected: five passes.

- [ ] **Step 8: Review only; do not commit without authorization**

Run:

```powershell
git diff --check
git diff -- platform/windows/input tests/windows/resource_self_test_policy_test.cpp tests/CMakeLists.txt
```

---

### Task 6: Harden Theme and Custom-Control Accessibility

**Files:**
- Modify: `apps/host/Keyina.Host/UI/Fluent/FluentControls.cs`
- Modify: `apps/host/Keyina.Host/UI/SettingsForm.cs`
- Test: `apps/host/Keyina.Host.Tests/FluentLayoutTests.cs`
- Test: `apps/host/Keyina.Host.Tests/FluentThemeTests.cs`

**Interfaces:**
- Consumes: resolved `FluentThemePalette` and the new `themeSelector`.
- Produces: stable accessible names/descriptions, keyboard reachability, visible focus, and non-color-only state for the theme and custom Fluent controls.

- [ ] **Step 1: Add failing accessibility assertions**

Add tests that verify:

```csharp
AssertEx.Equal("Chủ đề giao diện", selector.AccessibleName);
AssertEx.True(selector.AccessibleDescription?.Contains("Windows", StringComparison.Ordinal) == true);
AssertEx.True(selector.TabStop);
```

For custom toggles/buttons/setting rows, assert enabled/disabled and selected state is represented in accessible text or the native control pattern, not color alone.

- [ ] **Step 2: Run managed tests to verify failure**

Run:

```powershell
dotnet build Keyina.slnx -c Debug
dotnet run --project apps/host/Keyina.Host.Tests/Keyina.Host.Tests.csproj -c Debug --no-build
```

- [ ] **Step 3: Implement the smallest accessibility fixes**

Set `AccessibleName`, `AccessibleDescription`, `AccessibleRole`, `TabStop`, and focus cues at component creation. Do not add duplicate spoken text when the native label association already exposes the same information.

- [ ] **Step 4: Verify narrow/high-contrast screenshot contracts**

Run:

```powershell
dotnet run --project apps/host/Keyina.Host/Keyina.Host.csproj -c Debug --no-build -- --self-test
dotnet run --project apps/host/Keyina.Host/Keyina.Host.csproj -c Release --no-build -- --resource-self-test
```

Expected: self-tests pass. Run the existing screenshot renderer tests and confirm no horizontal overflow assertions fail.

- [ ] **Step 5: Review only; do not commit without authorization**

Run:

```powershell
git diff --check
git diff -- apps/host/Keyina.Host/UI apps/host/Keyina.Host.Tests
```

---

### Task 7: Security, Privacy, and Failure-Path Audit

**Files:**
- Inspect/modify only on confirmed failure: `apps/host/Keyina.Host.Windows/Credentials/`, `apps/host/Keyina.Host.Windows/Networking/`, `apps/host/Keyina.Host/Translation/`, `apps/host/Keyina.Host/Speech/`, `apps/host/Keyina.Host/Runtime/`, `platform/windows/input/`.
- Add focused tests beside the owning existing test class.

**Interfaces:**
- Consumes: existing credential vault, endpoint validator, focus guard, clipboard privacy, IPC, and diagnostic contracts.
- Produces: no fail-open secret/default path and stable content-free errors.

- [ ] **Step 1: Run repository scans**

Run:

```powershell
git grep -n -I -E "(api[_-]?key|secret|password|credential|token)[[:space:]]*[:=][[:space:]]*\"[^\"]+\"" -- apps platform scripts

git grep -n -I -E "DangerousAcceptAnyServerCertificateValidator|ServerCertificateCustomValidationCallback|http://|CreateFileW|WriteAllText|Console\.Write(Line)?\(" -- apps platform scripts
```

Classify each result as production-reachable, test-only, documentation-only, identifier-only, or safe fail-closed behavior.

- [ ] **Step 2: Run dependency vulnerability checks**

Run:

```powershell
dotnet list Keyina.slnx package --vulnerable --include-transitive
```

Expected: no vulnerable package. If the command reports a vulnerability, update only to the smallest compatible patched version and run Debug/Release managed tests.

- [ ] **Step 3: Add a regression for every confirmed production issue**

Examples of acceptable focused contracts:

```csharp
AssertEx.False(log.Contains(sourceText, StringComparison.Ordinal));
AssertEx.False(log.Contains(apiKey, StringComparison.Ordinal));
AssertEx.Throws<ArgumentException>(() => validator.Resolve("http://public.example", allowLocalEndpoint: true));
```

- [ ] **Step 4: Run managed and native focused suites**

Run:

```powershell
dotnet build Keyina.slnx -c Debug
dotnet run --project apps/host/Keyina.Host.Tests/Keyina.Host.Tests.csproj -c Debug --no-build
ctest --preset windows-msvc-debug -R "^keyina.unit$" --output-on-failure
```

- [ ] **Step 5: Review only; do not commit without authorization**

Run:

```powershell
git diff --check
git diff --stat
git diff
```

Confirm no credential values, transcript text, selected text, user paths, or generated binaries entered the diff.

---

### Task 8: Full Debug, Release, Sanitizer, Performance, and Release Verification

**Files:**
- Modify only for confirmed failures.
- Update: `docs/compatibility/2026-08-05-full-project-bug-hunt-results.md`

**Interfaces:**
- Consumes: all previous tasks.
- Produces: fresh verification evidence and a residual-risk report.

- [ ] **Step 1: Run native Debug**

Run:

```powershell
cmake --preset windows-msvc-debug
cmake --build --preset windows-msvc-debug
ctest --preset windows-msvc-debug --output-on-failure
```

Expected with an installed resident: resource tests report the explicit blocked precondition. Run deterministic unit/integration subsets separately and record the blocked desktop lane rather than claiming a pass.

- [ ] **Step 2: Run managed Debug**

Run:

```powershell
dotnet build Keyina.slnx -c Debug
dotnet run --project apps/host/Keyina.Host.Tests/Keyina.Host.Tests.csproj -c Debug --no-build
dotnet run --project apps/host/Keyina.Host/Keyina.Host.csproj -c Debug --no-build -- --self-test
dotnet run --project apps/host/Keyina.Host/Keyina.Host.csproj -c Debug --no-build -- --speech-self-test
dotnet run --project apps/host/Keyina.Host/Keyina.Host.csproj -c Debug --no-build -- --hotkey-self-test
```

Expected: zero warnings/errors and all tests pass.

- [ ] **Step 3: Run native and managed Release**

Run:

```powershell
cmake --preset windows-msvc-release
cmake --build --preset windows-msvc-release
ctest --preset windows-msvc-release --output-on-failure

dotnet build Keyina.slnx -c Release
dotnet run --project apps/host/Keyina.Host.Tests/Keyina.Host.Tests.csproj -c Release --no-build
dotnet run --project apps/host/Keyina.Host/Keyina.Host.csproj -c Release --no-build -- --resource-self-test
```

- [ ] **Step 4: Run deterministic verification scripts and benchmarks**

Run:

```powershell
python tools/check_vectors.py
python tools/test_compare_benchmark.py
.\build\windows-msvc-release\benchmarks\Release\keyina_bench.exe > benchmark-result-new.json
dotnet run --project apps/host/Keyina.Host.Benchmarks/Keyina.Host.Benchmarks.csproj -c Release --no-build
```

Compare against checked-in thresholds; do not loosen a threshold without measured justification.

- [ ] **Step 5: Run Linux sanitizer lane where the configured environment is available**

Run:

```bash
cmake --preset linux-clang-asan
cmake --build --preset linux-clang-asan
ASAN_OPTIONS=detect_leaks=1:halt_on_error=1 UBSAN_OPTIONS=halt_on_error=1:print_stacktrace=1 ctest --preset linux-clang-asan --output-on-failure
```

- [ ] **Step 6: Run release contract verification without publishing**

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/windows/build-release.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/windows/verify-release.ps1
```

Do not sign or publish. If the installed resident blocks a self-test, record the precondition and rerun only after explicit authorization to stop it.

- [ ] **Step 7: Write the evidence report**

Create `docs/compatibility/2026-08-05-full-project-bug-hunt-results.md` containing:

- exact commands run;
- pass/fail/blocked results;
- confirmed root causes and changed files;
- benchmark/resource values;
- security/privacy findings;
- UI/accessibility evidence;
- tests skipped and why;
- residual manual-only compatibility risks.

- [ ] **Step 8: Final verification before completion claim**

Run:

```powershell
git diff --check
git status --short
git diff --stat
git diff
```

Verify the diff contains no accidental generated assets, secrets, user-local paths, duplicate logic, unrelated refactors, or modifications to the user's pre-existing tests beyond intentional preservation.
