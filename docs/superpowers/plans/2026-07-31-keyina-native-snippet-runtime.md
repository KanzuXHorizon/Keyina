# Native Snippet Runtime Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make built-in and configured snippets activate from the native resident input path on an explicit delimiter without Telex transforming their raw triggers, including dynamic date/time variables and command snippets.

**Architecture:** The managed host publishes a bounded, versioned native snippet profile beside `runtime-input.bin`. The native resident reloads that profile outside the keyboard callback, tracks only viable raw trigger prefixes before Telex, expands text locally, and queues existing managed companion commands for command snippets. Injection remains marked as Keyina-generated so expansions are not recursively transformed.

**Tech Stack:** .NET 10/C# configuration codec and atomic storage, C++20 native resident input controller, Win32 low-level keyboard hook and `SendInput`, repository-native C# and C++ tests.

## Global Constraints

- Activation requires an explicit configured delimiter; Space is supported for every snippet.
- `PreserveDelimiter` remains configurable per snippet; built-in commands consume the delimiter and built-in date/time snippets preserve it.
- Built-ins and custom snippets share one matcher and duplicate triggers remain invalid.
- `${date}`, `${time}`, and `${datetime}` expand at activation time using local time.
- Secure/bypassed typing contexts, modifier shortcuts, and Keyina-injected input never activate snippets.
- Trigger text and expansion text are never logged.
- Profiles and decisions are bounded; invalid or stale snippet profiles fail closed without breaking ordinary Telex input.
- Preserve unrelated working-tree changes and stage only files changed by this feature.

---

### Task 1: Native snippet profile contract

**Files:**
- Create: `apps/host/Keyina.Host.Core/Configuration/RuntimeSnippetProfile.cs`
- Create: `apps/host/Keyina.Host/Configuration/RuntimeSnippetProfileStore.cs`
- Create: `apps/host/Keyina.Host.Tests/RuntimeSnippetProfileTests.cs`
- Modify: `apps/host/Keyina.Host/Runtime/KeyinaApplicationContext.cs`
- Create: `platform/windows/input/include/keyina/windows/runtime_snippet_profile.h`
- Create: `platform/windows/input/runtime_snippet_profile.cpp`
- Create: `tests/windows/runtime_snippet_profile_test.cpp`
- Modify: `tests/CMakeLists.txt`

- [ ] Add failing C# round-trip and validation tests for built-ins, custom snippets, commands, delimiters, UTF-8, variables, preserve-delimiter, malformed data, and size bounds.
- [ ] Run focused C# tests and confirm failure because the codec/store do not exist.
- [ ] Implement deterministic versioned encoding and atomic publication beside `runtime-input.bin`.
- [ ] Add failing C++ golden-vector decoder tests and confirm failure because the native decoder does not exist.
- [ ] Implement the bounded C++ decoder and run focused native tests.

### Task 2: Raw-prefix matcher before Telex

**Files:**
- Create: `platform/windows/input/include/keyina/windows/runtime_snippet_matcher.h`
- Create: `platform/windows/input/runtime_snippet_matcher.cpp`
- Create: `tests/windows/runtime_snippet_matcher_test.cpp`
- Modify: `tests/CMakeLists.txt`

- [ ] Add failing tests for viable prefixes, exact match plus Space/Tab/Enter, case policy, delimiter policy, custom Telex-sensitive triggers, preserve-delimiter, commands, variables, Backspace/reset, invalid prefix fallback, and injected/modifier bypass.
- [ ] Run focused native tests and confirm expected failures.
- [ ] Implement bounded prefix tracking and local variable expansion.
- [ ] Run focused tests until green.

### Task 3: Resident controller and Win32 command integration

**Files:**
- Modify: `platform/windows/input/include/keyina/windows/resident_input_controller.h`
- Modify: `platform/windows/input/resident_input_controller.cpp`
- Modify: `tests/windows/resident_input_controller_test.cpp`
- Modify: `platform/windows/input/include/keyina/windows/win32_input_runtime.h`
- Modify: `platform/windows/input/win32_input_runtime.cpp`
- Modify: `platform/windows/input/include/keyina/windows/runtime_hotkeys.h`
- Modify: `platform/windows/input/runtime_hotkeys.cpp`

- [ ] Add failing resident-controller tests proving `;kvi Space`, `;kvoice Space`, date/time expansion, custom trigger literals, delimiter retention, secure bypass, and no recursive processing.
- [ ] Add command result data to input decisions and map snippet commands onto existing managed companion arguments.
- [ ] Load/reload snippets with the runtime profile timer, queue command results after injection, and keep callbacks bounded.
- [ ] Run focused native tests and live resident self-tests.

### Task 4: Settings visibility and full verification

**Files:**
- Modify: `apps/host/Keyina.Host/UI/SettingsForm.cs`
- Modify: `apps/host/Keyina.Host.Tests/SettingsFormTests.cs`
- Modify: `README.md`

- [ ] Add failing UI tests for delimiter behavior and dynamic-variable examples.
- [ ] Update snippet copy to state `trigger + Space` and per-snippet delimiter retention.
- [ ] Run Debug/Release .NET builds, complete managed tests, native builds/tests, and live integration checks.
- [ ] Inspect final diff for unrelated changes, secrets, unbounded allocations in callbacks, and typed-text diagnostics.
- [ ] Stage only this feature and commit with a focused message.
