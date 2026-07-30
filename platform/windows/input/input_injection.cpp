#include <keyina/windows/input_injection.h>

#include <cstddef>

namespace keyina::windows {

std::size_t BuildKeyboardInputSequence(
    const InputDecision& decision,
    std::span<INPUT> destination) noexcept {
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

}  // namespace keyina::windows
