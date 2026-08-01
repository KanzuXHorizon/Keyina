# Snippet Control Recycling Design

## Goal

Reduce the cost of applying a genuinely changed 1,000-snippet snapshot without changing Keyina's current Settings UI, snippet behavior, filtering, keyboard support, or persistence model.

The previous implementation cleared `snippetsList` and constructed every WinForms control again. On the development machine this cost approximately 557 ms and 56.3 MB of allocation for 1,000 custom snippets.

## Approaches considered

### Full virtualized/custom-drawn list

This provides the lowest steady-state control count and the best theoretical scaling. It also requires new hit testing, keyboard navigation, accessibility peers, focus handling, scrolling, action routing, theme rendering, and screenshot coverage. It is too much behavioral risk for the first optimization step.

### Exact-key incremental reconciliation

This keeps cards whose trigger is unchanged and only creates or removes changed triggers. It is simple and helps one-row edits, but a bulk import or rename of all triggers still recreates all controls.

### Exact-key reconciliation plus control recycling — selected

Cards with matching triggers are retained. Unmatched custom cards are placed in a recycle pool and rebound to unmatched desired snippets before any new control is created. Built-in rows remain fixed and are never recycled into custom rows. Controls are only created or disposed when the total custom-row count changes.

This preserves the current WinForms layout and accessibility behavior while addressing both ordinary edits and bulk replacement with the same number of snippets.

## Architecture

`SettingsForm.AddSnippetRows()` becomes a reconciliation operation:

1. Build the desired ordered `SnippetRow` sequence from built-ins plus `currentSnapshot.Snippets`.
2. Capture the current scroll position.
3. Match existing cards to desired rows by trigger using `StringComparer.OrdinalIgnoreCase`.
4. Put unmatched custom cards into a recycle queue.
5. For each desired row, use an exact match, then a recycled custom card, then create a new card as the final fallback.
6. Rebind each selected card through `UpdateSnippetRow(FluentCard card, SnippetRow row)`.
7. Remove and dispose only unused surplus cards.
8. Reorder controls to the desired sequence without clearing the collection.
9. Reapply an active search/filter and restore the scroll position.

`CreateSnippetRow` creates only the stable visual/control tree. Button handlers do not close over the original `SnippetRow`; they resolve the current row from the owning card's `Tag` when invoked. This makes recycled cards safe.

Static Settings labels are indexed once by name. Snapshot updates no longer search recursively through the full control tree to update hotkey copy.

## Card rebinding contract

`UpdateSnippetRow` updates only changed values:

- card `Name` and `Tag`;
- trigger, expansion, and scope labels;
- accessible name and description.

Child controls keep stable role names such as `snippetTrigger`, `snippetEdit`, and `snippetDelete`. This avoids unnecessary name changes and lets recycled controls retain their event wiring.

A built-in card has the existing three-column shape. A custom card has the existing six-column shape. Recycling is allowed only between custom cards of the same shape.

## User-visible behavior

- The UI looks and behaves the same.
- Search and type filters remain active after a snapshot update.
- Scroll position remains stable when the list shape permits it.
- Focus remains on a reused control when it still exists.
- Editing, duplicating, and deleting a recycled card act on its newly bound snippet.
- No background thread, timer, animation, dependency, or network work is added.

## Performance evidence

The existing `application_settings_apply_1000_snippets` benchmark remains the primary comparison. On the same development machine and Release configuration:

- median changed-snapshot time fell from approximately 557 ms to 92.65 ms;
- allocation fell from approximately 56.3 MB to 2.51 MB per operation;
- unchanged 1,000-snippet snapshots remain on the cache fast path at approximately 6.9 microseconds and 1.78 KB per operation;
- no absolute product guarantee is inferred from one machine.

Focused behavior tests, rather than hardware-specific timing assertions, enforce recycling in CI.

## Error and lifecycle handling

- Reconciliation runs under `SuspendLayout`/`ResumeLayout` with `try/finally`.
- Removed surplus cards are disposed after removal.
- A malformed card without a `SnippetRow` tag is not recycled; it is removed and disposed.
- Reconciliation does not mutate the source snapshot.
- Existing snippet validation remains the authority for duplicate triggers and invalid configuration.

## Verification

1. A failing test proved a full 1,000-to-1,000 replacement changed every card reference before implementation.
2. The same test now proves custom card references are recycled and rebound to the new rows.
3. Additional tests cover one add, one remove, same-trigger content updates, filtering, repeated replacements, and current-row action resolution.
4. The focused Settings benchmark is run on the same machine and Release configuration.
5. Managed Release tests, native CTest, `git diff --check`, UI detector, and publish verification must pass.
6. No commit or push is performed without explicit authorization.
