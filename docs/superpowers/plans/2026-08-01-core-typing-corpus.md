# Core Typing Corpus and Oracle Expansion Implementation Plan

> **Current behavior note (2026-08-03):** This plan's Backspace reconstruction scope is an internal core-engine oracle. Shipped input paths no longer invoke that primitive for physical Backspace; they reset state and pass the key through.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expand Keyina's deterministic native correctness coverage for Vietnamese Telex, mixed English/technical content, long sentences, complex Unicode boundaries, and Backspace reconstruction, then fix only defects reproduced by the new corpus.

**Architecture:** Keep the existing C++20 `keyina::Engine`, `ContextGuard`, and bounded 64-key composition model. Add a strict data-driven event-script corpus beside the existing golden vectors, replay it through public engine interfaces, and assert exact external text plus engine state after every event. This slice does not change Windows delivery, TSF, Settings UI, resident threads, or application policy.

**Tech Stack:** C++20, CMake/CTest, existing `KEYINA_TEST` runner, UTF-8 TSV test data, native Release benchmark executable.

## Global Constraints

- Correctness takes precedence over benchmark improvements.
- TSF remains optional and is not modified or promoted.
- Do not add dictionary autocorrection, spelling guesses, cloud processing, or typed-content telemetry.
- Do not add a worker thread, polling loop, timer, queue, dependency, or steady-state allocation to the keyboard callback.
- Unsupported or ambiguous content must fail open without corrupting surrounding text.
- Preserve the existing 64-physical-key active-composition bound.
- Every production behavior change must first have a focused failing regression.
- Do not commit or push without explicit user authorization.

---

### Task 1: Strict event-script corpus loader

**Files:**
- Create: `tests/typing_corpus_test.cpp`
- Create: `tests/data/typing_sequences.tsv`
- Modify: `tests/CMakeLists.txt`

**Interfaces:**
- Consumes: `keyina::Engine`, `keyina::EngineConfig`, `keyina::KeyEvent`, `keyina::TonePlacement`, and `KEYINA_TEST_DATA_DIR`.
- Produces: `CorpusCase`, `CorpusEvent`, `ParseCorpusLine(std::string_view)`, `DecodeUtf8Strict(std::string_view)`, `DecodeEventScript(std::u32string_view)`, and `ReplayCorpusCase(const CorpusCase&)` inside the test translation unit.

- [ ] **Step 1: Add one valid and four malformed corpus rows**

Create `tests/data/typing_sequences.tsv` with the header and initial row:

```text
# name	placement	restore_invalid_word	script	expected	expected_active_raw
simple_sentence	Modern	true	xin chaof{SPACE}thees giowis{SPACE}	xin chào thế giới 	
```

The script grammar is exact:

- Ordinary Unicode scalar: `KeyKind::Character`.
- `{SPACE}`: `CommitBoundary` with `U' '`, then append the physical space when the edit is not consumed.
- `{TAB}`: `CommitBoundary` with `U'\t'`, then append tab when not consumed.
- `{ENTER}`: `CommitBoundary` with `U'\n'`, then append newline when not consumed.
- `{BS}`: `KeyKind::Backspace`.
- `{RESET}`: `KeyKind::Reset`; no external text is removed.
- `{{` and `}}`: literal braces.
- Unknown, unterminated, or empty control tokens are parse errors.

- [ ] **Step 2: Write failing parser and replay tests**

Add these tests to `tests/typing_corpus_test.cpp`:

```cpp
KEYINA_TEST(typing_corpus_rejects_malformed_rows_and_scripts) {
  KEYINA_EXPECT_TRUE(Throws([] { ParseCorpusLine("missing-columns"); }));
  KEYINA_EXPECT_TRUE(Throws([] {
    ParseCorpusLine("bad-placement\tFuture\ttrue\ta\ta\ta");
  }));
  KEYINA_EXPECT_TRUE(Throws([] {
    DecodeEventScript(U"abc{UNKNOWN}");
  }));
  KEYINA_EXPECT_TRUE(Throws([] {
    DecodeEventScript(U"abc{SPACE");
  }));
}

KEYINA_TEST(checked_in_typing_corpus_replays_exact_text_and_state) {
  const auto cases = LoadCorpusCases(
      std::string{KEYINA_TEST_DATA_DIR} + "/typing_sequences.tsv");
  KEYINA_EXPECT_TRUE(cases.size() >= 1);
  for (const auto& test : cases) {
    ReplayCorpusCase(test);
  }
}
```

`ReplayCorpusCase` must apply every returned `TextEdit`, assert `erase_codepoints <= external.size()`, assert `external` ends with `engine.VisibleText()` while composition is active, and finally compare exact `external`, `RawKeys()`, and `VisibleText()` with the row expectations.

- [ ] **Step 3: Register the test source and verify RED**

Add `typing_corpus_test.cpp` to `keyina_tests` in `tests/CMakeLists.txt`.

Run:

```powershell
cmake --build --preset windows-msvc-debug --config Debug
build/windows-msvc-debug/tests/Debug/keyina_tests.exe
```

Expected: compilation or tests fail because the loader/replay functions are not implemented.

- [ ] **Step 4: Implement the minimal strict parser and replay engine**

Implement:

```cpp
struct CorpusEvent {
  keyina::KeyEvent event;
  std::optional<char32_t> literal_if_unconsumed;
};

struct CorpusCase {
  std::string name;
  keyina::TonePlacement placement;
  bool restore_invalid_word;
  std::u32string script;
  std::u32string expected;
  std::u32string expected_active_raw;
};
```

Use strict UTF-8 validation equivalent to `golden_vectors_test.cpp`: reject overlong encodings, surrogates, truncation, and values above U+10FFFF. Parse exactly six tab-separated fields; reject duplicate case names, empty names, invalid booleans, and unknown placement values.

- [ ] **Step 5: Run the focused test and verify GREEN**

Run:

```powershell
cmake --build --preset windows-msvc-debug --config Debug
build/windows-msvc-debug/tests/Debug/keyina_tests.exe
```

Expected: the initial corpus row and malformed-input tests pass.

---

### Task 2: Expand word-level Vietnamese and technical golden vectors

**Files:**
- Modify: `tests/data/telex_vectors.tsv`
- Modify: `tests/golden_vectors_test.cpp`

**Interfaces:**
- Consumes: existing four-column format `raw`, `expected`, `rollback`, `guard_reason`.
- Produces: at least 220 checked-in word/token vectors with exact output, rollback, and guard classification.

- [ ] **Step 1: Raise the minimum corpus count before adding data**

Change the final assertion in `checked_in_golden_vectors_match_engine_and_rollback` from:

```cpp
KEYINA_EXPECT_TRUE(vector_count >= 100);
```

to:

```cpp
KEYINA_EXPECT_TRUE(vector_count >= 220);
```

- [ ] **Step 2: Run the native unit test and verify RED**

Run:

```powershell
cmake --build --preset windows-msvc-debug --config Debug
build/windows-msvc-debug/tests/Debug/keyina_tests.exe
```

Expected: `checked_in_golden_vectors_match_engine_and_rollback` fails because the existing file contains fewer than 220 vectors.

- [ ] **Step 3: Add Vietnamese nucleus, onset, coda, placement, and correction vectors**

Append explicit vectors covering all six tones for `â`, `ă`, `ê`, `ô`, `ơ`, and `ư`; representative `iê`, `yê`, `uô`, `ươ`, `uyê`, `oai`, and `uây` words; `qu` and `gi`; checked codas `c`, `t`, `p`, `ch`; nasal codas `m`, `n`, `ng`, `nh`; mixed case; delayed modifiers; tone replacement; `z`; repeated-key escape; and ambiguous sequences that must remain literal.

Required rows include at minimum:

```text
chieecs	chiếc	chieecs	None
thuyeenf	thuyền	thuyeenf	None
quoocs	quốc	quoocs	None
giaf	già	giaf	None
luoon	luôn	luoon	None
muowij	muội	muowij	None
khuyeens	khuyến	khuyeens	None
ngoais	ngoái	ngoais	None
khuaays	khuấy	khuaays	None
baacs	bác	baacs	None
mawtj	mặt	mawtj	None
hieepj	hiệp	hieepj	None
saachs	sách	saachs	None
taam	tâm	taam	None
baanf	bần	baanf	None
lawngj	lặng	lawngj	None
nhanh	nhanh	nhanh	None
asf	à	asf	None
aasz	â	aasz	None
ass	as	ass	None
```

Expected spelling must be reviewed against Vietnamese orthography; intentionally invalid examples must be marked and remain literal rather than silently corrected.

- [ ] **Step 4: Add English and technical-token vectors**

Add explicit rows for camelCase, PascalCase, snake_case, kebab-case, package names, PowerShell switches, JSON/XML fragments without tabs, IPv4, IPv6, ports, UUIDs, hashes, semantic versions, scientific notation, Windows/UNC/Linux paths, and URLs with query strings.

Required rows include at minimum:

```text
restoreInvalidWord	restoreInvalidWord	restoreInvalidWord	Identifier
RuntimeInputProfile	RuntimeInputProfile	RuntimeInputProfile	Identifier
keyina_host	keyina_host	keyina_host	Identifier
keyina-input	keyina-input	keyina-input	Identifier
Microsoft.PowerShell	Microsoft.PowerShell	Microsoft.PowerShell	Identifier
--NoProfile	--NoProfile	--NoProfile	ShellToken
192.168.1.10	192.168.1.10	192.168.1.10	VersionOrHash
2001:db8::1	2001:db8::1	2001:db8::1	VersionOrHash
550e8400-e29b-41d4-a716-446655440000	550e8400-e29b-41d4-a716-446655440000	550e8400-e29b-41d4-a716-446655440000	VersionOrHash
1.25e-6	1.25e-6	1.25e-6	VersionOrHash
C:\\Users\\Kanzu\\Keyina	C:\\Users\\Kanzu\\Keyina	C:\\Users\\Kanzu\\Keyina	FilePath
\\\\server\\share\\file.txt	\\\\server\\share\\file.txt	\\\\server\\share\\file.txt	FilePath
/usr/local/bin/keyina	/usr/local/bin/keyina	/usr/local/bin/keyina	FilePath
https://example.com?q=research&lang=vi	https://example.com?q=research&lang=vi	https://example.com?q=research&lang=vi	Url
```

- [ ] **Step 5: Run focused tests and classify every failure**

Run:

```powershell
cmake --build --preset windows-msvc-debug --config Debug
build/windows-msvc-debug/tests/Debug/keyina_tests.exe
```

For each failure, determine whether the expected vector is wrong, `ContextGuard` classification is incomplete, or the engine transforms incorrectly. Do not change production code until a single failing vector has a reviewed expected output.

---

### Task 3: Long sentences, mixed content, boundaries, and complex Unicode

**Files:**
- Modify: `tests/data/typing_sequences.tsv`
- Modify: `tests/typing_corpus_test.cpp`

**Interfaces:**
- Consumes: the Task 1 corpus loader and event grammar.
- Produces: deterministic phrase, paragraph, multi-paragraph, technical-sentence, Unicode-boundary, and reset cases.

- [ ] **Step 1: Add sentence rows that initially expose unsupported expectations**

Add rows for:

1. A 20–50 character Vietnamese phrase.
2. A 300–500 character paragraph.
3. A mixed Vietnamese/English technical sentence containing `research`, `powershell`, a Windows path, an email address, and a URL.
4. A multi-paragraph script exceeding 2,000 physical character events, represented by a named repetition field in the test code rather than a 2,000-character duplicated TSV row.
5. Modern/traditional tone-placement pairs for `hoaf`, `thuyr`, and `khoer` inside sentences.
6. Space, comma, period, colon, semicolon, parentheses, smart quotes, em dash, Tab, and Enter boundaries.
7. Emoji, supplementary-plane characters, mathematical symbols, CJK text, and precomposed Vietnamese separated by `{RESET}` boundaries.
8. NFD combining-mark input separated by `{RESET}`; the surrounding ASCII/Vietnamese output must remain intact even when the combining sequence itself is passed through literally.

- [ ] **Step 2: Add exact per-event invariants**

After every corpus event, assert:

```cpp
KEYINA_EXPECT_TRUE(engine.RawKeys().size() <= keyina::kMaxActiveKeys);
KEYINA_EXPECT_TRUE(edit.erase_codepoints <= external.size());
```

When `engine.RawKeys()` is non-empty, assert that `external` ends with `engine.VisibleText()`. After `{SPACE}`, `{TAB}`, `{ENTER}`, or `{RESET}`, assert `RawKeys()` and `VisibleText()` are empty.

- [ ] **Step 3: Add a generated long-stream case**

Define a checked-in phrase pair:

```cpp
constexpr std::u32string_view kLongRawPhrase =
    U"tooi ddang research Keyina vaaf kieemr tra powershell{SPACE}";
constexpr std::u32string_view kLongExpectedPhrase =
    U"tôi đang research Keyina và kiểm tra powershell ";
```

Replay it at least 40 times through the same event decoder, producing more than 2,000 physical events, and compare the exact concatenated output. This is deterministic and contains no sleep or desktop input.

- [ ] **Step 4: Run the corpus and verify failures are real correctness gaps**

Run:

```powershell
cmake --build --preset windows-msvc-debug --config Debug
build/windows-msvc-debug/tests/Debug/keyina_tests.exe
```

Expected: all parser and invariant tests run; any mismatch names the corpus case and first differing Unicode scalar index.

- [ ] **Step 5: Correct corpus expectations that model the wrong layer**

Characters normally bypassed by the resident—emoji, CJK, combining marks, and precomposed external text—must be represented as `{RESET}` followed by literal external insertion in the corpus harness. Do not force them through Telex transformation merely to make a test convenient.

---

### Task 4: Exhaustive Backspace and continued-typing reconstruction

**Files:**
- Modify: `tests/typing_corpus_test.cpp`
- Modify: `tests/engine_history_test.cpp`

**Interfaces:**
- Consumes: word vectors from `telex_vectors.tsv`, `Engine::RawKeys()`, `Engine::VisibleText()`, and `Engine::Process(KeyKind::Backspace)`.
- Produces: replay-equivalence tests for every raw prefix and focused continued-typing regressions.

- [ ] **Step 1: Write a failing replay-equivalence test**

For every transformable vector whose raw sequence is at most `kMaxActiveKeys`:

1. Type the complete raw sequence.
2. Backspace one physical key.
3. In a fresh engine with the same config, replay the raw prefix excluding that key.
4. Assert exact equality of external text, `RawKeys()`, and `VisibleText()`.
5. Repeat until the composition is empty.

Add a minimum assertion that at least 150 vectors participate.

- [ ] **Step 2: Run and verify RED for any incorrect reconstruction**

Run:

```powershell
cmake --build --preset windows-msvc-debug --config Debug
build/windows-msvc-debug/tests/Debug/keyina_tests.exe
```

Expected: if an existing edge case reconstructs incorrectly, the failure reports its raw vector and remaining prefix. If the test passes immediately, retain it as broad regression coverage and continue with Step 3.

- [ ] **Step 3: Add focused correction scripts**

Add exact tests for:

- Type `tieengs`, Backspace tone, retype `f`, expect `tiềng` according to raw-key semantics.
- Type `nguyenx`, Backspace tone, type `e`, expect `nguyên`.
- Type `truowcs`, Backspace across `s`, `c`, and `w`, then retype modifiers in another valid order and expect `trước`.
- Type a Latin token such as `powershell`, Backspace repeatedly, and verify every prefix remains literal.
- Cross a boundary, press Backspace through the resident-controller recovery path only in its existing controller tests; the engine itself must not claim text committed before the boundary.

- [ ] **Step 4: Implement only the smallest reproduced production fix**

If a failure is in `core/src/engine.cpp`, change the earliest incorrect composition step while preserving `DifferenceView` minimal edits, bounded buffers, and allocation-free `ProcessView`. If a failure is controller ownership rather than engine state, add the regression to `tests/windows/resident_input_controller_test.cpp` and fix only that boundary.

- [ ] **Step 5: Run focused and full native Debug tests**

Run:

```powershell
cmake --build --preset windows-msvc-debug --config Debug
ctest --test-dir build/windows-msvc-debug -C Debug --output-on-failure
```

Expected: all native Debug tests pass.

---

### Task 5: Corpus-quality gates and diagnostic failure messages

**Files:**
- Modify: `tests/golden_vectors_test.cpp`
- Modify: `tests/typing_corpus_test.cpp`
- Create: `docs/correctness/typing-corpus.md`

**Interfaces:**
- Produces: duplicate detection, category counts, first-difference diagnostics, and documented corpus format.

- [ ] **Step 1: Add duplicate and coverage checks**

Reject duplicate raw+configuration rows. Track and assert minimum coverage:

```text
word/token vectors                 >= 220
sentence/event cases               >= 20
Backspace replay vectors           >= 150
technical guard vectors            >= 30
modern/traditional paired cases    >= 3
long-stream physical events        > 2000
```

- [ ] **Step 2: Add first-difference diagnostics**

When expected and actual text differ, report case name, scalar index, expected scalar as `U+XXXX`, actual scalar as `U+XXXX` or `<end>`, raw length, and event index. Do not print credentials or user data; all reported strings are checked-in synthetic corpus names and scalars.

- [ ] **Step 3: Document the exact formats**

Create `docs/correctness/typing-corpus.md` documenting:

- `telex_vectors.tsv` columns and allowed `GuardReason` values.
- `typing_sequences.tsv` six columns and event-script grammar.
- How boundaries, resets, Unicode pass-through, modern/traditional placement, and Backspace are modeled.
- The rule that every real defect becomes a failing synthetic vector before a production change.
- Privacy: only checked-in synthetic data is allowed.

- [ ] **Step 4: Run tests and inspect coverage output**

Run:

```powershell
cmake --build --preset windows-msvc-release --config Release
build/windows-msvc-release/tests/Release/keyina_tests.exe
```

Expected: coverage gates pass and failures, if any, are actionable without raw private text.

---

### Task 6: Performance, resource, and full-release verification

**Files:**
- Modify only if evidence requires: `core/src/engine.cpp`
- Modify: `docs/benchmarks/2026-08-01-core-typing-corpus-results.md`
- Review all changed corpus, test, core, and documentation files.

**Interfaces:**
- Consumes: `keyina_bench.exe`, native callback self-tests, native CTest, managed tests, publish script, and resident resource probes.
- Produces: before/after correctness and performance evidence without machine-independent marketing claims.

- [ ] **Step 1: Capture native engine benchmark evidence**

Run three Release executions:

```powershell
build/windows-msvc-release/benchmarks/Release/keyina_bench.exe
```

Record median/p95/p99 for `ascii_pass_through`, `letter_modifier`, `tone_update`, complete-word, delayed-modifier, Backspace, invalid-boundary, and context-guard cases. Engine operations must remain allocation-free.

- [ ] **Step 2: Run callback and transform self-tests**

Run from the Release build or published directory:

```powershell
KeyinaInput.exe --callback-latency-self-test
KeyinaInput.exe --transform-callback-latency-self-test
KeyinaInput.exe --typing-self-test
```

Require exact-output pass, zero failed injections, and no material p95 regression from the recorded machine baseline. This slice is not required to improve injection latency because it does not modify delivery.

- [ ] **Step 3: Run full managed and native verification**

```powershell
cmake --build --preset windows-msvc-release --config Release
ctest --test-dir build/windows-msvc-release -C Release --output-on-failure
dotnet build Keyina.slnx -c Release
dotnet run --project apps/host/Keyina.Host.Tests/Keyina.Host.Tests.csproj -c Release
git diff --check
```

Expected: zero warnings/errors and every native/managed test passes.

- [ ] **Step 4: Publish and run resource probes**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/windows/publish.ps1
artifacts/publish/win-x64/KeyinaInput.exe --resource-self-test
artifacts/publish/win-x64/KeyinaInput.exe --tray-resource-self-test
```

Require resident private memory below the existing 10 MiB gate, zero steady-state thread growth, no input contamination, and no handle/process growth.

- [ ] **Step 5: Write the results report**

Create `docs/benchmarks/2026-08-01-core-typing-corpus-results.md` containing exact vector counts, test counts, reproduced defects and fixes, three benchmark runs, callback evidence, resource evidence, unsupported areas, and the explicit statement that real application compatibility remains Slice 5.

- [ ] **Step 6: Synchronize verified targeted edits to `F:\Keyina`**

If implementation was performed in an isolated worktree, apply only the reviewed corpus/core/test/docs changes to the actual checkout. Preserve existing Settings, overlay, benchmark, and publish WIP. Repeat focused corpus tests, full Release verification, publish, and exactly-one-resident checks on `F:\Keyina`.

- [ ] **Step 7: Do not commit or push**

Leave all verified changes uncommitted unless the user explicitly authorizes a commit or remote update.
