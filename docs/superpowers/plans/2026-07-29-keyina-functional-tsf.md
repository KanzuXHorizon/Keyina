# Keyina Functional TSF Input Slice Plan

> **For agentic workers:** Execute task-by-task with test-first development. Do not register or activate the global profile automatically.

**Goal:** Turn the safe pass-through TSF scaffold into a functioning Vietnamese input service whose key handling and text edits are exercised through a real in-process TSF context.

**Architecture:** A pure Windows key router maps virtual-key state into normalized core events. `TextService` consumes those events, requests synchronous TSF edit sessions, owns one composition range, translates code-point edits to UTF-16, and falls back to literal/pass-through behavior when any precondition fails. A repository-owned `ITextStoreACP` host verifies the real COM/TSF interaction without installing Keyina globally.

**Tech Stack:** C++20, Windows TSF/COM, CMake, MSVC, CTest.

## Safety invariants

- Secure-mode activation never advises or handles keys.
- Ctrl, Alt, Windows-key chords, dead keys, unsupported layouts, and invalid Unicode pass through.
- `OnTestKeyDown` never mutates application text.
- A key is reported eaten only after a synchronous edit session succeeds.
- Any edit-session failure resets internal engine state and leaves subsequent keys pass-through.
- Erasure is limited to the owned composition range.
- Focus loss, external composition termination, and deactivation clear all owned state.
- Global registration is not part of automated tests.

### Task 1: Pure key routing

**Files:**
- Create: `platform/windows/tsf/include/keyina/tsf/key_router.h`
- Create: `platform/windows/tsf/src/key_router.cpp`
- Create: `tests/windows/key_router_test.cpp`

- [x] Add failing tests for ASCII letter case, Backspace, whitespace boundaries, punctuation boundaries, modifier chords, unsupported keys, and active-composition dependence.
- [x] Implement a deterministic allocation-free router independent from `GetKeyboardState`.
- [x] Verify focused and full Windows tests.
- [x] Commit as `feat(windows): add deterministic TSF key routing`.

### Task 2: Functional edit sessions and composition ownership

**Files:**
- Modify: `platform/windows/tsf/src/text_service.h`
- Modify: `platform/windows/tsf/src/text_service.cpp`
- Create: `platform/windows/tsf/src/key_edit_session.h`
- Create: `platform/windows/tsf/src/key_edit_session.cpp`
- Modify: `platform/windows/tsf/CMakeLists.txt`

- [x] Add a compile-time RED by requiring `ITfCompositionSink` and the edit-session class from `TextService`.
- [x] Implement synchronous `ITfContext::RequestEditSession` handling.
- [x] Start one TSF composition at the caret, apply only validated UTF-16 edits, and keep selection at composition end.
- [x] Commit/reset composition on boundaries, backspace-to-empty, `commit_before`, focus loss, external termination, and deactivation.
- [x] Keep all unsupported paths pass-through and verify DLL smoke tests.
- [x] Commit as `feat(windows): apply engine edits through TSF compositions`.

### Task 3: Real local TSF integration host

**Files:**
- Create: `tests/windows/test_text_store.h`
- Create: `tests/windows/test_text_store.cpp`
- Create: `tests/windows/tsf_integration_test.cpp`
- Modify: `tests/CMakeLists.txt`

- [ ] Implement the minimum correct `ITextStoreACP` contract required by `ITfDocumentMgr::CreateContext`.
- [ ] Activate a real `ITfThreadMgr`, create a document/context, instantiate Keyina through its class factory, and activate it without global registration.
- [ ] Type `tieengs`, `dduowngf`, Backspace, a boundary, and a protected technical token through `ITfKeyEventSink`.
- [ ] Assert resulting UTF-16 text, selection, composition cleanup, secure-mode pass-through, and DLL unloadability.
- [ ] Run the integration test repeatedly in Debug and Release.
- [ ] Commit as `test(windows): verify functional TSF input locally`.

### Task 4: Sanitizer and reliability lanes

**Files:**
- Modify: `cmake/KeyinaCompilerOptions.cmake`
- Modify: `CMakePresets.json`
- Modify: `.github/workflows/ci.yml`

- [ ] Add an MSVC AddressSanitizer preset and verify toolchain support.
- [ ] Run all supported local sanitizer tests; record unsupported lanes explicitly.
- [ ] Keep Linux Clang ASan/UBSan CI for cross-platform core coverage.
- [ ] Commit as `build: add Windows sanitizer verification`.

### Task 5: Functional-slice evidence

**Files:**
- Modify: `platform/windows/tsf/README.md`
- Create: `docs/compatibility/local-tsf-host.md`
- Modify: `README.md`

- [ ] Record exact Debug, Release, integration, sanitizer, and benchmark commands and results.
- [ ] Re-run vector validation and benchmark comparator tests.
- [ ] Inspect source/diff for registry side effects, raw text diagnostics, generated files, and secrets.
- [ ] State global registration and third-party application testing as unverified until an elevated manual session is run.
- [ ] Commit as `docs: record functional TSF evidence`.
