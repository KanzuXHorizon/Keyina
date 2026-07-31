# Native Callback Fast-Path Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove unnecessary typing-context Win32 calls from physical key-up events while preserving all suppression, hotkey, safety, and compatibility behavior.

**Architecture:** Keep the C++20 native resident and existing hook/message-loop architecture. Route non-hotkey key-up events through the controller's context-free release path before `CaptureTypingContext`, expose a content-free context-capture counter, and make the live typing self-test enforce the new invariant.

**Tech Stack:** C++20, Win32 `WH_KEYBOARD_LL`, CMake, CTest, MSVC Debug/Release, existing native benchmark and self-test infrastructure.

## Global Constraints

- Preserve existing user-visible behavior and runtime profile compatibility.
- Preserve hotkey release commands and suppression before the optimized key-up path.
- Preserve controller suppression of a physical key-up after its key-down was consumed.
- Preserve fail-open handling for secure, password, excluded, elevated, unknown, and failed targets.
- Add no heap allocation, logging, file access, network access, formatting, or process launch to the callback.
- Do not rewrite the resident or engine in Rust without measured evidence and an isolated rollback-safe boundary.
- Work on the clean `main` checkout as explicitly authorized by the user and commit the verified slice.

---

### Task 1: Add a failing live invariant for context captures

**Files:**
- Modify: `platform/windows/input/include/keyina/windows/win32_input_runtime.h`
- Modify: `platform/windows/input/native_resident.cpp`

**Interfaces:**
- Produces: `std::uint64_t Win32InputRuntime::typing_context_capture_count() const noexcept`.
- The typing self-test compares that value with `raw.size()`, the number of generated physical key-down events.

- [ ] **Step 1: Add the self-test assertion before implementing the counter.**

Extend `RunTypingSelfTest` to store `typing_context_captures`, read `runtime.typing_context_capture_count()`, include it in failure JSON, and require:

```cpp
typing_context_captures <= raw.size()
```

for a successful run.

- [ ] **Step 2: Build to verify RED.**

Run:

```powershell
cmake --build --preset windows-msvc-debug
```

Expected: compilation fails because `typing_context_capture_count` is not defined.

### Task 2: Implement the key-up fast path

**Files:**
- Modify: `platform/windows/input/include/keyina/windows/win32_input_runtime.h`
- Modify: `platform/windows/input/win32_input_runtime.cpp`

**Interfaces:**
- Produces: `typing_context_capture_count_` incremented only at `CaptureTypingContext` entry.
- Consumes: `ResidentInputController::Process(const PhysicalKeyEvent&, const TypingContext&)`; its key-up branch does not inspect context.

- [ ] **Step 1: Add the content-free counter.**

Add a zero-initialized `std::uint64_t typing_context_capture_count_` member and a `noexcept` getter beside existing runtime counters. Increment the counter at the beginning of `CaptureTypingContext`.

- [ ] **Step 2: Add the context-free key-up path.**

Immediately after hotkey routing and suppression, add:

```cpp
if (!key_down) {
  const InputDecision release = controller_.Process(event, {});
  return release.suppress
      ? 1
      : CallNextHookEx(nullptr, code, message, data);
}
```

Do not update snippet UI, pointer registration, context counters, or injection on this branch.

- [ ] **Step 3: Build and run focused live tests to verify GREEN.**

Run:

```powershell
cmake --build --preset windows-msvc-debug
.\build\windows-msvc-debug\platform\windows\input\Debug\KeyinaInput.exe --typing-self-test
.\build\windows-msvc-debug\platform\windows\input\Debug\KeyinaInput.exe --clipboard-typing-self-test
```

Expected: both self-tests print their pass marker.

### Task 3: Verify correctness, resources, and Release performance

**Files:**
- Modify: `docs/benchmarks/2026-08-01-native-callback-fast-path.md`

**Interfaces:**
- Produces: a checked-in report containing exact commands, pass counts, resource snapshots, benchmark results, and the Rust decision.

- [ ] **Step 1: Run the complete native Debug suite.**

```powershell
ctest --preset windows-msvc-debug --output-on-failure
```

Expected: 10/10 tests pass.

- [ ] **Step 2: Build and test Release.**

```powershell
cmake --preset windows-msvc-release
cmake --build --preset windows-msvc-release
ctest --preset windows-msvc-release --output-on-failure
```

Expected: all Release CTest tests pass.

- [ ] **Step 3: Run resource probes.**

```powershell
.\build\windows-msvc-release\platform\windows\input\Release\KeyinaInput.exe --resource-self-test
.\build\windows-msvc-release\platform\windows\input\Release\KeyinaInput.exe --tray-resource-self-test
```

Expected: each JSON result contains `"budget_pass":true`, private working set and private memory at or below 10 MiB, and `thread_count_delta` equal to zero.

- [ ] **Step 4: Run the Release benchmark three times.**

```powershell
.\build\windows-msvc-release\benchmarks\Release\keyina_bench.exe
.\build\windows-msvc-release\benchmarks\Release\keyina_bench.exe
.\build\windows-msvc-release\benchmarks\Release\keyina_bench.exe
```

Expected: every case reports `budget_pass: true` and zero ordinary-path allocation regressions.

- [ ] **Step 5: Write the evidence report.**

Record the exact observed context-capture invariant, test totals, resource JSON values, and median p99 across the three benchmark runs. State explicitly that the change reduces context calls on key-up but does not claim a universal end-to-end keyboard latency percentile.

### Task 4: Inspect and commit the isolated slice

**Files:**
- Review all files changed by Tasks 1–3.

- [ ] **Step 1: Run final quality checks.**

```powershell
git diff --check
git status --short
git diff --stat
```

Expected: no whitespace errors and only the design, plan, runtime header/source, native self-test, and benchmark report are changed.

- [ ] **Step 2: Commit.**

```powershell
git add `
  docs/superpowers/specs/2026-08-01-native-callback-fast-path-design.md `
  docs/superpowers/plans/2026-08-01-native-callback-fast-path.md `
  docs/benchmarks/2026-08-01-native-callback-fast-path.md `
  platform/windows/input/include/keyina/windows/win32_input_runtime.h `
  platform/windows/input/win32_input_runtime.cpp `
  platform/windows/input/native_resident.cpp
git commit -m "perf(input): skip context capture on key release"
```

Expected: one focused commit with no unrelated files.
