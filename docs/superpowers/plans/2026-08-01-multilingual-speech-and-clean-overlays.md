# Multilingual Speech and Clean Overlays Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add configurable multilingual speech recognition with auto-detect and convert Keyina overlays/popups to clean borderless surfaces with in-content close controls.

**Architecture:** Store the selected Speechmatics language in `KeyinaConfiguration`, expose it through settings snapshot/actions, and construct each speech session from the current language. Keep `auto` as the default while allowing explicit provider language codes. Reuse existing Fluent UI helpers for compact borderless overlay headers and preserve keyboard, drag, resize, focus, and cancellation behavior.

**Tech Stack:** .NET 8, WinForms, Speechmatics realtime WebSocket API, existing Keyina configuration/test infrastructure.

## Global Constraints

- Default speech language is `auto`.
- Explicit language selection must persist in `settings.json` and affect newly created sessions.
- Invalid or unsupported language codes must fail configuration validation.
- Existing Vietnamese typing must remain independent from speech failures.
- Overlay close controls must be keyboard accessible and must not introduce Windows minimize/maximize/title-bar chrome.
- Preserve unrelated working-tree changes.

---

### Task 1: Speech language configuration and validation

**Files:**
- Create: `apps/host/Keyina.Host.Core/Speech/SpeechLanguageCatalog.cs`
- Modify: `apps/host/Keyina.Host.Core/Configuration/KeyinaConfiguration.cs`
- Modify: `apps/host/Keyina.Host/Configuration/AtomicConfigurationStore.cs`
- Test: `apps/host/Keyina.Host.Tests/ConfigurationStoreTests.cs`

**Interfaces:**
- Produces: `SpeechLanguageCatalog.Supported`, `Normalize(string)`, and `KeyinaConfiguration.SpeechLanguage`.

- [ ] Add failing round-trip and invalid-language configuration tests.
- [ ] Implement a curated Speechmatics language catalog including `auto`, Vietnamese, English, Japanese, Korean, Chinese, French, German, Spanish, Portuguese, Italian, Thai, Indonesian, and Russian.
- [ ] Add `SpeechLanguage = "auto"` with validation and legacy-load normalization.
- [ ] Run focused configuration tests.

### Task 2: Runtime session language wiring

**Files:**
- Modify: `apps/host/Keyina.Host/Speech/DictationContracts.cs`
- Modify: `apps/host/Keyina.Host/Runtime/KeyinaApplicationContext.cs`
- Modify: `apps/host/Keyina.Host.Tests/SpeechmaticsProtocolTests.cs`
- Modify: `apps/host/Keyina.Host.Tests/KeyinaApplicationContextTests.cs`

**Interfaces:**
- Consumes: `KeyinaConfiguration.SpeechLanguage`.
- Produces: `SpeechmaticsSessionFactory(Func<string> languageProvider, ...)` so every new session uses the latest selected language.

- [ ] Add tests proving auto is default and an explicit language reaches `StartRecognition`.
- [ ] Change the factory to resolve and validate the language per session creation.
- [ ] Wire the application context to the current configuration value.
- [ ] Run speech/session/application-context tests.

### Task 3: Settings dropdown and persistence action

**Files:**
- Modify: `apps/host/Keyina.Host/UI/SettingsModels.cs`
- Modify: `apps/host/Keyina.Host/UI/SettingsForm.cs`
- Modify: `apps/host/Keyina.Host/Runtime/KeyinaApplicationContext.cs`
- Test: `apps/host/Keyina.Host.Tests/KeyinaApplicationContextTests.cs`

**Interfaces:**
- Produces: `SettingsSnapshot.SpeechLanguage` and `SettingsActions.SetSpeechLanguage`.

- [ ] Add snapshot/action tests for speech language changes.
- [ ] Add a Fluent-styled dropdown to the speech page with localized display names and stable provider codes.
- [ ] Persist changes atomically and ensure the dropdown does not fire during snapshot binding.
- [ ] Update speech status copy to show Auto or the selected language.
- [ ] Run settings/application tests.

### Task 4: Borderless translation and popup surfaces

**Files:**
- Modify: `apps/host/Keyina.Host/UI/TranslationPreviewForm.cs`
- Modify: `apps/host/Keyina.Host/UI/HotkeyCaptureDialog.cs`
- Modify: `apps/host/Keyina.Host/UI/FirstRunForm.cs`
- Modify: `apps/host/Keyina.Host/UI/SnippetEditorDialog.cs`
- Modify: `apps/host/Keyina.Host/UI/DictationOverlayForm.cs`
- Modify: `apps/host/Keyina.Host/UI/SnippetSuggestionOverlayForm.cs`
- Test: relevant form tests under `apps/host/Keyina.Host.Tests/`

**Interfaces:**
- Produces: borderless forms with an accessible in-content close/cancel button and retained drag/resize behavior where appropriate.

- [ ] Add or update form construction tests for `FormBorderStyle.None`, disabled system chrome, and close controls.
- [ ] Build compact custom headers using existing theme helpers; avoid a new UI framework.
- [ ] Add drag handling and resize hit-testing only to forms that were previously resizable.
- [ ] Preserve Escape, cancel, focus, TopMost, and no-activate semantics.
- [ ] Run UI tests and screenshot renderer.

### Task 5: Verification and documentation

**Files:**
- Modify: `README.md`
- Modify: `docs/compatibility/speechmatics.md`

- [ ] Update documentation for auto detection versus explicit language selection.
- [ ] Run `dotnet test` for host tests and the repository’s available build/self-test commands.
- [ ] Inspect `git diff --check`, final diff scope, and confirm no API keys or generated artifacts were added.
