#include <keyina/windows/input_injection.h>

#include "../test_support.h"

#include <array>
#include <span>

KEYINA_TEST(native_input_injection_contains_keyboard_events_only) {
  keyina::windows::InputDecision decision{};
  decision.suppress = true;
  decision.backspace_count = 2;
  decision.insert_units = 2;
  decision.insert[0] = L'á';
  decision.insert[1] = L'x';
  std::array<INPUT, 16> inputs{};

  const auto count = keyina::windows::BuildKeyboardInputSequence(
      decision, inputs);

  KEYINA_EXPECT_EQ(count, std::size_t{8});
  for (std::size_t index = 0; index < count; ++index) {
    KEYINA_EXPECT_EQ(inputs[index].type, static_cast<DWORD>(INPUT_KEYBOARD));
    KEYINA_EXPECT_EQ(inputs[index].ki.dwExtraInfo,
                     keyina::windows::kKeyinaInjectionMarker);
  }
  KEYINA_EXPECT_EQ(inputs[0].ki.wVk, static_cast<WORD>(VK_BACK));
  KEYINA_EXPECT_EQ(inputs[1].ki.dwFlags, static_cast<DWORD>(KEYEVENTF_KEYUP));
  KEYINA_EXPECT_EQ(inputs[4].ki.wVk, static_cast<WORD>(0));
  KEYINA_EXPECT_EQ(inputs[4].ki.wScan, static_cast<WORD>(L'á'));
  KEYINA_EXPECT_EQ(inputs[4].ki.dwFlags,
                   static_cast<DWORD>(KEYEVENTF_UNICODE));
  KEYINA_EXPECT_EQ(inputs[5].ki.dwFlags,
                   static_cast<DWORD>(KEYEVENTF_UNICODE | KEYEVENTF_KEYUP));
}

KEYINA_TEST(native_input_injection_fails_without_partial_mouse_or_keyboard_output) {
  keyina::windows::InputDecision decision{};
  decision.suppress = true;
  decision.backspace_count = 2;
  std::array<INPUT, 3> insufficient{};
  for (auto& input : insufficient) {
    input.type = INPUT_MOUSE;
  }

  const auto count = keyina::windows::BuildKeyboardInputSequence(
      decision, insufficient);

  KEYINA_EXPECT_EQ(count, std::size_t{0});
  for (const auto& input : insufficient) {
    KEYINA_EXPECT_EQ(input.type, static_cast<DWORD>(INPUT_MOUSE));
  }
}
