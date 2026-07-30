# Changelog

All notable user-visible changes to Keyina will be documented in this file.

The format is based on Keep a Changelog, and the project intends to use Semantic Versioning once stable releases begin.

## [Unreleased]

### Added

- Native C++20 Vietnamese Telex engine with reversible composition and Context Guard.
- Resident Windows keyboard-hook backend that does not require `Win + Space`.
- .NET 10 tray host with settings, hotkeys, snippets, diagnostics, and optional speech input.
- Deterministic brand assets, screenshot gallery, tests, golden vectors, and benchmarks.
- Public contribution, security, support, issue, and pull-request workflows.

### Changed

- The keyboard-hook backend is now the default typing path.
- The Windows TSF backend is optional and disabled by default.

### Security

- Ordinary typing remains offline.
- Speech credentials are stored through Windows Credential Manager.
- Secure, elevated, injected, and unsupported input contexts use literal pass-through.

## Release status

No stable public release has been published. The repository currently represents a source-first public preview.
