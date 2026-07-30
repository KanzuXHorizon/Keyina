# Changelog

All notable user-visible changes to Keyina will be documented in this file.

The format is based on Keep a Changelog, and the project intends to use Semantic Versioning once stable releases begin.

## [Unreleased]

### Added

- Native C++20 Vietnamese Telex engine with reversible composition and Context Guard.
- Resident Windows keyboard-hook backend that does not require `Win + Space`.
- .NET 10 tray host with settings, snippets, diagnostics, and optional speech input.
- Configurable global shortcuts with safe capture, conflict detection, transactional rollback, and one-click reset.
- First-run onboarding, credential-free settings import/export, and per-application exclusions.
- Selection translation with DeepL primary routing, optional user-configured LibreTranslate fallback, preview, protected technical tokens, and one-shot undo.
- Reproducible self-contained x64 publishing, portable ZIP, per-user Inno Setup installer, checksums, release manifest, packaged self-tests, and fail-closed Authenticode signing hooks.
- Deterministic brand assets, screenshot gallery, tests, golden vectors, and benchmarks.
- Public contribution, security, support, issue, and pull-request workflows.

### Changed

- Telex composition now follows reversible buffer and repeated-key escape behavior for flexible key order without rewriting visible text at word boundaries.
- Speech dictation tolerates the short TSF target reconnect window caused by global hotkeys before reporting that no text field is active.
- Overview status cards now fill their grid cells, avoid horizontal overflow, and use clearer Vietnamese status copy.
- The keyboard-hook backend is now the default typing path.
- The Windows TSF backend is optional and disabled by default.
- Translation configuration is provider-neutral and stores DeepL and LibreTranslate credentials separately in Windows Credential Manager.
- Configuration reads retry short-lived file locks caused by an overlapping atomic save.
- Product and file versions now come from the shared `KeyinaVersion` release property.

### Security

- Ordinary typing remains offline.
- Speech and translation credentials are stored through Windows Credential Manager and excluded from portable exports.
- LibreTranslate endpoints reject redirects, userinfo, unsafe public HTTP, mixed public/private DNS answers, and private-address SSRF unless explicit local mode is enabled.
- Secure, elevated, injected, excluded, and unsupported input contexts use literal pass-through.

## Release status

No trusted signed public release has been published. The repository can now build and verify an unsigned local installer and portable release; a public release still requires a trusted code-signing identity and clean-VM release-candidate testing.
