# Settings Responsive, Accessibility, and Hierarchy Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Keyina Settings responsive from 760 px upward, fully keyboard-operable, and less dense at narrow widths without removing content.

**Architecture:** Add a three-mode layout policy to `SettingsForm` and a compact paint mode to `FluentNavigationButton`. Keep the existing two-column shell and page implementations; only adapt sidebar width, metadata visibility, content padding, card density, and focus/navigation behavior.

**Tech Stack:** .NET 10, WinForms, owner-drawn Fluent controls, Keyina custom test runner, Settings screenshot renderer.

## Global Constraints

- Preserve all current Settings actions, data binding, snippet lazy loading, and snapshot caching.
- Do not introduce a hamburger menu, drawer animation, additional thread, timer, dependency, or network work.
- Do not change current section names or hotkeys.
- Preserve DPI scaling, light/dark palette support, and the existing Fluent owner-drawn controls.
- Do not commit or push without explicit user authorization.

---

### Task 1: Preserve current snippet performance behavior in the isolated worktree

**Files:**
- Modify: `apps/host/Keyina.Host/UI/SettingsForm.cs`
- Modify: `apps/host/Keyina.Host.Tests/SnippetSuggestionTests.cs`

**Interfaces:**
- Preserves: `SettingsForm.OpenSection(string)` and lazy snippet row initialization.
- Produces: a baseline matching the current dirty checkout before responsive work.

- [ ] Apply the current checkout's snippet lazy-loading and unchanged-snapshot cache changes to the worktree.
- [ ] Update the snippet management test to open the snippets section before inspecting rows.
- [ ] Run the snippet and Settings focused tests and confirm the synchronized baseline passes.

### Task 2: Add responsive layout modes

**Files:**
- Modify: `apps/host/Keyina.Host.Tests/SettingsFormTests.cs`
- Modify: `apps/host/Keyina.Host/UI/SettingsForm.cs`
- Modify: `apps/host/Keyina.Host/UI/Fluent/FluentControls.cs`

**Interfaces:**
- Produces: `SettingsLayoutMode` with `Expanded`, `Compact`, and `Narrow`.
- Produces: `SettingsForm.CurrentLayoutMode` for deterministic tests.
- Produces: `FluentNavigationButton.Compact`.

- [ ] Add failing tests for 760 px narrow mode, 900 px compact mode, and 1100 px expanded mode.
- [ ] Add a failing test requiring icon-only navigation and preserved accessible names/tooltips in narrow mode.
- [ ] Implement mode selection at 860 and 1020 px breakpoints.
- [ ] In narrow mode use 76 px sidebar, 18 px content padding, hidden secondary sidebar metadata, hidden theme status, and compact navigation painting.
- [ ] In compact mode use 196 px sidebar and 22 px content padding.
- [ ] In expanded mode restore 228 px sidebar, 30 px content padding, and all metadata.
- [ ] Set the supported minimum size to 760×620 and run focused tests.

### Task 3: Add keyboard navigation and focus transfer

**Files:**
- Modify: `apps/host/Keyina.Host.Tests/SettingsFormTests.cs`
- Modify: `apps/host/Keyina.Host/UI/SettingsForm.cs`

**Interfaces:**
- Produces: navigation behavior for `Keys.Up`, `Keys.Down`, `Keys.Home`, and `Keys.End`.
- Produces: `FocusFirstInteractiveControl(Panel)` used after visible section changes.

- [ ] Add failing tests that show the form, focus a navigation button, send Down/Home/End, and assert the selected/focused section.
- [ ] Add a failing test requiring `OpenSection` to focus the first enabled visible tab-stop control of the target page.
- [ ] Implement wraparound navigation over the dictionary insertion order.
- [ ] Suppress handled navigation keypresses to avoid system beeps.
- [ ] Move focus only while the form is visible; construction and snapshot application must not steal focus.
- [ ] Run focused keyboard and credential-focus tests.

### Task 4: Apply responsive density and verify every page

**Files:**
- Modify: `apps/host/Keyina.Host.Tests/SettingsFormTests.cs`
- Modify: `apps/host/Keyina.Host/UI/SettingsForm.cs`
- Modify: `apps/host/Keyina.Host/UI/SettingsScreenshotRenderer.cs`

**Interfaces:**
- Produces: narrow content-card padding of 16 px and bottom margin of 10 px, while snippet list rows retain 6 px spacing.
- Produces: compact screenshot gallery support for 760×620.

- [ ] Add failing tests requiring narrow card/stack density and no horizontal scrollbar on every page at 760×620.
- [ ] Apply density recursively only to page cards and vertical stacks; restore standard values in compact/expanded modes.
- [ ] Extend the screenshot renderer with an optional client size and filename prefix without changing the existing default gallery.
- [ ] Render all eight pages at 760×620 and 1100×760.
- [ ] Inspect screenshots for clipping, overlap, unreadable icon-only navigation, and missing focus/action surfaces.

### Task 5: Full verification and checkout synchronization

**Files:**
- Review all modified files and generated screenshot artifacts.

**Interfaces:**
- Uses: Release managed/native suites and `git diff --check`.

- [ ] Run the full managed Release build and test suite.
- [ ] Run native Release CTest.
- [ ] Run `git diff --check` and inspect the scoped diff for generated noise or unrelated changes.
- [ ] Compare SHA-256 before synchronizing modified source/test files to `F:\Keyina`.
- [ ] Re-run full managed and native verification on the actual checkout.
- [ ] Publish only after all checks pass and restart exactly one resident from `artifacts/publish/win-x64`.
