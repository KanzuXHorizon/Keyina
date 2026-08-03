# Backspace Selection and Release Design

## Goal

Eliminate the intermittent Chromium Backspace behavior where an erase-only Vietnamese edit leaves prior text selected, merge the current GitHub Actions dependency update, and produce a verified Keyina 0.1.8 Windows installer.

## Root cause

Chromium-class targets use selection replacement because direct Backspace plus Unicode injection can be reordered. The selection sequence currently emits `Shift+Left` for `backspace_count`, releases Shift, and then emits Unicode insertion events. When the input decision contains erasure but no insertion, the sequence ends after selection, so the target retains highlighted text instead of deleting it.

## Design

1. Preserve selection replacement for edits that both erase and insert text.
2. For erase-only decisions, emit ordinary marked `VK_BACK` down/up pairs. This matches keyboard delivery, leaves no active selection, and keeps Keyina-injected events isolated by `kKeyinaInjectionMarker`.
3. Add an input-sequence regression test that proves erase-only selection delivery contains Backspace events and no Shift/Left events.
4. Keep the existing partial-input recovery path unchanged; ordinary Backspace pairs already recover safely through the generic unmatched-key release logic.
5. Update and merge Dependabot PR #2 only after its rebased CI passes, then pull merged `main` before committing the bug fix.
6. Bump the shared product version from 0.1.7 to 0.1.8, synchronize native/managed visible versions and lock files, run the full release verification, and build the Inno Setup installer.

## Verification

- Focused native input-injection unit test fails before the production change and passes after it.
- Full native Debug and Release CTest suites pass.
- Managed Release build and 335-test suite pass.
- Linux Clang ASan/UBSan suite passes.
- Release script verifies package contents, checksums, manifest, and installer lifecycle.
- Installer file version and release manifest report 0.1.8.
