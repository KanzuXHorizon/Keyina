#pragma once

#include <windows.h>

#include <cstdint>
#include <string>
#include <string_view>

namespace keyina::windows {

enum class EditableControlKind : std::uint8_t {
  Unsupported = 0,
  Win32Edit,
  RichEdit,
};

enum class StandardEditReplacementResult : std::uint8_t {
  Replaced = 0,
  InvalidTarget,
  UnsupportedControl,
  FocusChanged,
  TargetHung,
  InvalidSelection,
  FocusChangedAfterSelection,
  TargetHungAfterSelection,
  ReplaceFailed,
};

[[nodiscard]] EditableControlKind ClassifyEditableControlClass(
    std::wstring_view class_name) noexcept;

[[nodiscard]] bool IsFocusedWindowForProcess(
    HWND target,
    DWORD expected_process_id) noexcept;

[[nodiscard]] bool StandardEditTargetMayBeMutated(
    StandardEditReplacementResult result) noexcept;

[[nodiscard]] StandardEditReplacementResult TryReplaceFocusedStandardEdit(
    HWND target,
    DWORD expected_process_id,
    std::uint16_t erase_codepoints,
    const std::wstring& replacement,
    DWORD timeout_milliseconds = 10) noexcept;

}  // namespace keyina::windows
