# Backspace Selection and Release Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Merge the current GitHub Actions updates, fix erase-only Backspace on Chromium targets, and create a verified Keyina 0.1.8 Windows installer.

**Architecture:** Keep the current delivery-mode architecture. Correct only the erase-only branch of `BuildSelectionReplacementSequence`: replacement edits continue to select then insert, while pure erasure emits marked Backspace pairs and cannot leave a selection active.

**Tech Stack:** C++20, Win32 `SendInput`, CMake/CTest, .NET 10, PowerShell release scripts, Inno Setup, GitHub Actions.

## Global Constraints

- Preserve existing Chromium selection replacement for erase-and-insert edits.
- Never leave Shift, Control, or selection state active after injection.
- Do not include generated benchmark artifacts in commits.
- Version every distributable component as 0.1.8.
- Merge PR #2 only after its rebased checks pass.

---

### Task 1: Merge GitHub Actions dependency update

**Files:**
- Modify remotely: `.github/workflows/ci.yml`
- Modify remotely: `.github/workflows/release.yml`

**Interfaces:**
- Consumes: Dependabot PR #2 and current `main` CI.
- Produces: updated `main` with official action major versions.

- [ ] Rebase PR #2 onto current `main` with the GitHub update-branch API.
- [ ] Wait for Windows, Linux sanitizer, and security checks.
- [ ] Squash-merge PR #2 and delete the remote branch.
- [ ] Pull merged `main` locally and verify local/remote SHAs match.

### Task 2: Add the Backspace regression test

**Files:**
- Modify: `tests/windows/input_injection_test.cpp`

**Interfaces:**
- Consumes: `BuildSelectionReplacementSequence(const InputDecision&, std::span<INPUT>)`.
- Produces: a failing test requiring erase-only decisions to emit `VK_BACK` pairs without `VK_SHIFT` or `VK_LEFT`.

- [ ] Add `native_chromium_erase_only_replacement_uses_backspace_without_selection` with `backspace_count = 2` and no insert units.
- [ ] Run the focused native unit binary and confirm the new assertions fail because the current sequence emits Shift/Left.

### Task 3: Correct erase-only selection delivery

**Files:**
- Modify: `platform/windows/input/input_injection.cpp`

**Interfaces:**
- Consumes: `InputDecision::backspace_count` and `InputDecision::insert_units`.
- Produces: marked Backspace down/up pairs for erase-only decisions; unchanged select-and-insert events otherwise.

- [ ] Change required-event calculation so erase-only output needs `backspace_count * 2` events.
- [ ] Emit `VK_BACK` down/up pairs when there is no insertion payload.
- [ ] Keep the existing Shift+Left plus Unicode sequence for replacement edits.
- [ ] Run the focused test and confirm it passes.
- [ ] Run the complete native Debug and Release suites.

### Task 4: Version and package Keyina 0.1.8

**Files:**
- Modify: `Directory.Build.props`
- Modify: `CMakeLists.txt`
- Modify: `apps/host/Keyina.Host/UI/SettingsModels.cs`
- Modify: managed `packages.lock.json` files containing Keyina project dependency versions.

**Interfaces:**
- Consumes: shared version properties and release scripts.
- Produces: binaries, manifest, archive, and installer consistently reporting 0.1.8.

- [ ] Update shared/native/UI version values and lock-file project dependency versions to 0.1.8.
- [ ] Run managed Release build and all managed tests.
- [ ] Run Linux Clang ASan/UBSan verification.
- [ ] Ensure Inno Setup is available, installing it only if absent.
- [ ] Run `scripts/windows/build-release.ps1 -Version 0.1.8` without `-SkipInstaller`.
- [ ] Verify release manifest checksums and installer metadata.

### Task 5: Commit and push

**Files:**
- Commit all tracked files from Tasks 2–4 plus the design and plan documents.

**Interfaces:**
- Produces: reviewed commit on `main` and a CI run for the final tree.

- [ ] Run `git diff --check` and inspect the staged diff.
- [ ] Exclude `benchmark-result.json` and generated release directories.
- [ ] Commit with a focused message.
- [ ] Push `main` and confirm remote SHA.
- [ ] Confirm the new CI run starts and report installer artifact paths.
