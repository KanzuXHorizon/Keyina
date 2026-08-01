# Keyina Overall UI/UX Modernization Design

Date: 2026-08-01
Status: Approved direction, ready for implementation planning

## 1. Goal

Modernize Keyina's complete desktop UI into a coherent, native, task-first Fluent experience while preserving the current WinForms/.NET 10 foundation, low resource usage, resident-process behavior, Win32 interoperability, no-activate overlays, keyboard hooks, and existing business logic.

The work covers:

- Settings
- First-run experience
- Translation preview
- Dictation overlay
- Snippet suggestion overlay
- Shared Fluent controls, theme, spacing, typography, state handling, accessibility, DPI behavior, and tests

## 2. Product Constraints

Keyina is a resident Windows utility rather than a content-heavy desktop application. The typing engine, global hooks, tray lifecycle, foreground-window behavior, and latency profile are more important than adopting a newer UI framework.

Required constraints:

- Keep WinForms on `net10.0-windows10.0.19041.0`.
- Do not introduce Electron, WebView, MAUI, or a second full UI stack.
- Preserve no-activate and click-through behavior for overlays.
- Preserve existing settings contracts and business actions.
- Avoid increasing resident memory, startup time, or typing-path latency materially.
- Keep light, dark, and high-contrast modes functional.
- Keep the work incremental and testable by surface.

## 3. Selected Approach

Use a **Native Fluent WinForms modernization** strategy.

This means:

1. Expand the existing `Keyina.Host.UI.Fluent` layer into a complete design system.
2. Refactor each UI surface around user tasks and information hierarchy.
3. Keep native behavior and runtime architecture intact.
4. Add focused reusable components instead of rewriting all forms from scratch.
5. Verify visual, keyboard, accessibility, DPI, overflow, and runtime behavior after each vertical slice.

## 4. Rejected Alternatives

### 4.1 Light visual refinement only

Rejected because it would not address structural problems such as oversized settings pages, inconsistent hierarchy, inline validation gaps, narrow-layout navigation, duplicated layout logic, and weak loading/error states.

### 4.2 WPF rewrite

Rejected because it introduces a parallel UI architecture and migration risk without enough benefit for a small resident utility. Existing WinForms tests, native behavior, and Fluent controls would need substantial replacement.

### 4.3 WinUI 3 rewrite

Rejected for the current phase because the migration cost, deployment complexity, Windows App SDK dependency, HWND interop work, overlay behavior risk, and test rewrite outweigh the final visual improvement. It may be reconsidered only if Settings becomes a much larger companion application separated from the resident host.

## 5. Design Principles

### 5.1 Native first

The interface should feel like a precise Windows 11 utility, not a web dashboard embedded in a desktop shell.

### 5.2 Task first

Each page should expose the user's primary task before explanatory copy or advanced configuration.

### 5.3 Progressive disclosure

Advanced, provider-specific, local-network, diagnostic, and destructive controls should remain available without dominating the default experience.

### 5.4 Compact but readable

Reduce unnecessary vertical space and nested cards while keeping touch-safe targets, clear hierarchy, and adequate scanability.

### 5.5 State is local

Validation, loading, warning, and success feedback should appear next to the control or task that caused it. A global status message should be reserved for cross-page or application-wide state.

### 5.6 Keyboard and accessibility are first-class

Every primary operation must be reachable and understandable without a pointer or color-only signal.

## 6. Shared Design System

Create or formalize the following tokens and reusable behaviors.

### 6.1 Spacing

Use a fixed spacing scale:

- 4: icon/text micro-gap
- 8: compact control gap
- 12: standard internal gap
- 16: row and card padding
- 24: section separation
- 32: page-level separation

Avoid arbitrary margins unless required by a platform control.

### 6.2 Typography

Define semantic roles rather than assigning raw sizes throughout forms:

- Display: first-run hero only
- Page title
- Section title
- Body
- Secondary body
- Caption
- Monospace diagnostic
- Keycap/hotkey label

Use `Segoe UI Variable Text` with robust fallback to `Segoe UI` where unavailable.

### 6.3 Geometry

Standardize:

- Compact control height: 32
- Default control height: 36
- Prominent action height: 40
- Small radius for inputs and buttons
- Medium radius for cards and popovers
- One-pixel borders in normal scale, adjusted for high contrast

### 6.4 Semantic state

Support consistently:

- Default
- Hover
- Pressed
- Focused
- Disabled
- Loading
- Success
- Warning
- Error
- Selected

No state may rely on color alone. Pair color with text, iconography, or shape.

### 6.5 Reusable components

Introduce or refine:

- `FluentSettingRow`
- `FluentSectionHeader`
- `FluentInlineMessage`
- `FluentCredentialField`
- `FluentEmptyState`
- `FluentActionBar`
- `FluentProgressState`
- improved navigation item notification state
- shared scroll-container and page-padding helpers

Components must remain small, independently testable, and limited to presentation behavior.

## 7. Settings Experience

### 7.1 Information architecture

Group navigation into:

- Core
  - Overview
  - Typing
- Tools
  - Speech
  - Translation
  - Snippets
- System
  - Hotkeys
  - Applications
  - Diagnostics

The grouping is visual only; existing section identifiers remain stable.

### 7.2 Layout behavior

Expanded mode:

- Fixed sidebar with icon and label
- Stable page title and subtitle
- Independent content scroll region

Compact mode:

- Narrower sidebar with reduced label width or icon-priority treatment
- Tooltips retained

Narrow mode:

- Replace squeezed sidebar behavior with a compact top selector or overlay navigation affordance
- Preserve page title and content width
- No horizontal clipping at the minimum supported size

### 7.3 Page structure

Each page follows:

1. Page title and one-line purpose
2. Current state or action required
3. Primary settings
4. Optional/provider-specific details
5. Advanced or diagnostic content

Avoid nested cards unless a child group has its own meaningful boundary.

### 7.4 Validation and credentials

- Show validation next to the specific key or endpoint field.
- Preserve password masking and Credential Manager storage.
- Save buttons only enable when the current value is valid and changed.
- Removal requires clear wording and predictable focus after completion.
- Local/private endpoint permission remains visually marked as advanced and security-sensitive.

### 7.5 Diagnostics

- Keep the typing sandbox isolated and explicit.
- Make recording state visually obvious.
- Preserve raw logs and filtering without making diagnostics look like a primary workflow.
- Avoid high-frequency UI invalidation when the page is not active.

## 8. First-Run Experience

Replace the current static three-card layout with an activation-oriented checklist.

Order:

1. Confirm typing works
2. Configure speech, optional
3. Configure translation, optional

Requirements:

- The first task offers a direct "type here" verification path.
- Optional features are labeled optional.
- Each item shows incomplete, configured, or unavailable state.
- The primary action completes setup.
- Skip remains secondary and does not visually compete.
- Closing or skipping never disables the core typing experience.

## 9. Translation Preview

Use a reading-first compact window.

Requirements:

- Clear distinction between source and translated text.
- Long content scrolls without expanding beyond the work area.
- Primary actions: replace and copy.
- Secondary actions remain visually subordinate.
- Keyboard shortcuts and default action are explicit.
- Loading, provider error, empty selection, and retry states are represented.
- The preview must not steal focus unexpectedly from the source application.
- Text remains selectable where behavior permits.

## 10. Dictation Overlay

Use a glanceable, low-noise state model.

States:

- Starting
- Listening
- Processing
- Completed
- Microphone unavailable
- Network/provider error
- Cancelled

Requirements:

- Show the current state before transcript detail.
- Keep transcript length bounded with meaningful truncation.
- Do not animate continuously when reduced motion is enabled.
- Preserve click-through/no-activate behavior where currently required.
- Avoid layout shifts as status text changes.

## 11. Snippet Suggestion Overlay

Design for keyboard-first completion.

Requirements:

- Strong selected-row indication.
- Highlight matched text without reducing legibility.
- Support arrow navigation, Enter/Tab confirmation, and Escape dismissal.
- Bound height and scroll long result sets.
- Preserve no-activate behavior.
- Provide clear empty/no-match handling without opening a large surface.
- Avoid expensive repainting during rapid typing.

## 12. Accessibility

Acceptance requirements for all surfaces:

- Logical tab order
- Visible focus indicator
- Correct accessible name, role, value, and description
- Keyboard access to primary and destructive actions
- No color-only status communication
- Functional at 125%, 150%, 175%, and 200% scaling
- Functional in Windows high contrast
- Graceful text expansion for Vietnamese labels and provider errors
- Reduced-motion behavior where animation exists
- Minimum contrast target of 4.5:1 for normal text and 3:1 for large text or essential non-text UI

## 13. Performance and Resource Constraints

- Do not add timers that run when a surface is hidden.
- Avoid rebuilding entire settings pages for small state changes.
- Reuse controls and virtualize or recycle long snippet lists where already supported.
- Avoid per-keystroke allocations in suggestion rendering beyond existing bounded behavior.
- Keep UI changes off the typing engine's latency-critical path.
- Add or retain focused performance gates for navigation, overlay updates, and snippet rendering.

## 14. Error Handling

- Provider errors must identify the affected service and the next available action.
- Invalid configuration must not crash or block unrelated features.
- Failed theme or system-setting reads fall back safely.
- Unsupported visual effects must degrade to a stable opaque surface.
- Runtime exceptions in optional UI updates should be captured through existing diagnostics without terminating the resident host.

## 15. Testing Strategy

### 15.1 Component tests

Add tests for:

- token and palette behavior
- layout mode thresholds
- setting-row accessibility and enabled states
- credential validation presentation
- inline message severity
- overlay state transitions
- keyboard navigation contracts

### 15.2 Form tests

Expand existing tests to verify:

- expected controls and accessible metadata
- tab order
- minimum size behavior
- narrow layout without clipping
- bounded translation and transcript content
- no-activate/click-through flags
- high-contrast palette selection

### 15.3 Runtime verification

Perform manual or automated checks for:

- light, dark, high contrast
- 100%, 150%, 200% DPI
- minimum supported window size
- long Vietnamese text and long provider errors
- keyboard-only navigation
- overlay behavior in real foreground applications
- no focus theft
- no new console/runtime exceptions

### 15.4 Regression protection

Use matched before/after screenshots where browser-style tooling is not applicable, and retain behavior assertions as the source of truth. Screenshots support visual review but do not replace functional tests.

## 16. Implementation Slices

Implement in this order:

1. Shared tokens, typography, state model, and reusable primitives
2. Settings shell and navigation responsiveness
3. Settings page structure and inline state handling
4. First-run activation flow
5. Translation preview
6. Dictation overlay
7. Snippet suggestion overlay
8. Accessibility, DPI, high-contrast, and overflow hardening
9. Performance verification and final visual consistency pass

Each slice must build and pass focused tests before proceeding.

## 17. Non-Goals

- Rewriting the typing engine
- Changing provider contracts
- Introducing cloud synchronization
- Replacing WinForms with WPF or WinUI 3
- Redesigning tray behavior beyond visual consistency
- Adding animation for decoration alone
- Broad refactoring outside UI boundaries

## 18. Completion Criteria

The modernization is complete when:

- All five primary UI surfaces use the shared design system.
- Settings navigation works without clipping in expanded, compact, and narrow modes.
- Every feature has local loading, empty, success, warning, and error presentation where applicable.
- Primary workflows are keyboard accessible.
- High contrast and 200% DPI remain usable.
- Overlays preserve focus and native window behavior.
- Existing behavior tests pass and new focused UI tests pass.
- No material regression is observed in startup time, resident memory, typing latency, or rapid snippet rendering.
- The final diff contains no unrelated architectural rewrite.
