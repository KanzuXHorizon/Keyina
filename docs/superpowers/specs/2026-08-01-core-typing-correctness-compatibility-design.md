# Core Typing Correctness, Latency, Compatibility, and UX Design

## Goal

Make Keyina's default native typing path reliably correct for Vietnamese Telex, mixed Vietnamese/English content, long sentences, complex Unicode, burst typing, Backspace recovery, and real applications while keeping resident CPU, memory, handles, and background activity tightly bounded.

The product target is evidence-backed best-in-class behavior, not an unverifiable claim of universal perfection. Completion requires explicit correctness, latency, resource, compatibility, privacy, and regression gates. Any unsupported target or unresolved linguistic ambiguity must be reported rather than hidden behind a "100% compatible" label.

The selected direction is evidence-driven improvement of the existing C++20 engine and Win32 resident path. TSF remains optional and is not part of this slice.

## Current evidence

The native engine already performs ordinary transforms in roughly sub-microsecond to low-microsecond ranges with no steady-state allocation. End-to-end latency is dominated by synchronous Windows text delivery and target-application processing rather than Vietnamese composition itself.

Existing coverage includes representative Telex words, invalid-Latin restoration, delayed modifier orders, technical tokens, Backspace reconstruction, hook safety, and synthetic Chromium ordering. Remaining risk is concentrated in corpus breadth, long mixed text, complex Unicode, real target applications, delivery policy selection, and long-running stability.

## Non-goals

- Do not rewrite the engine in another language without profiler evidence.
- Do not make TSF the default backend.
- Do not add dictionary autocorrection, spelling guesses, cloud processing, or typed-content telemetry.
- Do not add a permanent worker thread, unbounded queue, polling loop, or background service.
- Do not claim universal application compatibility from synthetic targets alone.
- Do not trade correctness or fail-open safety for benchmark improvements.

## Architecture

The work is divided into five independently testable layers.

### 1. Vietnamese correctness oracle

A data-driven corpus defines physical Telex input, expected Unicode output, expected per-key edits, and expected composition state. The public `Engine::ProcessView` interface remains the authority.

Coverage includes:

- Every Vietnamese vowel shape and six tone states.
- Common two- and three-vowel nuclei: `iê`, `yê`, `uô`, `ươ`, `uyê`, `oai`, `uây`, and related forms.
- Onsets and codas including `qu`, `gi`, `ng`, `ngh`, `ch`, `nh`, `c`, `t`, `p`, `m`, and `n`.
- Modern and traditional tone placement pairs such as `hòa/hoà`, `thủy/thuỷ`, and `khỏe/khoẻ`.
- Correct, delayed, unusual, and repeated Telex orders.
- Tone replacement, tone clearing with `z`, repeated-key escape, mixed case, Shift, and Caps Lock.
- Backspace at every composition step followed by continued typing.
- Boundaries: Space, punctuation, Tab, Enter, arrows, mouse focus changes, and mode toggles.
- Invalid and ambiguous sequences that must remain literal rather than being autocorrected.

Every discovered defect first becomes a failing corpus entry. The smallest responsible engine or controller boundary is then corrected.

### 2. Mixed-language and technical-token oracle

A separate corpus verifies that English and technical content remains literal while Vietnamese portions transform correctly.

Required categories:

- English words containing Telex keys: `search`, `research`, `process`, `source`, `browser`, `powershell`, `windows`, and similar forms.
- camelCase, PascalCase, snake_case, kebab-case, identifiers, class names, package names, and command names.
- URLs, email addresses, domains, ports, IPv4, IPv6, UUIDs, hashes, semantic versions, and scientific notation.
- Windows, UNC, Linux, and URI paths.
- PowerShell, CMD, Bash, JSON, XML, HTML, Markdown, regex, and source-code fragments.
- Mixed Vietnamese and technical sentences where only natural-language Vietnamese segments transform.

The context guard remains bounded and allocation-free in the keyboard callback.

### 3. Long-text, Unicode, and burst harness

A deterministic harness feeds exact marked physical events and validates complete Unicode scalar output.

Workloads:

- Short phrases of 20–50 characters.
- Paragraphs of 300–500 characters.
- Multi-paragraph text exceeding 2,000 physical key events.
- Inter-key delays of 0 ms, 1 ms, 5 ms, and 10 ms.
- Rapid Backspace, correction, boundary, focus-change, and mode-toggle sequences.
- Emoji and supplementary-plane characters before, between, and after Vietnamese text.
- Precomposed NFC input, decomposed NFD input, combining marks, smart quotes, dashes, mathematical symbols, and non-Latin text.

Assertions:

- No lost, duplicated, reordered, or stale characters.
- Exact Unicode scalar sequence matches the oracle.
- Tone is placed on the correct vowel.
- Composition state is reset only at defined safety boundaries.
- Backspace reconstructs the expected raw and visible state.
- Invalid Unicode scalars or unsupported combining sequences fail open without corrupting surrounding text.

### 4. Delivery and compatibility policy

Engine cost and delivery cost are measured separately. Target classification selects the safest known mode:

- Standard minimal Backspace plus Unicode injection.
- Owned ordered text stream for compatible Chromium-style controls.
- Explicit clipboard compatibility only when enabled or required by a verified application profile.
- Bypass for secure, elevated, raw-input, remote, unsupported, or uncertain targets.

No asynchronous delivery change is accepted unless it proves exact ordering under burst input. Every delivery optimization must report event count, `SendInput` call count, median, p95, p99, maximum, failures, and exact-output status.

The real-application matrix covers, when installed and safely testable:

- Notepad and representative Win32/WinForms/WPF/WinUI controls.
- Edge and Chrome address bar, text input, textarea, and contenteditable.
- VS Code editor, search, and integrated terminal.
- Windows Terminal, PowerShell, and Command Prompt.
- Word and Excel rich text and cell editing.
- An Electron chat application.
- Password fields, elevated targets, excluded applications, focus switching, and mouse reset.
- Games or raw-input targets such as Delta Force, with fail-open behavior when direct support is unsafe.

Synthetic probes remain deterministic release gates; real applications produce explicit compatibility evidence rather than unconditional claims.

### 5. Diagnostics and UI/UX

Settings receives a practical compatibility and latency surface without increasing resident background cost.

The UI shows:

- Focused application identity and integrity relationship.
- Classified input path: Standard, Ordered Unicode, Clipboard compatibility, Bypass, or Unverified.
- Content-free timing stages: callback, context capture, controller, injection, and total.
- A safe `Kiểm tra trong ứng dụng hiện tại` workflow that uses a clearly identified test field or explicit user action and never records private text.
- Clear status labels: Tốt, Dùng chế độ tương thích, Không an toàn, or Chưa kiểm chứng.
- Per-application override: Auto, Unicode, Clipboard, or Bypass. TSF is not exposed as a recommended mode in this slice.
- Recovery guidance when the target is elevated, remote, secure, raw-input, or incompatible.

The UI preserves the existing Fluent native design, keyboard navigation, narrow layout, High Contrast behavior, and accessibility names. Expensive reports and compatibility pages remain lazy-created.

## Performance and resource budgets

Correctness is the primary gate. Performance changes are accepted only after repeated Release measurements on the same machine, with warm-up, sample count, machine identity, build commit, compiler mode, and tolerance recorded.

Target budgets:

- Native engine operations remain allocation-free after construction.
- Orthography lookup is bounded, deterministic, and free of heap allocation, locale APIs, regex, dictionaries, locks, and I/O in the callback path.
- Ordinary controller processing remains in the existing low-microsecond range.
- Engine composition median, p95, and p99 must not regress by more than 5% or 0.25 microseconds, whichever tolerance is larger, against the accepted baseline on the same machine.
- Callback pass-through p95 and p99 must not regress by more than 5% or 0.5 microseconds, whichever tolerance is larger.
- Transform callback and injection median/p95 must improve or remain within the benchmark tolerance; maximum latency is investigated separately and cannot be hidden by good averages.
- Resident private memory remains below the existing 10 MiB gate.
- No new steady-state resident thread.
- No handle, GDI, USER object, or process growth during endurance runs.
- Idle CPU remains effectively zero outside normal Windows scheduling noise.
- Diagnostics are disabled by default and add no typed-content storage.

A faster result is rejected if it introduces character loss, wrong tone placement, stale composition, focus leakage, secure-input transformation, or application regressions.

## Privacy and failure handling

- Diagnostics record key categories, stage timings, process identity, control class, and stable error codes only.
- Raw typed text, selected text, transcript text, clipboard content, credentials, and document content are never logged.
- Unsupported or uncertain contexts fail open and reset composition.
- Injection failure clears owned composition state and lets subsequent physical input proceed normally.
- Elevated-integrity mismatch is reported clearly rather than bypassed unsafely.
- Compatibility overrides are bounded, validated executable names rather than paths or wildcards.

## Verification strategy

1. Add corpus tests first and observe failures before production changes.
2. Run focused engine, controller, injection, and context-guard tests after every correction.
3. Run the long-text and burst harness in Debug and repeated Release runs.
4. Compare callback and transform benchmark reports against the recorded baseline.
5. Run managed and native full suites, resource probes, profile reload, publish checks, and `git diff --check`.
6. Publish the actual Windows bundle and run deterministic self-tests from the published directory.
7. Execute the real-application matrix and record unsupported cases honestly.
8. Run UI detector, responsive screenshots, keyboard navigation, High Contrast, and accessibility checks for any Settings changes.

## Delivery order

### Slice 1 — Corpus and oracle expansion

Build the Vietnamese, English/technical, long-sentence, complex-Unicode, and Backspace corpus. Fix only reproduced correctness defects.

### Slice 2 — Burst and delivery profiling

Add deterministic long-stream and burst probes, split delivery metrics by mode, and identify the dominant remaining Win32 or target cost.

### Slice 3 — Evidence-driven delivery optimization

Apply at most one measured delivery optimization at a time. Retain it only when output correctness and compatibility remain intact.

### Slice 4 — Compatibility center and per-application policy

Add practical diagnostics, safe application classification, explicit override modes, and clear fail-open guidance.

### Slice 5 — Real-application release matrix and polish

Validate representative browsers, editors, terminals, Office, password/elevated targets, and games; finish DPI, High Contrast, accessibility, wording, and resource hardening.

## Acceptance criteria

- Every supported Telex primitive, tone state, vowel shape, mixed-case policy, tone replacement, tone clearing, repeated-key escape, and documented quick-Telex option has an exact regression test.
- Every runtime nucleus rule has generated modern/traditional tone-target coverage, valid-coda coverage, checked-coda tone coverage, and recoverable-prefix coverage.
- Expanded correctness corpus passes with exact Unicode and state assertions.
- Mixed English and technical content remains literal where required.
- Long paragraphs and zero-delay burst runs have zero loss, duplication, or reordering.
- Tone placement and Backspace reconstruction are verified across modern/traditional settings and mixed case.
- No new keyboard-callback allocation, locale-dependent branch, resident thread, polling loop, lock contention, file/network access, or unbounded queue.
- Focused Release benchmarks remain inside the explicit median/p95/p99 regression tolerances and publish machine-readable comparison evidence.
- Published resident stays within memory, CPU, thread, handle, and object budgets.
- Real-application results are recorded with explicit supported, fallback, bypass, or unresolved status.
- Settings remains responsive, accessible, and visually consistent.
- No claim of "complete", "best-in-class", or "100% compatible" is made unless all release gates have fresh evidence; unresolved applications and linguistic edge cases remain explicitly listed.
- No commit or push is performed without explicit authorization.
