# Keyina

Keyina is a privacy-first Vietnamese text input platform for Windows. It is not a visual remake of UniKey or EVKey. The project focuses on three measurable differentiators:

1. **Reversible composition** — every transformation can be undone without losing the original keystrokes.
2. **Context Guard** — deterministic protection for source code, URLs, email addresses, commands, identifiers, games, and English-heavy tokens.
3. **Evidence-driven compatibility** — diagnostics and a published application matrix replace undocumented compatibility workarounds.

## Product principles

- Local-first: the keystroke path never requires a network connection.
- No keystroke logging, no cloud telemetry by default, and no processing in password or secure-input fields.
- Clean-room implementation under Apache-2.0; no source code is copied from GPL input engines.
- The C++20 core is independent from Windows UI and TSF integration.
- Tests, fuzzing, benchmarks, and compatibility evidence are release gates.

## Planned architecture

```text
Physical key / TSF event
        |
        v
Keyina TSF adapter (C++ COM in-process server)
        |
        v
Keyina Core (C++20)
  - Telex/VNI parser
  - reversible composition journal
  - Vietnamese orthography
  - Context Guard
  - app-profile policy
        |
        +--> local diagnostics events without text payloads
        |
        v
Committed composition in the target application

Settings and Text Lens are separate Windows App SDK processes and are never on the keystroke hot path.
```

## Repository layout

```text
core/                    Deterministic, platform-independent input engine
platform/windows/tsf/    Windows Text Services Framework adapter
apps/settings/           WinUI 3 settings and diagnostics application
tests/                   Unit, contract, property, and integration tests
benchmarks/              Reproducible latency and allocation benchmarks
docs/                    Product design, plans, compatibility evidence
```

## Status

The repository is in the foundation and engine implementation phase. See the current design and implementation plan:

- `docs/superpowers/specs/2026-07-29-keyina-vietnamese-text-platform-design.md`
- `docs/superpowers/plans/2026-07-29-keyina-foundation-and-core.md`

No production-ready Windows input service is claimed until the TSF build, installation, application matrix, secure-field behavior, and release benchmarks pass on Windows.
