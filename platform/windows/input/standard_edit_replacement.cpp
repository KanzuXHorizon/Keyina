#include <keyina/windows/standard_edit_replacement.h>

#include <array>
#include <cstddef>

namespace keyina::windows {
namespace {

bool EqualsAsciiCaseInsensitive(
    std::wstring_view left,
    std::wstring_view right) noexcept {
  if (left.size() != right.size()) {
    return false;
  }
  for (std::size_t index = 0; index < left.size(); ++index) {
    wchar_t lhs = left[index];
    wchar_t rhs = right[index];
    if (lhs >= L'a' && lhs <= L'z') {
      lhs = static_cast<wchar_t>(lhs - (L'a' - L'A'));
    }
    if (rhs >= L'a' && rhs <= L'z') {
      rhs = static_cast<wchar_t>(rhs - (L'a' - L'A'));
    }
    if (lhs != rhs) {
      return false;
    }
  }
  return true;
}

bool ContainsAsciiCaseInsensitive(
    std::wstring_view value,
    std::wstring_view needle) noexcept {
  if (needle.size() > value.size()) {
    return false;
  }
  for (std::size_t offset = 0;
       offset + needle.size() <= value.size(); ++offset) {
    if (EqualsAsciiCaseInsensitive(
            value.substr(offset, needle.size()), needle)) {
      return true;
    }
  }
  return false;
}

bool SendBoundedEditMessage(
    HWND target,
    UINT message,
    WPARAM w_param,
    LPARAM l_param,
    DWORD timeout_milliseconds) noexcept {
  DWORD_PTR result = 0;
  SetLastError(ERROR_SUCCESS);
  return SendMessageTimeoutW(
             target,
             message,
             w_param,
             l_param,
             SMTO_ABORTIFHUNG | SMTO_BLOCK | SMTO_ERRORONEXIT,
             timeout_milliseconds,
             &result) != 0;
}

}  // namespace

EditableControlKind ClassifyEditableControlClass(
    std::wstring_view class_name) noexcept {
  if (EqualsAsciiCaseInsensitive(class_name, L"Edit") ||
      ContainsAsciiCaseInsensitive(class_name, L".EDIT.")) {
    return EditableControlKind::Win32Edit;
  }
  if (ContainsAsciiCaseInsensitive(class_name, L"RICHEDIT")) {
    return EditableControlKind::RichEdit;
  }
  return EditableControlKind::Unsupported;
}

bool IsFocusedWindowForProcess(
    HWND target,
    DWORD expected_process_id) noexcept {
  if (target == nullptr || expected_process_id == 0 ||
      IsWindow(target) == FALSE) {
    return false;
  }
  DWORD process_id = 0;
  if (GetWindowThreadProcessId(target, &process_id) == 0 ||
      process_id != expected_process_id) {
    return false;
  }
  if (process_id == GetCurrentProcessId() && GetFocus() == target) {
    return true;
  }
  GUITHREADINFO information{};
  information.cbSize = sizeof(information);
  return GetGUIThreadInfo(0, &information) != FALSE &&
      information.hwndFocus == target;
}

bool StandardEditTargetMayBeMutated(
    StandardEditReplacementResult result) noexcept {
  return result ==
          StandardEditReplacementResult::FocusChangedAfterSelection ||
      result == StandardEditReplacementResult::TargetHungAfterSelection ||
      result == StandardEditReplacementResult::ReplaceFailed;
}

StandardEditReplacementResult TryReplaceFocusedStandardEdit(
    HWND target,
    DWORD expected_process_id,
    std::uint16_t erase_codepoints,
    const std::wstring& replacement,
    DWORD timeout_milliseconds) noexcept {
  if (target == nullptr || expected_process_id == 0 ||
      replacement.empty() || timeout_milliseconds == 0 ||
      IsWindow(target) == FALSE) {
    return StandardEditReplacementResult::InvalidTarget;
  }
  if (!IsFocusedWindowForProcess(target, expected_process_id)) {
    return StandardEditReplacementResult::FocusChanged;
  }

  std::array<wchar_t, 128> class_name{};
  const int class_length = GetClassNameW(
      target,
      class_name.data(),
      static_cast<int>(class_name.size()));
  if (class_length <= 0) {
    return StandardEditReplacementResult::InvalidTarget;
  }
  const EditableControlKind kind = ClassifyEditableControlClass(
      std::wstring_view(
          class_name.data(),
          static_cast<std::size_t>(class_length)));
  if (kind != EditableControlKind::Win32Edit) {
    // RichEdit selection APIs above WM_USER carry caller pointers and are not
    // safe to send across process boundaries. It uses clipboard/keyboard
    // delivery instead of pretending to share the Win32 Edit contract.
    return StandardEditReplacementResult::UnsupportedControl;
  }

  DWORD selection_start = 0;
  DWORD selection_end = 0;
  if (!SendBoundedEditMessage(
          target,
          EM_GETSEL,
          reinterpret_cast<WPARAM>(&selection_start),
          reinterpret_cast<LPARAM>(&selection_end),
          timeout_milliseconds)) {
    return StandardEditReplacementResult::TargetHung;
  }
  if (selection_end < selection_start ||
      selection_start < erase_codepoints) {
    return StandardEditReplacementResult::InvalidSelection;
  }
  if (!IsFocusedWindowForProcess(target, expected_process_id)) {
    return StandardEditReplacementResult::FocusChanged;
  }

  const DWORD replacement_start =
      selection_start - static_cast<DWORD>(erase_codepoints);
  if (!SendBoundedEditMessage(
          target,
          EM_SETSEL,
          static_cast<WPARAM>(replacement_start),
          static_cast<LPARAM>(selection_end),
          timeout_milliseconds)) {
    return StandardEditReplacementResult::TargetHungAfterSelection;
  }
  if (!IsFocusedWindowForProcess(target, expected_process_id)) {
    return StandardEditReplacementResult::FocusChangedAfterSelection;
  }
  if (!SendBoundedEditMessage(
          target,
          EM_REPLACESEL,
          TRUE,
          reinterpret_cast<LPARAM>(replacement.c_str()),
          timeout_milliseconds)) {
    return StandardEditReplacementResult::ReplaceFailed;
  }
  return StandardEditReplacementResult::Replaced;
}

}  // namespace keyina::windows
