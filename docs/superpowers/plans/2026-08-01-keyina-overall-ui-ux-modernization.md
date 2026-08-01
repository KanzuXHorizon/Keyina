# Keyina Overall UI/UX Modernization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Modernize all Keyina WinForms surfaces into one coherent, native Fluent, task-first experience without changing resident behavior, settings contracts, or typing-path performance.

**Architecture:** Extend the existing `Keyina.Host.UI.Fluent` presentation layer with semantic tokens and small reusable components, then migrate each surface incrementally. Preserve all business actions and Win32 window styles; tests assert structure, accessibility, keyboard behavior, DPI behavior, and overlay invariants.

**Tech Stack:** .NET 10, WinForms, C#, custom owner-drawn Fluent controls, existing Keyina interactive test harness.

## Global Constraints

- Keep WinForms on `net10.0-windows10.0.19041.0`.
- Do not introduce Electron, WebView, MAUI, WPF, WinUI 3, or a second full UI stack.
- Preserve no-activate and click-through overlay behavior.
- Preserve settings contracts and business actions.
- Keep light, dark, and high-contrast modes functional.
- Avoid material resident-memory, startup-time, or typing-latency regressions.
- Use spacing values 4, 8, 12, 16, 24, and 32 only unless a native control requires otherwise.

---

### Task 1: Semantic design tokens and reusable setting primitives

**Files:**
- Modify: `apps/host/Keyina.Host/UI/Fluent/FluentTheme.cs`
- Modify: `apps/host/Keyina.Host/UI/Fluent/FluentControls.cs`
- Create: `apps/host/Keyina.Host/UI/Fluent/FluentLayout.cs`
- Test: `apps/host/Keyina.Host.Tests/FluentControlsTests.cs`

**Interfaces:**
- Produces: `FluentSpacing`, `FluentControlMetrics`, `FluentTypography`, `FluentSettingRow`, `FluentSectionHeader`, and `FluentInlineMessage`.

- [ ] Add failing tests asserting semantic spacing, control heights, accessible names, focusability, and high-contrast-safe state text.
- [ ] Run `dotnet test apps/host/Keyina.Host.Tests/Keyina.Host.Tests.csproj --filter FluentControls` and confirm failure.
- [ ] Implement tokens and primitives with presentation-only behavior.
- [ ] Run the focused tests and confirm pass.
- [ ] Commit `feat(ui): add semantic Fluent layout primitives`.

### Task 2: Settings information architecture and adaptive shell

**Files:**
- Modify: `apps/host/Keyina.Host/UI/SettingsForm.cs`
- Test: `apps/host/Keyina.Host.Tests/SettingsFormTests.cs`

**Interfaces:**
- Consumes: Task 1 primitives.
- Preserves: all existing section IDs and `SettingsActions` calls.

- [ ] Add failing tests for Core/Tools/System navigation groups, stable page header, no horizontal overflow at minimum width, and local inline validation containers.
- [ ] Run focused Settings tests and confirm failure.
- [ ] Refactor navigation grouping, page spacing, setting rows, narrow-mode selector, and credential validation presentation without changing actions.
- [ ] Run focused Settings tests and confirm pass.
- [ ] Commit `feat(ui): modernize settings information architecture`.

### Task 3: First-run activation checklist

**Files:**
- Modify: `apps/host/Keyina.Host/UI/FirstRunForm.cs`
- Test: `apps/host/Keyina.Host.Tests/FirstRunFormTests.cs`

**Interfaces:**
- Preserves: `Action<string> openSection`, `Action complete`.

- [ ] Add failing tests for typing-first order, optional labels, checklist states, direct typing verification input, and non-competing skip action.
- [ ] Run focused FirstRun tests and confirm failure.
- [ ] Implement checklist-oriented onboarding using shared primitives.
- [ ] Run focused tests and confirm pass.
- [ ] Commit `feat(ui): turn first run into activation checklist`.

### Task 4: Translation preview reading-first layout

**Files:**
- Modify: `apps/host/Keyina.Host/UI/TranslationPreviewForm.cs`
- Test: `apps/host/Keyina.Host.Tests/TranslationPreviewFormTests.cs`

**Interfaces:**
- Preserve no-focus-steal and existing replace/copy behavior.

- [ ] Add failing tests for source/result hierarchy, bounded scrolling, explicit replace/copy actions, loading/error/empty state containers, and keyboard defaults.
- [ ] Run focused tests and confirm failure.
- [ ] Implement compact reading-first layout and semantic states.
- [ ] Run focused tests and confirm pass.
- [ ] Commit `feat(ui): improve translation preview readability`.

### Task 5: Dictation and snippet overlays

**Files:**
- Modify: `apps/host/Keyina.Host/UI/DictationOverlayForm.cs`
- Modify: `apps/host/Keyina.Host/UI/SnippetSuggestionOverlayForm.cs`
- Test: `apps/host/Keyina.Host.Tests/DictationOverlayFormTests.cs`
- Create or modify: `apps/host/Keyina.Host.Tests/SnippetSuggestionOverlayFormTests.cs`

**Interfaces:**
- Preserve no-activate, click-through, keyboard navigation, and overlay lifetime behavior.

- [ ] Add failing tests for listening/transcribing/error visual states, transcript truncation, selected snippet visibility, match emphasis metadata, and bounded work-area sizing.
- [ ] Run focused overlay tests and confirm failure.
- [ ] Implement compact state-first overlays without timers or allocation-heavy animation.
- [ ] Run focused tests and confirm pass.
- [ ] Commit `feat(ui): refine dictation and snippet overlays`.

### Task 6: Accessibility, DPI, regression, and performance verification

**Files:**
- Modify tests under `apps/host/Keyina.Host.Tests/`
- Modify shared UI files only when verification reveals a defect.

**Interfaces:**
- Verifies all prior tasks.

- [ ] Add coverage for tab order, accessible metadata, 200% DPI layout, high contrast, long Vietnamese text, reduced-motion-safe behavior, and zero horizontal clipping.
- [ ] Run `dotnet test apps/host/Keyina.Host.Tests/Keyina.Host.Tests.csproj`.
- [ ] Run `dotnet build Keyina.slnx -c Release`.
- [ ] Inspect final diff for business-logic changes, accidental dependencies, allocation-heavy paint paths, and unrelated edits.
- [ ] Commit `test(ui): verify modernization accessibility and DPI`.
