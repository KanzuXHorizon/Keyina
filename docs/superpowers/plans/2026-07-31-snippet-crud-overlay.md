# Snippet CRUD and Suggestion Overlay Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Hoàn thiện trang Gõ tắt với danh sách cuộn được, CRUD thân thiện cho custom snippet dùng trigger `;k...`, và overlay gợi ý snippet không lấy focus khi người dùng gõ `;k`.

**Architecture:** Dùng `SnippetConfiguration` hiện có làm nguồn dữ liệu duy nhất. Settings UI thao tác qua các action cập nhật toàn bộ mảng snippet, `KeyinaApplicationContext` xác thực rồi lưu atomically và publish runtime profile. Overlay gợi ý là WinForms topmost/no-activate, nhận chuỗi phím đã gõ từ observer dùng chung của `ModifierKeyboardHook`, chỉ hiển thị danh sách khớp và không tự chèn hoặc nuốt phím.

**Tech Stack:** .NET 10, WinForms, Keyina configuration/runtime profile, low-level keyboard hook hiện có.

## Global Constraints

- Trigger custom bắt buộc bắt đầu bằng `;k` và dài tối thiểu 3 ký tự.
- Built-in snippets chỉ đọc; custom snippets được thêm, sửa, nhân bản, xóa.
- Overlay không activate, không lấy focus, không chặn chuột, không hiện trong secure input hoặc ứng dụng bị loại trừ.
- Không thêm dependency mới.
- Không commit vì working tree đang chứa nhiều thay đổi chưa commit của người dùng.

---

### Task 1: Snippet validation and settings contract

**Files:**
- Modify: `apps/host/Keyina.Host.Core/Configuration/KeyinaConfiguration.cs`
- Modify: `apps/host/Keyina.Host/UI/SettingsModels.cs`
- Modify: `apps/host/Keyina.Host/Runtime/KeyinaApplicationContext.cs`
- Test: `apps/host/Keyina.Host.Tests/ConfigurationStoreTests.cs`
- Test: `apps/host/Keyina.Host.Tests/KeyinaApplicationContextTests.cs`

- [ ] Add failing tests for rejecting custom triggers outside `;k...` and for persisting a replacement snippet collection.
- [ ] Add a `SetSnippets` settings action and implement validation/save/runtime profile refresh in the application context.
- [ ] Run focused tests.

### Task 2: Scrollable snippet library and CRUD dialog

**Files:**
- Modify: `apps/host/Keyina.Host/UI/SettingsForm.cs`
- Create: `apps/host/Keyina.Host/UI/SnippetEditorDialog.cs`
- Test: `apps/host/Keyina.Host.Tests/SettingsFormTests.cs`
- Create: `apps/host/Keyina.Host.Tests/SnippetEditorDialogTests.cs`

- [ ] Add failing UI contract tests for `AutoScroll`, Add button, custom row actions, and dialog validation.
- [ ] Replace static custom-count-only rendering with built-ins plus snapshot custom snippets.
- [ ] Implement Add/Edit/Duplicate/Delete using a compact accessible editor dialog.
- [ ] Add search empty state and result count.
- [ ] Run focused UI tests and render the snippets screenshot.

### Task 3: Non-activating `;k` suggestion overlay

**Files:**
- Modify: `apps/host/Keyina.Host.Windows/Hotkeys/ModifierKeyboardHook.cs`
- Create: `apps/host/Keyina.Host.Core/Snippets/SnippetSuggestionSession.cs`
- Create: `apps/host/Keyina.Host/UI/SnippetSuggestionOverlayForm.cs`
- Modify: `apps/host/Keyina.Host/Runtime/KeyinaApplicationContext.cs`
- Test: `apps/host/Keyina.Host.Tests/SnippetSuggestionSessionTests.cs`
- Create: `apps/host/Keyina.Host.Tests/SnippetSuggestionOverlayFormTests.cs`

- [ ] Add failing tests for starting only at `;k`, prefix filtering, Backspace, Escape/boundary reset, and maximum visible results.
- [ ] Expose a non-consuming raw-key event from the existing modifier hook.
- [ ] Implement suggestion session over built-in and custom definitions.
- [ ] Implement topmost/no-activate/click-through overlay and connect it to runtime configuration updates.
- [ ] Run focused overlay and hook tests.

### Task 4: Verification and release readiness

**Files:**
- Modify only if verification finds a defect.

- [ ] Run all managed tests.
- [ ] Build the full solution in Release with zero warnings.
- [ ] Run speech, hotkey, and resource self-tests.
- [ ] Render and inspect the snippets settings screenshot.
- [ ] Run `git diff --check` and inspect final diff for unrelated changes or secrets.
