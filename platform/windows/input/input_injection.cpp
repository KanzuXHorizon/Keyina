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

std::size_t BuildLiteralUnicodeInputSequence(
    char32_t character,
    std::span<INPUT> destination) noexcept {
  if (character == U'\0' || character > 0x10FFFF ||
      (character >= 0xD800 && character <= 0xDFFF)) {
    return 0;
  }

  std::array<WORD, 2> units{};
  std::size_t unit_count = 1;
  if (character <= 0xFFFF) {
    units[0] = static_cast<WORD>(character);
  } else {
    const char32_t adjusted = character - 0x10000;
    units[0] = static_cast<WORD>(0xD800 + (adjusted >> 10));
    units[1] = static_cast<WORD>(0xDC00 + (adjusted & 0x3FF));
    unit_count = 2;
  }

  const std::size_t required = unit_count * 2;
  if (destination.size() < required) {
    return 0;
  }

  std::size_t count = 0;
  for (std::size_t index = 0; index < unit_count; ++index) {
    INPUT down{};
    down.type = INPUT_KEYBOARD;
    down.ki.wScan = units[index];
    down.ki.dwFlags = KEYEVENTF_UNICODE;
    down.ki.dwExtraInfo = kKeyinaInjectionMarker;
    destination[count++] = down;

    INPUT up = down;
    up.ki.dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP;
    destination[count++] = up;
  }
  return count;
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
