# Keyina Foundation and Core Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a deterministic, reversible, benchmarked C++20 Vietnamese Telex engine and the contracts required for a Windows TSF adapter.

**Architecture:** A standard-library-only `keyina_core` library consumes normalized key events and returns declarative text edits. Vietnamese transformation, composition history, Context Guard, and app-profile policy remain platform-independent; Windows-specific TSF and UI code depend on these interfaces rather than duplicating input rules.

**Tech Stack:** C++20, CMake 3.25+, CTest, repository-owned test harness, optional Clang sanitizers, Windows SDK/COM for the later TSF adapter.

## Global Constraints

- Apache-2.0 clean-room implementation; do not copy UniKey, EVKey, OpenKey, or other input-engine source code.
- No network, filesystem, registry, process, window, clipboard, or environment access from `keyina_core`.
- No raw typed text in diagnostics or persisted state.
- Literal input is the fallback for inconsistent state.
- Hot-path behavior is deterministic and does not invoke AI.
- Unicode output is precomposed Vietnamese where a precomposed scalar exists.
- Optimized benchmark budgets are those defined in the design specification.

---

### Task 1: Reproducible repository and test harness

**Files:**
- Create: `CMakeLists.txt`
- Create: `CMakePresets.json`
- Create: `cmake/KeyinaCompilerOptions.cmake`
- Create: `core/CMakeLists.txt`
- Create: `tests/CMakeLists.txt`
- Create: `tests/test_main.cpp`
- Create: `tests/test_support.h`
- Create: `.github/workflows/ci.yml`

**Interfaces:**
- Produces: CMake target `keyina_core`, test executable `keyina_tests`, CTest label `unit`, and preset names `windows-msvc-debug`, `windows-msvc-release`, `linux-clang-debug`, and `linux-clang-asan`.

- [ ] **Step 1: Write a smoke test that includes `<keyina/version.h>` and asserts `keyina::kVersionMajor == 0`.**
- [ ] **Step 2: Configure the test target and run the build; verify it fails because the header and core target do not exist.**
- [ ] **Step 3: Add the minimal root/core/test CMake files and `core/include/keyina/version.h`.**
- [ ] **Step 4: Configure, build, and run CTest; verify the smoke test passes.**
- [ ] **Step 5: Add warnings-as-errors, sanitizer options, deterministic output directories, and Windows/Linux CI jobs.**
- [ ] **Step 6: Re-run configure, build, and CTest with the available local compiler.**
- [ ] **Step 7: Commit as `build: establish reproducible native toolchain`.**

### Task 2: Unicode scalar and Vietnamese letter model

**Files:**
- Create: `core/include/keyina/vietnamese.h`
- Create: `core/src/vietnamese.cpp`
- Create: `tests/vietnamese_test.cpp`

**Interfaces:**
- Produces:
  - `enum class Tone { None, Acute, Grave, Hook, Tilde, Dot };`
  - `enum class VowelShape { Plain, Breve, Circumflex, Horn };`
  - `struct VietnameseLetter { char32_t base; VowelShape shape; Tone tone; bool uppercase; };`
  - `std::optional<VietnameseLetter> DecomposeVietnamese(char32_t);`
  - `std::optional<char32_t> ComposeVietnamese(const VietnameseLetter&);`
  - `bool IsVietnameseVowel(char32_t);`

- [ ] **Step 1: Add failing table-driven tests for all lower- and uppercase Vietnamese precomposed vowels plus `đ/Đ`.**
- [ ] **Step 2: Run only `vietnamese_test.cpp`; verify missing-symbol failures.**
- [ ] **Step 3: Implement explicit constexpr composition tables and binary or bounded linear lookup.**
- [ ] **Step 4: Run the focused tests and then all tests.**
- [ ] **Step 5: Add round-trip invariants: compose(decompose(x)) equals x for every supported scalar.**
- [ ] **Step 6: Commit as `feat(core): add Vietnamese Unicode model`.**

### Task 3: Context Guard with stable reason codes

**Files:**
- Create: `core/include/keyina/context_guard.h`
- Create: `core/src/context_guard.cpp`
- Create: `tests/context_guard_test.cpp`

**Interfaces:**
- Produces:
  - `enum class GuardReason { None, ModifierChord, Url, Email, FilePath, Identifier, VersionOrHash, ShellToken, ApplicationBypass };`
  - `struct GuardResult { bool transform; GuardReason reason; };`
  - `GuardResult ClassifyToken(std::u32string_view token, const GuardContext&);`

- [ ] **Step 1: Add failing examples for Vietnamese prose, `https://example.com`, `name@example.com`, `C:\\Temp`, `snake_case`, `camelCase`, `v2.3.1`, `--help`, and explicit app bypass.**
- [ ] **Step 2: Run the focused tests and verify the classifier is missing.**
- [ ] **Step 3: Implement the ordered deterministic rules from the design specification without regex or heap allocation.**
- [ ] **Step 4: Run focused and full tests.**
- [ ] **Step 5: Add precedence tests proving URL/email/path rules win over generic identifier classification.**
- [ ] **Step 6: Commit as `feat(core): add deterministic context guard`.**

### Task 4: Reversible Telex engine

**Files:**
- Create: `core/include/keyina/engine.h`
- Create: `core/src/engine.cpp`
- Create: `tests/engine_letter_test.cpp`
- Create: `tests/engine_tone_test.cpp`
- Create: `tests/engine_history_test.cpp`

**Interfaces:**
- Produces the `KeyKind`, `KeyEvent`, `TextEdit`, `EngineConfig`, `TonePlacement`, and `Engine` interfaces defined in the design specification.
- Consumes: Vietnamese composition helpers and Context Guard.

- [ ] **Step 1: Add failing tests for `aa→â`, `aw→ă`, `ee→ê`, `oo→ô`, `ow→ơ`, `uw→ư`, `dd→đ`, uppercase preservation, and literal boundaries.**
- [ ] **Step 2: Verify the tests fail because `Engine` is absent.**
- [ ] **Step 3: Implement raw-key capture, visible-token state, minimal suffix edits, and letter modifiers.**
- [ ] **Step 4: Run letter tests and all tests.**
- [ ] **Step 5: Add failing tone tests for `as→á`, `aaf→ầ`, `tieengs→tiếng`, `duowngf→đường`, tone replacement, and tone preservation while changing vowel shape.**
- [ ] **Step 6: Implement vowel-nucleus detection and configurable modern/traditional tone placement.**
- [ ] **Step 7: Run tone tests and all tests.**
- [ ] **Step 8: Add failing history tests proving backspace, reset, commit, and exact `RawKeys()` rollback.**
- [ ] **Step 9: Implement the bounded journal and history transitions.**
- [ ] **Step 10: Run all tests and commit as `feat(core): implement reversible Telex composition`.**

### Task 5: Golden vectors and invariant runner

**Files:**
- Create: `tests/data/telex_vectors.tsv`
- Create: `tests/golden_vectors_test.cpp`
- Create: `tests/invariant_test.cpp`
- Create: `tools/check_vectors.py`

**Interfaces:**
- Consumes: public `Engine` API.
- Produces: a documented TSV schema with raw sequence, expected output, expected rollback, and guard expectation.

- [ ] **Step 1: Add a parser test for comments, UTF-8 fields, and malformed row rejection.**
- [ ] **Step 2: Verify malformed data fails clearly.**
- [ ] **Step 3: Add at least 100 curated vectors spanning Vietnamese syllables, uppercase, punctuation, code, URL, email, path, and shell cases.**
- [ ] **Step 4: Run golden-vector tests and fix only production behavior that contradicts the approved specification.**
- [ ] **Step 5: Add deterministic generated sequences up to length six and assert edit-size, visible-state, rollback, and reset invariants.**
- [ ] **Step 6: Run the full suite repeatedly and commit as `test(core): add golden vectors and invariants`.**

### Task 6: Benchmark and regression budgets

**Files:**
- Create: `benchmarks/CMakeLists.txt`
- Create: `benchmarks/engine_benchmark.cpp`
- Create: `benchmarks/baseline.schema.json`
- Create: `tools/compare_benchmark.py`
- Create: `docs/benchmarks/README.md`

**Interfaces:**
- Produces: executable `keyina_bench`, JSON result format containing environment metadata and latency percentiles, and comparator exit code `1` for a regression greater than 20%.

- [ ] **Step 1: Add failing comparator tests for accepted, rejected, missing, and incompatible baselines.**
- [ ] **Step 2: Implement strict benchmark JSON parsing in the Python comparator.**
- [ ] **Step 3: Add benchmark cases for ASCII pass-through, letter modifier, tone update, guard-protected URL, and 64-code-point classification.**
- [ ] **Step 4: Build optimized binaries, run warm-up and measured iterations, and write the first machine-specific evidence file outside Git.**
- [ ] **Step 5: Verify no benchmark is used as a correctness test and document measurement limitations.**
- [ ] **Step 6: Commit as `perf(core): add reproducible latency benchmarks`.**

### Task 7: Windows TSF contract scaffold

**Files:**
- Create: `platform/windows/tsf/CMakeLists.txt`
- Create: `platform/windows/tsf/include/keyina/tsf/edit_translator.h`
- Create: `platform/windows/tsf/src/edit_translator.cpp`
- Create: `platform/windows/tsf/src/dll_entry.cpp`
- Create: `platform/windows/tsf/src/text_service.h`
- Create: `platform/windows/tsf/src/text_service.cpp`
- Create: `platform/windows/tsf/KeyinaTsf.def`
- Create: `tests/windows/edit_translator_test.cpp`
- Create: `scripts/windows/bootstrap-build-tools.ps1`
- Create: `scripts/windows/register-dev.ps1`
- Create: `scripts/windows/unregister-dev.ps1`

**Interfaces:**
- Consumes: `keyina::TextEdit` and `keyina::Engine`.
- Produces: a pure edit-translation function testable without registering TSF, then the COM DLL lifecycle and registration exports.

- [ ] **Step 1: Add a failing Windows-only test translating code-point erasure into validated UTF-16 composition edits.**
- [ ] **Step 2: Implement the pure UTF-16 translator and verify it independently.**
- [ ] **Step 3: Add minimal COM DLL exports and a text-service class whose key path delegates to `Engine`.**
- [ ] **Step 4: Add scripts that install documented Build Tools prerequisites and register/unregister only developer builds.**
- [ ] **Step 5: Configure and compile x64 on Windows; do not claim runtime compatibility yet.**
- [ ] **Step 6: Commit as `feat(windows): scaffold TSF input service`.**

### Task 8: Verification and release evidence

**Files:**
- Create: `docs/compatibility/matrix.md`
- Create: `docs/security/privacy.md`
- Create: `docs/release/checklist.md`
- Modify: `README.md`

**Interfaces:**
- Produces: release evidence tied to commit hash and exact commands.

- [ ] **Step 1: Run clean configure/build/test on every locally available lane.**
- [ ] **Step 2: Run sanitizer and optimized benchmark lanes.**
- [ ] **Step 3: Inspect the complete diff and scan for secrets, binary artifacts, raw diagnostic text, and accidental generated files.**
- [ ] **Step 4: Record exact pass/fail/blocked status for every release gate; blocked Windows runtime tests remain explicitly blocked.**
- [ ] **Step 5: Update README status without overstating readiness.**
- [ ] **Step 6: Commit as `docs: record foundation verification evidence`.**
