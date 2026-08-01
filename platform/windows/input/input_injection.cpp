#include <keyina/windows/input_injection.h>

#include <cstddef>

namespace keyina::windows {

TextDeliveryMode ChooseTextDeliveryMode(
    bool clipboard_compatibility_enabled,
    bool chromium_target) noexcept {
  if (clipboard_compatibility_enabled) {
    return TextDeliveryMode::Clipboard;
  }
  return chromium_target
             ? TextDeliveryMode::SelectionReplacement
             : TextDeliveryMode::Keyboard;
}

bool ShouldOwnTextStream(
    bool vietnamese_enabled,
    bool bypass_typing,
    bool clipboard_delivery,
    bool selection_replacement_target) noexcept {
  return vietnamese_enabled && !bypass_typing && !clipboard_delivery &&
      selection_replacement_target;
}

bool BuildLiteralInputDecision(
    char32_t character,
    InputDecision& decision) noexcept {
  if (character == U'\0' || character > 0x10FFFF ||
      (character >= 0xD800 && character <= 0xDFFF)) {
    return false;
  }

  InputDecision candidate{};
  candidate.suppress = true;
  if (character <= 0xFFFF) {
    candidate.insert[0] = static_cast<wchar_t>(character);
    candidate.insert_units = 1;
  } else {
    const char32_t adjusted = character - 0x10000;
    candidate.insert[0] =
        static_cast<wchar_t>(0xD800 + (adjusted >> 10));
    candidate.insert[1] =
        static_cast<wchar_t>(0xDC00 + (adjusted & 0x3FF));
    candidate.insert_units = 2;
  }
  decision = candidate;
  return true;
}

std::size_t BuildKeyboardInputSequence(
    const InputDecision& decision,
    std::span<INPUT> destination) noexcept {
  if (decision.insert_units > decision.insert.size()) {
    return 0;
  }
  const std::size_t required =
      (static_cast<std::size_t>(decision.backspace_count) * 2) +
      (static_cast<std::size_t>(decision.insert_units) * 2);
  if (required > destination.size()) {
    return 0;
  }

  std::size_t count = 0;
  auto append = [&](WORD virtual_key, WORD scan_code,
                    DWORD flags) noexcept {
    INPUT input{};
    input.type = INPUT_KEYBOARD;
    input.ki.wVk = virtual_key;
    input.ki.wScan = scan_code;
    input.ki.dwFlags = flags;
    input.ki.dwExtraInfo = kKeyinaInjectionMarker;
    destination[count++] = input;
  };

  for (std::uint16_t index = 0;
       index < decision.backspace_count; ++index) {
    append(VK_BACK, 0, 0);
    append(VK_BACK, 0, KEYEVENTF_KEYUP);
  }
  for (std::uint16_t index = 0; index < decision.insert_units; ++index) {
    const WORD unit = static_cast<WORD>(decision.insert[index]);
    append(0, unit, KEYEVENTF_UNICODE);
    append(0, unit, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP);
  }
  return count;
}

std::size_t BuildClipboardPasteSequence(
    const InputDecision& decision,
    std::span<INPUT> destination) noexcept {
  if (decision.insert_units == 0 && decision.extended_insert.empty()) {
    return 0;
  }
  const std::size_t required =
      (decision.backspace_count == 0 ? 0 : 2) +
      (static_cast<std::size_t>(decision.backspace_count) * 2) + 4;
  if (required > destination.size()) {
    return 0;
  }

  std::size_t count = 0;
  auto append = [&](WORD virtual_key, DWORD flags) noexcept {
    INPUT input{};
    input.type = INPUT_KEYBOARD;
    input.ki.wVk = virtual_key;
    input.ki.dwFlags = flags;
    input.ki.dwExtraInfo = kKeyinaInjectionMarker;
    destination[count++] = input;
  };
  if (decision.backspace_count != 0) {
    append(VK_SHIFT, 0);
    for (std::uint16_t index = 0;
         index < decision.backspace_count; ++index) {
      append(VK_LEFT, 0);
      append(VK_LEFT, KEYEVENTF_KEYUP);
    }
    append(VK_SHIFT, KEYEVENTF_KEYUP);
  }
  append(VK_CONTROL, 0);
  append(static_cast<WORD>('V'), 0);
  append(static_cast<WORD>('V'), KEYEVENTF_KEYUP);
  append(VK_CONTROL, KEYEVENTF_KEYUP);
  return count;
}

std::size_t BuildSelectionReplacementSequence(
    const InputDecision& decision,
    std::span<INPUT> destination) noexcept {
  if (decision.insert_units > decision.insert.size()) {
    return 0;
  }
  const std::size_t required =
      (decision.backspace_count == 0 ? 0 : 2) +
      (static_cast<std::size_t>(decision.backspace_count) * 2) +
      (static_cast<std::size_t>(decision.insert_units) * 2);
  if (required > destination.size()) {
    return 0;
  }

  std::size_t count = 0;
  auto append = [&](WORD virtual_key, WORD scan_code, DWORD flags) noexcept {
    INPUT input{};
    input.type = INPUT_KEYBOARD;
    input.ki.wVk = virtual_key;
    input.ki.wScan = scan_code;
    input.ki.dwFlags = flags;
    input.ki.dwExtraInfo = kKeyinaInjectionMarker;
    destination[count++] = input;
  };

  if (decision.backspace_count != 0) {
    append(VK_SHIFT, 0, 0);
    for (std::uint16_t index = 0; index < decision.backspace_count; ++index) {
      append(VK_LEFT, 0, 0);
      append(VK_LEFT, 0, KEYEVENTF_KEYUP);
    }
    append(VK_SHIFT, 0, KEYEVENTF_KEYUP);
  }
  for (std::uint16_t index = 0; index < decision.insert_units; ++index) {
    const WORD unit = static_cast<WORD>(decision.insert[index]);
    append(0, unit, KEYEVENTF_UNICODE);
    append(0, unit, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP);
  }
  return count;
}

bool ShouldRestoreClipboard(
    DWORD owned_sequence,
    DWORD current_sequence) noexcept {
  return owned_sequence != 0 && owned_sequence == current_sequence;
}

bool RequiresSelectionReplacementForWindowClass(
    std::wstring_view class_name) noexcept {
  return class_name.starts_with(L"Chrome_");
}

}  // namespace keyina::windows
