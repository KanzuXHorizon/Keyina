# Keyina Vietnamese Text Platform Design

**Date:** 2026-07-29  
**Status:** Approved for implementation  
**License:** Apache-2.0, clean-room implementation

## 1. Product definition

Keyina is a Windows-native Vietnamese text input platform. Its first release must type Vietnamese as reliably as established tools while providing capabilities they do not expose as first-class, testable contracts.

Keyina is not differentiated by a new settings window. It is differentiated by three system behaviors:

1. **Reversible composition journal:** every accepted key records a bounded transformation entry containing the original token, resulting token, edit span, and reason. Undo and fallback restore exact user intent instead of approximating it with blind backspaces.
2. **Context Guard:** a deterministic classifier prevents Vietnamese transformations in code, URLs, email addresses, file paths, shell commands, identifiers, version strings, and English-heavy tokens. It runs locally and does not use an AI model in the keystroke path.
3. **Evidence-driven compatibility:** the Windows adapter emits content-free diagnostic events and is released only with a compatibility matrix covering representative native, Chromium, Electron, Office, terminal, elevated, remote, and game scenarios.

## 2. Competitive baseline

The baseline is intentionally demanding:

- UniKey already supports Telex, VNI, VIQR, many encodings, Simple Telex, per-application on/off state, x86, x64, and ARM64.
- EVKey already offers keyboard-hook and IME modes, application profiles, macros, browser workarounds, and game-oriented behavior.
- OpenKey already includes modern orthography options, spelling checks, key restoration, Quick Telex, and browser compatibility options.

Keyina therefore must not claim superiority from features that already exist. A Keyina feature is considered differentiating only when it has an observable contract, a test suite, and a benchmark or compatibility artifact.

## 3. Users and primary jobs

### 3.1 Developer

The user writes Vietnamese prose, source code, terminal commands, URLs, issue identifiers, and English technical terms in the same session. Keyina must stop accidental transformations without requiring frequent manual mode changes.

### 3.2 Knowledge worker or student

The user needs reliable Vietnamese typing, macros, selected-text transformations, and consistent behavior across browsers, Office applications, messaging software, and PDF tools.

### 3.3 Gamer and power user

The user needs per-application bypass, low latency, elevated-process compatibility, and a visible explanation when an application cannot be supported safely.

## 4. Scope

### 4.1 Release 0.1 foundation

Release 0.1 delivers a buildable and benchmarked platform-independent C++20 engine with:

- Telex letter modifiers: `aa`, `aw`, `ee`, `oo`, `ow`, `uw`, and `dd`.
- Telex tone keys: `s`, `f`, `r`, `x`, and `j`.
- Deterministic tone relocation when the vowel nucleus changes.
- Exact rollback to raw keystrokes.
- Backspace through composed text without corrupting engine state.
- Modern and traditional tone-placement policy as explicit configuration.
- Context Guard decisions with reason codes.
- Application-profile policy objects independent of Windows process APIs.
- Zero network dependencies and zero runtime package dependencies in the core.
- Unit, golden-vector, invariant, fuzz-entry, and benchmark executables.

### 4.2 Release 0.2 Windows input service

Release 0.2 adds:

- C++ COM in-process Text Services Framework service.
- x64 build and registration tooling.
- TSF composition and edit-session handling.
- Secure-input and unsupported-scope bypass.
- Foreground application profile resolution.
- Content-free diagnostic ring buffer.
- Installation, uninstallation, and repair commands.
- Compatibility evidence for Notepad, Word, Excel, Edge, Chrome, Firefox, VS Code, Windows Terminal, PowerShell, Discord, and one elevated process.

### 4.3 Release 0.3 product surface

Release 0.3 adds:

- WinUI 3 settings application using the current stable Windows App SDK channel.
- Text Lens for explicit selected-text transformations.
- Snippets with application scopes and secure-field exclusion.
- Diagnostic Center with exportable redacted evidence.
- x64 and ARM64 packaging.

### 4.4 Explicit non-goals before Release 1.0

- Cloud account, cloud synchronization, or mandatory sign-in.
- AI correction on the keystroke hot path.
- Translation, generative rewriting, or grammar generation.
- Legacy Vietnamese encodings beyond Unicode NFC.
- Kernel driver, input injection driver, or anti-cheat bypass.
- Silent operation in password, PIN, payment, or secure desktop fields.
- Automatic collection of typed text for diagnostics.

## 5. Architecture

### 5.1 Core boundary

`keyina_core` is a C++20 static library with no Windows headers. It receives normalized key events and returns declarative edits.

```cpp
namespace keyina {

enum class KeyKind { Character, Backspace, CommitBoundary, Reset };

enum class GuardDecision { Transform, PassThrough, ResetAndPassThrough };

struct KeyEvent {
  KeyKind kind;
  char32_t character;
  bool shift;
  bool control;
  bool alt;
};

struct TextEdit {
  std::size_t erase_codepoints;
  std::u32string insert;
  bool consumed;
  bool commit_before;
};

class Engine {
 public:
  explicit Engine(EngineConfig config = {});
  [[nodiscard]] TextEdit Process(const KeyEvent& event);
  void Reset() noexcept;
  [[nodiscard]] std::u32string_view VisibleText() const noexcept;
  [[nodiscard]] std::u32string_view RawKeys() const noexcept;
};

}  // namespace keyina
```

The platform adapter owns text-store integration. It must not duplicate Vietnamese rules.

### 5.2 Composition journal

The engine stores at most 64 key records for the active token. Each record contains:

- raw key before transformation;
- visible token before transformation;
- visible token after transformation;
- edit returned to the adapter;
- transformation reason;
- guard decision.

A token boundary commits and clears the journal. When the 64-key ownership limit is reached, the next edit sets `commit_before`, starts a fresh owned token, and never rewrites text from the committed token. The journal is memory-only and never written to disk.

### 5.3 Context Guard

Context Guard is an ordered deterministic ruleset. Earlier rules have higher priority:

1. Control or Alt chord: pass through and reset composition unless the adapter identifies a supported input command.
2. URL or URI markers: pass through after `://`, `www.`, or a recognized scheme prefix.
3. Email marker: pass through tokens containing `@` with valid local-part characters.
4. File path: pass through drive prefixes, UNC prefixes, and slash-separated technical tokens.
5. Source identifier: pass through camelCase, PascalCase, snake_case, namespace separators, and tokens containing digits mixed with ASCII letters.
6. Shell and version token: pass through option prefixes, environment syntax, semantic versions, hashes, and common operators.
7. Explicit application profile bypass: pass through.
8. Otherwise: transform as Vietnamese.

Every non-transform result includes a stable reason code. The classifier never inspects text outside the active token in Release 0.1.

### 5.4 Windows TSF adapter

The Windows adapter is a COM in-process text service registered through TSF. It translates key events to the core, requests TSF edit sessions, updates composition ranges, and applies returned declarative edits. It does not synthesize a sequence of global backspaces when a TSF composition is available.

The adapter must:

- refuse work when the input scope is password, PIN, numeric password, or another secure scope;
- reset state when document manager, context, focus, or composition ownership changes;
- avoid blocking calls, file I/O, logging I/O, network access, and settings IPC on the key event path;
- fail open to literal input when state is inconsistent;
- expose structured diagnostic counters without text payloads.

### 5.5 Settings and diagnostics process

The UI process is separate from the TSF DLL. Settings are persisted atomically in a versioned JSON document. The TSF adapter reads an immutable memory-mapped snapshot or equivalent local read-only snapshot prepared outside the hot path.

Diagnostic events may contain:

- timestamp;
- executable hash or normalized executable name according to user setting;
- adapter state transition;
- error code;
- edit-session duration bucket;
- fallback reason;
- input scope category.

Diagnostic events must not contain raw keys, composed text, clipboard text, window titles, document paths, URLs, or email addresses.

## 6. Vietnamese composition rules

### 6.1 Canonical representation

The engine stores visible text as Unicode scalar values in precomposed form when a precomposed Vietnamese character exists. Public UTF-8 helpers reject invalid UTF-8 rather than replacing bytes silently.

### 6.2 Letter modifiers

The following transformations are case-preserving:

- `a` + `a` → `â`
- `a` + `w` → `ă`
- `e` + `e` → `ê`
- `o` + `o` → `ô`
- `o` + `w` → `ơ`
- `u` + `w` → `ư`
- `d` + `d` → `đ`

A modifier applied to a vowel already carrying a tone preserves the tone and relocates it according to the configured placement policy.

### 6.3 Tone keys

- `s` → acute
- `f` → grave
- `r` → hook above
- `x` → tilde
- `j` → dot below

A tone key affects the current Vietnamese vowel nucleus. Repeating the same tone key removes that tone and emits the repeated key literally only when the configured fallback policy requires literal recovery. Applying a different tone replaces the prior tone.

### 6.4 Boundaries

Whitespace, punctuation, navigation, focus changes, and explicit commit events end the active token. Punctuation is returned as literal input after the token is committed.

### 6.5 Rollback

The engine exposes exact raw keys for the active token. A platform fallback may replace the visible token with raw keys in one declarative edit. No heuristic reconstruction is permitted.

## 7. Performance and resource budgets

Release benchmarks run in optimized builds after a warm-up phase.

- Typical ASCII pass-through: median ≤ 2 microseconds, p99 ≤ 10 microseconds.
- Vietnamese transformation: median ≤ 5 microseconds, p99 ≤ 25 microseconds.
- Context Guard classification for a token up to 64 code points: p99 ≤ 20 microseconds.
- No dynamic allocation for a pass-through key after engine construction.
- Active-token state ≤ 16 KiB.
- Core library has no background thread.
- TSF key-event handler performs no network or disk I/O.

The benchmark harness records CPU model, operating system, compiler, build type, iteration count, median, p95, p99, and maximum. A regression greater than 20% against the checked-in baseline blocks release unless the baseline change is reviewed with evidence.

## 8. Reliability and safety invariants

- Literal input is the fallback for every inconsistent state.
- Engine operations are deterministic for the same configuration and key sequence.
- `Reset()` is noexcept and leaves no visible pending state.
- Backspace never deletes more Unicode code points than the adapter reports as owned composition.
- The core does not access process, window, clipboard, filesystem, registry, network, or environment APIs.
- The adapter never runs in secure desktop contexts intentionally.
- Configuration parsing rejects unknown schema versions and invalid enum values.
- No plaintext typed content is persisted or emitted to diagnostics.

## 9. Test strategy

### 9.1 Unit tests

Each mapping, tone replacement, backspace transition, commit boundary, case behavior, and guard reason receives a focused test.

### 9.2 Golden vectors

A checked-in UTF-8 data file maps raw key sequences to expected Vietnamese output and expected raw rollback. It includes common syllables, uppercase forms, punctuation, repeated modifiers, ambiguous English tokens, code identifiers, URLs, email addresses, paths, and shell commands.

### 9.3 Invariant tests

For every generated key sequence up to a bounded length:

- returned erase count is never larger than current visible token length;
- applying each edit reproduces `VisibleText()`;
- rollback reproduces `RawKeys()`;
- reset clears both views;
- the engine does not throw for valid Unicode scalar input.

### 9.4 Fuzzing

LibFuzzer entry points cover UTF-8 decoding and engine key sequences. Sanitizer CI runs AddressSanitizer and UndefinedBehaviorSanitizer on Linux or Windows clang builds.

### 9.5 Windows integration tests

A small TSF-aware test host validates registration, activation, composition updates, focus transitions, secure scope bypass, and teardown. Application compatibility tests remain explicit because third-party applications cannot be fully simulated by unit tests.

## 10. Release gates

A release is blocked unless all applicable gates pass:

- clean configure and build from documented prerequisites;
- unit and invariant tests pass;
- sanitizer lane passes;
- benchmark budgets pass;
- Windows TSF registration and uninstall are reversible;
- secure input bypass is verified;
- compatibility matrix is current for the release commit;
- generated installer is signed for public distribution;
- final diff contains no key material, credentials, raw diagnostic text, or unreviewed binary artifact.

## 11. Technology decisions

- Core: C++20, CMake, standard library only initially.
- Testing: small repository-owned test harness for zero dependency bootstrap; migration to Catch2 is allowed only if it materially improves property/fuzz integration.
- Windows input: TSF and COM in C++.
- Settings: C# and WinUI 3 on the current stable Windows App SDK release, isolated from the hot path.
- Packaging: unpackaged developer build first; signed MSIX or signed bootstrap installer after compatibility validation.
- CI: Windows x64 build/test and a sanitizer-capable native lane; ARM64 compilation added before Release 0.3.

## 12. Research references

- UniKey official overview and release notes: https://www.unikey.org/en/
- EVKey official feature overview: https://evkeyvn.com/
- OpenKey official feature overview: https://open-key.org/
- Microsoft TSF overview: https://learn.microsoft.com/windows/win32/tsf/text-services-framework
- Microsoft TSF architecture: https://learn.microsoft.com/windows/win32/tsf/architecture
- Windows App SDK downloads: https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads
