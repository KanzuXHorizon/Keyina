# Settings Responsive, Accessibility, and Hierarchy Design

## Goal

Make Keyina Settings comfortable and fully operable from 760 px to large desktop widths without horizontal overflow, lost context, or keyboard traps.

## Layout modes

### Expanded: width >= 1020 px

- 228 px sidebar with icon and label navigation.
- Full product identity, privacy summary, version, and system-theme metadata.
- 30 px horizontal content padding and standard card density.

### Compact: width 860–1019 px

- 196 px sidebar with labels retained.
- 22 px horizontal content padding.
- Full navigation and metadata remain visible.
- Standard card hierarchy remains unchanged.

### Narrow: width 760–859 px

- 76 px icon-only sidebar using the same navigation buttons.
- Each icon retains `AccessibleName` and receives a tooltip with the full section name.
- Product subtitle, privacy summary, version, and system-theme metadata are hidden because they are secondary to the active task.
- Content padding falls to 18 px.
- Content cards use 16 px internal padding and 10 px vertical separation; dense snippet list rows retain their existing 6 px separation to avoid inflating long libraries.
- No page may expose horizontal scrolling at the 760×620 supported minimum.

## Keyboard and accessibility

- Navigation buttons support Up/Down/Home/End and wrap between the first and last section.
- Activating a section focuses the first enabled, visible, tab-stop control inside that page when the form is visible.
- Icon-only navigation remains a standard button surface, not a custom inaccessible hit target.
- Focus cues remain visible through the existing owner-drawn navigation button.
- The sidebar and navigation region receive explicit accessible names and descriptions.

## Hierarchy and density

- The active section title and subtitle remain visible in every mode.
- Narrow mode removes secondary chrome instead of hiding task instructions.
- Card density changes only by layout mode; content, labels, and actions do not disappear.
- Theme status is hidden only in narrow mode because it is informational and available elsewhere.

## Constraints

- Preserve all current Settings actions, data binding, snippet lazy loading, and snapshot caching.
- Do not introduce a hamburger menu, drawer animation, additional thread, timer, dependency, or network work.
- Do not change current section names or hotkeys.
- Preserve DPI scaling, light/dark palette support, and the existing Fluent owner-drawn controls.
- Do not commit or push without explicit user authorization.

## Verification

1. Focused tests must fail before implementation for narrow layout, keyboard navigation, focus transfer, and compact density.
2. At 760×620, every Settings page must avoid horizontal scroll and keep the active page within the client area.
3. Expanded layout must retain full navigation labels and metadata.
4. Arrow/Home/End navigation must select and focus the expected section.
5. Release build must have zero warnings and all managed/native tests must pass.
6. A screenshot gallery must be rendered at 760×620 and 1100×760 for visual comparison.
