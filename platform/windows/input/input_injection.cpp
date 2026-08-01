#include <keyina/windows/input_injection.h>

#include <algorithm>
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

std::size_t BuildPartialInputRecoverySequence(
    std::span<const INPUT> submitted,
    std::size_t inserted_count,
    std::span<INPUT> destination) noexcept {
  const std::size_t accepted = std::min(inserted_count, submitted.size());
  auto same_key = [](const KEYBDINPUT& left,
                     const KEYBDINPUT& right) noexcept {
    constexpr DWORD identity_flags = KEYEVENTF_EXTENDEDKEY |
        KEYEVENTF_UNICODE | KEYEVENTF_SCANCODE;
    return left.wVk == right.wVk && left.wScan == right.wScan &&
        (left.dwFlags & identity_flags) ==
            (right.dwFlags & identity_flags);
  };

  std::size_t active_count = 0;
  for (std::size_t index = 0; index < accepted; ++index) {
    const INPUT& input = submitted[index];
    if (input.type != INPUT_KEYBOARD) {
      continue;
    }
    if ((input.ki.dwFlags & KEYEVENTF_KEYUP) == 0) {
      if (active_count == destination.size()) {
        return 0;
      }
      destination[active_count++] = input;
      continue;
    }
    for (std::size_t active = active_count; active > 0; --active) {
      if (!same_key(destination[active - 1].ki, input.ki)) {
        continue;
      }
      for (std::size_t move = active; move < active_count; ++move) {
        destination[move - 1] = destination[move];
      }
      --active_count;
      break;
    }
  }

  std::reverse(destination.begin(), destination.begin() + active_count);
  for (std::size_t index = 0; index < active_count; ++index) {
    destination[index].ki.dwFlags |= KEYEVENTF_KEYUP;
    destination[index].ki.dwExtraInfo = kKeyinaInjectionMarker;
  }
  return active_count;
}

bool ShouldRestoreClipboard(
    DWORD owned_sequence,
    DWORD current_sequence) noexcept {
  return owned_sequence != 0 && owned_sequence == current_sequence;
}

bool IsStandardEditableWindowClass(
    std::wstring_view class_name) noexcept {
  auto equals_ascii_case_insensitive = [](
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
  };
  auto contains_ascii_case_insensitive = [&](std::wstring_view needle) noexcept {
    if (needle.size() > class_name.size()) {
      return false;
    }
    for (std::size_t offset = 0;
         offset + needle.size() <= class_name.size(); ++offset) {
      if (equals_ascii_case_insensitive(
              class_name.substr(offset, needle.size()), needle)) {
        return true;
      }
    }
    return false;
  };

  return equals_ascii_case_insensitive(class_name, L"Edit") ||
      contains_ascii_case_insensitive(L".EDIT.") ||
      contains_ascii_case_insensitive(L"RICHEDIT");
}

bool RequiresSelectionReplacementForWindowClass(
    std::wstring_view class_name) noexcept {
  return class_name.starts_with(L"Chrome_");
}

}  // namespace keyina::windows
