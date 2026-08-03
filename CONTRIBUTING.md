# Contributing to Keyina

Thank you for helping build a fast, private, and dependable Vietnamese input method for Windows.

## Project values

Every contribution should preserve these invariants:

- Ordinary typing stays offline and deterministic.
- Password fields and other secure input contexts fail open to literal input.
- Injected keyboard events are never processed a second time.
- Speech remains optional and isolated from normal typing.
- Credentials never enter configuration files, logs, tests, screenshots, or the repository.
- Keyina remains a clean-room Apache-2.0 implementation. Do not copy source code, tables, tests, or implementation details from UniKey, EVKey, OpenKey, or other projects with incompatible licenses.

## Before opening an issue

Search existing issues first. For a typing bug, include:

- Windows version and architecture.
- Keyina commit or release version.
- Target application and version.
- Input method and relevant settings.
- Exact physical keystrokes, expected text, and actual text.
- Whether the issue reproduces in Notepad.

Do not include passwords, API keys, private documents, raw keystroke logs, or other sensitive content.

Security vulnerabilities must follow [SECURITY.md](SECURITY.md), not a public issue.

## Development setup

Required on Windows:

- Windows 10 version 2004 or newer, or Windows 11.
- Visual Studio 2022 with Desktop development with C++.
- CMake 3.25 or newer.
- .NET SDK specified by `global.json`.
- Python 3 for vector and benchmark validation scripts.

Configure and verify the native Debug build. The default registry excludes foreground/`SendInput` tests so normal local verification does not steal focus or alter the active keyboard/cursor session:

```powershell
cmake --preset windows-msvc-debug
cmake --build --preset windows-msvc-debug
ctest --preset windows-msvc-debug --output-on-failure
python tools/check_vectors.py
python tools/test_compare_benchmark.py
```

Build and test the Windows host:

```powershell
dotnet build Keyina.slnx -c Debug
dotnet run --project apps/host/Keyina.Host.Tests/Keyina.Host.Tests.csproj -c Debug --no-build
dotnet run --project apps/host/Keyina.Host/Keyina.Host.csproj -c Debug --no-build -- --self-test
dotnet run --project apps/host/Keyina.Host/Keyina.Host.csproj -c Debug --no-build -- --speech-self-test
dotnet run --project apps/host/Keyina.Host/Keyina.Host.csproj -c Debug --no-build -- --hotkey-self-test
```

Before submitting performance-sensitive changes, also run the Release build and benchmarks documented in [README.md](README.md). Desktop-interactive native tests require `-DKEYINA_ENABLE_INTERACTIVE_DESKTOP_TESTS=ON` and must run only on an idle disposable desktop or isolated CI runner.

## Change workflow

1. Create a focused branch from `main`.
2. Add or update the smallest test that proves the intended behavior.
3. Keep unrelated refactors out of the change.
4. Run the focused checks, then the full relevant test lane.
5. Update user-facing documentation when behavior, compatibility, or limitations change.
6. Open a pull request using the repository template.

Commit messages should be concise and use a conventional prefix when practical, for example:

```text
feat(engine): support flexible Telex vowels
fix(hook): reset composition after navigation
perf(core): reduce protected-token allocations
docs: document elevated-app limitation
```

## Pull request expectations

A pull request should explain:

- The user-visible problem.
- The smallest implemented solution.
- Tests and commands actually run.
- Compatibility, privacy, security, or performance impact.
- Known limitations or follow-up work.

UI changes should include matched screenshots when visuals materially change. Native typing changes should include exact keystroke vectors and regression coverage.

## License

By contributing, you agree that your contribution is licensed under the repository's [Apache License 2.0](LICENSE).
