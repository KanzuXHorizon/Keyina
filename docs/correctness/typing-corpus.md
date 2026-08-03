# Keyina typing correctness corpus

Keyina treats typing correctness as checked-in synthetic evidence. A real defect is first reduced to a deterministic vector or event script, observed failing, then fixed at the earliest incorrect layer.

No corpus file may contain passwords, API keys, private messages, documents, clipboard contents, or captured user input.

## Word and token vectors

`tests/data/telex_vectors.tsv` contains four UTF-8, tab-separated columns:

```text
raw	expected	rollback	guard_reason
```

- `raw`: exact physical Unicode scalars supplied to `keyina::Engine`.
- `expected`: exact visible output after the complete raw sequence.
- `rollback`: exact active raw-key state. It must equal `raw` for the current corpus.
- `guard_reason`: expected `ContextGuard` classification.

Allowed guard reasons are:

```text
None
Url
Email
FilePath
Identifier
VersionOrHash
ShellToken
```

Rows beginning with `#` and blank rows are ignored. Raw sequences must be unique. UTF-8 must be canonical: overlong encodings, surrogate values, truncated sequences, and scalars above U+10FFFF are rejected.

The corpus covers Vietnamese vowel shapes and tones, onsets and codas, delayed Telex orders, uppercase, tone replacement, `z`, repeated-key escape, literal English, identifiers, shell tokens, paths, URLs, email addresses, hashes, versions, IPv4, and IPv6.

## Sentence and event scripts

`tests/data/typing_sequences.tsv` contains six UTF-8, tab-separated columns:

```text
name	placement	restore_invalid_word	script	expected	expected_active_raw
```

- `name`: unique stable diagnostic identifier.
- `placement`: `Modern` or `Traditional`.
- `restore_invalid_word`: `true` or `false`.
- `script`: physical characters and explicit control tokens.
- `expected`: exact external text after replay.
- `expected_active_raw`: exact raw state that remains active at the end; normally empty after a boundary.

Literal spaces, tabs, and newlines are forbidden in the script field because they hide the ownership boundary. Use explicit tokens instead.

### Script tokens

| Token | Meaning |
|---|---|
| `{SPACE}` | Commit the active composition, then pass through U+0020. |
| `{TAB}` | Commit the active composition, then pass through U+0009. |
| `{ENTER}` | Commit the active composition, then pass through U+000A. |
| `{BS}` | Internal core-engine rollback event used only by the deterministic oracle; shipped input backends do not map physical Backspace to this token. |
| `{RESET}` | Reset internal composition without editing external text. |
| `{B:XXXX}` | Commit boundary followed by the specified valid Unicode scalar. |
| `{L:XXXX}` | Reset the engine, then insert the specified valid scalar literally outside Telex. |
| `{{` | Literal `{`. |
| `}}` | Literal `}`. |

`XXXX` accepts one to six hexadecimal digits, excluding U+0000, surrogate code points, and values above U+10FFFF.

`{L:XXXX}` models characters that the resident routes outside the Telex engine, such as emoji, CJK, mathematical symbols, combining marks, and already composed Vietnamese. It must not be used to hide a Telex transformation defect.

## Invariants

Every event-script replay verifies:

- returned erase length never exceeds owned external text;
- active raw state never exceeds `keyina::kMaxActiveKeys`;
- active visible composition is an exact suffix of external text;
- Space, Tab, Enter, punctuation boundaries, and Reset leave no stale composition;
- final external text, raw keys, and visible text match exactly;
- diagnostics report the first differing Unicode scalar without logging arbitrary user content.

The generated mixed-language stream replays more than 2,000 physical events with no sleeps, desktop input, network access, or timing assumptions. The corpus includes repeated-`s` Latin words (`loss`, `lossless`, `classless`, and `assessment`) while retaining the short Telex escape contract `ass` → `as`.

## Core-engine Backspace reconstruction oracle

Every checked-in golden raw sequence within the active-key bound is tested in both modes:

```text
restore_invalid_word = false
restore_invalid_word = true
```

After each internal `{BS}` event, the existing engine state and visible text must equal a fresh engine replay of the remaining raw prefix. This checks the reusable core primitive independently from user-facing delivery behavior.

Physical Backspace in the native resident, managed fallback, and optional TSF path resets active composition and passes through to the target application. The target therefore deletes one visible character; the controller does not restore a previously committed composition or reinterpret Backspace as removal of a Telex modifier.

## Tone placement

The sentence corpus contains paired Modern and Traditional cases for the same physical sequences, including the open diphthongs represented by `hoaf`, `thuyr`, and `khoer`. Expected text is explicit; no locale-dependent normalization or spelling correction is performed.

## Adding a defect regression

1. Reduce the report to exact physical keys and expected Unicode text using synthetic content.
2. Choose the earliest affected layer: word vector, event script, controller test, or delivery self-test.
3. Add the smallest test and observe it fail for the intended reason.
4. Confirm the expected spelling and tone placement independently.
5. Fix the earliest incorrect implementation boundary.
6. Run focused tests and the default non-interactive native/managed suites. Enable `KEYINA_ENABLE_INTERACTIVE_DESKTOP_TESTS=ON` only on an idle disposable desktop or isolated CI runner before running foreground/`SendInput` probes.
7. Never widen a timeout, queue, or threshold to hide a correctness failure.

Real application compatibility is a separate release matrix. Passing this corpus proves deterministic engine/controller behavior, not universal compatibility with every third-party text stack.
