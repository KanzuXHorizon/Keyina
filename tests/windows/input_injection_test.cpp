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

KEYINA_TEST(native_clipboard_injection_uses_backspace_then_marked_ctrl_v) {
  keyina::windows::InputDecision decision{};
  decision.suppress = true;
  decision.backspace_count = 2;
  decision.insert_units = 2;
  decision.insert[0] = L'á';
  decision.insert[1] = L'x';
  std::array<INPUT, 16> inputs{};

  const auto count = keyina::windows::BuildClipboardPasteSequence(
      decision, inputs);

  KEYINA_EXPECT_EQ(count, std::size_t{10});
  KEYINA_EXPECT_EQ(inputs[0].ki.wVk, static_cast<WORD>(VK_SHIFT));
  KEYINA_EXPECT_EQ(inputs[1].ki.wVk, static_cast<WORD>(VK_LEFT));
  KEYINA_EXPECT_EQ(inputs[3].ki.wVk, static_cast<WORD>(VK_LEFT));
  KEYINA_EXPECT_EQ(inputs[5].ki.wVk, static_cast<WORD>(VK_SHIFT));
  KEYINA_EXPECT_EQ(inputs[6].ki.wVk, static_cast<WORD>(VK_CONTROL));
  KEYINA_EXPECT_EQ(inputs[7].ki.wVk, static_cast<WORD>('V'));
  KEYINA_EXPECT_EQ(inputs[8].ki.wVk, static_cast<WORD>('V'));
  KEYINA_EXPECT_EQ(inputs[9].ki.wVk, static_cast<WORD>(VK_CONTROL));
  for (std::size_t index = 0; index < count; ++index) {
    KEYINA_EXPECT_EQ(inputs[index].type, static_cast<DWORD>(INPUT_KEYBOARD));
    KEYINA_EXPECT_EQ(inputs[index].ki.dwExtraInfo,
                     keyina::windows::kKeyinaInjectionMarker);
  }
}

KEYINA_TEST(native_chromium_replacement_selects_text_before_inserting_unicode) {
  keyina::windows::InputDecision decision{};
  decision.suppress = true;
  decision.backspace_count = 2;
  decision.insert_units = 1;
  decision.insert[0] = L'ê';
  std::array<INPUT, 16> inputs{};

  const auto count = keyina::windows::BuildSelectionReplacementSequence(
      decision, inputs);

  KEYINA_EXPECT_EQ(count, std::size_t{8});
  KEYINA_EXPECT_EQ(inputs[0].ki.wVk, static_cast<WORD>(VK_SHIFT));
  KEYINA_EXPECT_EQ(inputs[1].ki.wVk, static_cast<WORD>(VK_LEFT));
  KEYINA_EXPECT_EQ(inputs[3].ki.wVk, static_cast<WORD>(VK_LEFT));
  KEYINA_EXPECT_EQ(inputs[5].ki.wVk, static_cast<WORD>(VK_SHIFT));
  KEYINA_EXPECT_EQ(inputs[6].ki.wScan, static_cast<WORD>(L'ê'));
  KEYINA_EXPECT_EQ(inputs[6].ki.dwFlags,
                   static_cast<DWORD>(KEYEVENTF_UNICODE));
  KEYINA_EXPECT_EQ(inputs[7].ki.dwFlags,
                   static_cast<DWORD>(KEYEVENTF_UNICODE | KEYEVENTF_KEYUP));
}

KEYINA_TEST(native_clipboard_restore_requires_the_same_clipboard_sequence) {
  KEYINA_EXPECT_TRUE(keyina::windows::ShouldRestoreClipboard(42, 42));
  KEYINA_EXPECT_TRUE(!keyina::windows::ShouldRestoreClipboard(42, 43));
  KEYINA_EXPECT_TRUE(!keyina::windows::ShouldRestoreClipboard(0, 0));
}

KEYINA_TEST(native_chromium_windows_defer_text_replacement_outside_the_hook) {
  KEYINA_EXPECT_TRUE(
      keyina::windows::ShouldDeferInputForWindowClass(L"Chrome_WidgetWin_1"));
  KEYINA_EXPECT_TRUE(
      keyina::windows::ShouldDeferInputForWindowClass(L"Chrome_RenderWidgetHostHWND"));
  KEYINA_EXPECT_TRUE(
      !keyina::windows::ShouldDeferInputForWindowClass(L"Edit"));
  KEYINA_EXPECT_TRUE(
      !keyina::windows::ShouldDeferInputForWindowClass(L"ApplicationFrameWindow"));
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
