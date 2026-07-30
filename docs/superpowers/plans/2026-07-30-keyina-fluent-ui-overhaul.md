# Keyina Fluent UI Overhaul Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the prototype WinForms chrome with a cohesive Windows 11 Fluent experience across the settings window, controls, theme, title bar, tray menu, diagnostics, and screenshot-review tooling without changing the Vietnamese typing engine.

**Architecture:** Keep .NET 10 WinForms and the resident-host architecture. Extract visual tokens and reusable native controls from `SettingsForm`, add a Win32 window-theme adapter and a custom ToolStrip renderer, then drive both settings and tray visuals from the existing immutable runtime snapshot. Favor native behaviors, graceful Windows 10 fallback, per-monitor DPI scaling, and deterministic screenshot tests over framework migration.

**Tech Stack:** .NET 10, Windows Forms, C#, GDI+/Win32 DWM interop, existing custom test runner and screenshot renderer.

## Global Constraints

- Preserve ordinary typing behavior and all existing runtime actions.
- Do not migrate to WinUI 3, WPF, Electron, WebView, or add Windows App SDK runtime deployment.
- Use Windows 11 Mica only when supported; use a solid Fluent fallback elsewhere.
- Use Acrylic semantics only for the transient tray menu appearance; the implementation remains a custom WinForms ToolStrip renderer.
- Follow the system light/dark preference and remain usable in high contrast.
- Preserve unrelated working-tree changes and do not commit without explicit authorization.
- Vietnamese is the primary visible language; no mixed English/Vietnamese copy on a single production screen.
- Every interactive control must have keyboard focus, accessible text, and a minimum practical desktop target size.

---

### Task 1: Lock visual contracts with tests

**Files:**
- Modify: `apps/host/Keyina.Host.Tests/SettingsFormTests.cs`
- Modify: `apps/host/Keyina.Host.Tests/KeyinaApplicationContextTests.cs`

**Interfaces:**
- Consumes: existing `SettingsForm`, `KeyinaApplicationContext` test hooks.
- Produces: assertions for theme-aware shell, named Fluent controls, Vietnamese copy, modern tray renderer, and deterministic layout.

- [ ] Add failing assertions for `FluentNavigationView`, theme selector, custom toggle controls, title-bar theming, no production `DataGridView`, and localized labels.
- [ ] Add failing assertions that the tray menu uses a custom renderer, image margin, modern padding, localized commands, and explicit accessible names.
- [ ] Run focused host tests and confirm failures are caused by missing Fluent behavior.

### Task 2: Introduce Fluent visual foundation

**Files:**
- Create: `apps/host/Keyina.Host/UI/Fluent/FluentTheme.cs`
- Create: `apps/host/Keyina.Host/UI/Fluent/FluentWindow.cs`
- Create: `apps/host/Keyina.Host/UI/Fluent/FluentControls.cs`
- Create: `apps/host/Keyina.Host/UI/Fluent/FluentTrayRenderer.cs`
- Modify: `apps/host/Keyina.Host/Program.cs`

**Interfaces:**
- Produces: `FluentTheme.Current`, `FluentTheme.Apply(Control)`, `FluentWindow.Apply(Form)`, `FluentToggle`, `FluentCard`, `FluentNavigationButton`, `FluentStatusBadge`, and `FluentTrayRenderer`.

- [ ] Implement system theme detection with high-contrast fallback and brand-derived light/dark palettes.
- [ ] Apply .NET 10 system color mode before any UI is created, suppressing only the required experimental warning locally.
- [ ] Add DWM dark-title-bar, rounded-corner, and Mica attributes with version-safe fallback.
- [ ] Implement reusable double-buffered controls with visible keyboard focus and DPI-scaled metrics.
- [ ] Implement a custom ToolStrip renderer with rounded selection, separators, check glyphs, icon spacing, and dark/light palettes.
- [ ] Run focused tests and build.

### Task 3: Rebuild the settings shell and pages

**Files:**
- Rewrite: `apps/host/Keyina.Host/UI/SettingsForm.cs`
- Modify: `apps/host/Keyina.Host/UI/SettingsScreenshotRenderer.cs`

**Interfaces:**
- Consumes: existing `SettingsSnapshot` and `SettingsActions` unchanged.
- Produces: responsive six-page Fluent settings experience with the same control names required by runtime/tests.

- [ ] Replace fixed-position sidebar with a responsive navigation shell using icons, selection indicator, status footer, and compact collapse behavior near minimum width.
- [ ] Replace checkbox cards with Fluent setting rows and right-aligned switches.
- [ ] Rebuild Overview with one primary readiness surface and compact measurable status rows.
- [ ] Rebuild Typing with a prominent real typing test, inline feedback, and Context Guard information.
- [ ] Rebuild Speech with localized credential state, password reveal action, save/remove actions, and privacy notice.
- [ ] Rebuild Hotkeys as editable-looking command rows with conflict/status presentation while preserving current immutable shortcuts.
- [ ] Replace the snippets `DataGridView` with a responsive list surface, search field, command bar, empty/loading-ready structure, and current deterministic sample rows.
- [ ] Rebuild Diagnostics as individual health checks with progress, result banner, copy/open actions, and non-blocking execution.
- [ ] Preserve control names, runtime event wiring, accessible names, tab order, and snapshot mapping.
- [ ] Render all gallery pages and inspect at 980×690 and the minimum supported size.

### Task 4: Modernize tray and runtime window behavior

**Files:**
- Modify: `apps/host/Keyina.Host/Runtime/KeyinaApplicationContext.cs`
- Test: `apps/host/Keyina.Host.Tests/KeyinaApplicationContextTests.cs`

**Interfaces:**
- Consumes: `FluentTrayRenderer`, current `SettingsSnapshot`, existing icons and actions.
- Produces: polished right-click menu and consistent runtime visual state.

- [ ] Add a branded header/status item, concise localized commands, standard icons where unambiguous, shortcut labels, separators, and a destructive exit treatment.
- [ ] Apply modern menu sizing, padding, rounded renderer, dark/light palette, and keyboard navigation.
- [ ] Add setup/repair command visibility based on readiness without creating contradictory state.
- [ ] Keep double-click opening Overview, tooltip state mapping, and start-with-Windows check behavior.
- [ ] Apply Fluent window attributes every time the settings form is created or system preference changes.
- [ ] Run application-context tests.

### Task 5: Verification and visual evidence

**Files:**
- Modify only files required by observed failures.
- Update: `docs/screenshots/*.png`

**Interfaces:**
- Produces: fresh tests, build output, and screenshot evidence.

- [ ] Build `Keyina.slnx` with warnings as errors.
- [ ] Run all host tests.
- [ ] Render the six-page screenshot gallery and inspect each image for clipping, mixed-language copy, focus/layout defects, and inconsistent density.
- [ ] Run native tests that are not blocked by unrelated working-tree changes.
- [ ] Inspect the final diff for accidental engine/runtime changes, generated junk, secrets, and edits outside the UI/tray scope.
- [ ] Report checks actually run, remaining manual Windows 10/11, DPI, high-contrast, and tray-shell validation gates.
