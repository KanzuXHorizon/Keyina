#include <keyina/windows/input_injection.h>

#include "../test_support.h"

#include <array>
#include <cstring>
#include <span>

KEYINA_TEST(native_input_injection_contains_keyboard_events_only) {
  keyina::windows::InputDecision decision{};
  decision.suppress = true;
  decision.backspace_count = 2;
  decision.insert_units = 2;
  decision.insert[0] = L'á';
  decision.insert[1] = L'x';
  std::array<INPUT, 16> inputs;
  std::memset(inputs.data(), 0xA5, sizeof(inputs));
  for (auto& input : inputs) {
    input.type = INPUT_MOUSE;
  }

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
  std::array<INPUT, 16> inputs;
  std::memset(inputs.data(), 0x5A, sizeof(inputs));
  for (auto& input : inputs) {
    input.type = INPUT_MOUSE;
  }

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

KEYINA_TEST(native_partial_input_recovery_releases_only_unmatched_keys) {
  keyina::windows::InputDecision decision{};
  decision.suppress = true;
  decision.backspace_count = 2;
  decision.insert_units = 1;
  decision.insert[0] = L'x';
  std::array<INPUT, 16> submitted{};
  const std::size_t submitted_count =
      keyina::windows::BuildClipboardPasteSequence(decision, submitted);
  KEYINA_EXPECT_EQ(submitted_count, std::size_t{10});

  std::array<INPUT, 16> recovery{};
  const std::size_t recovery_count =
      keyina::windows::BuildPartialInputRecoverySequence(
          std::span<const INPUT>(submitted.data(), submitted_count),
          8,
          recovery);

  KEYINA_EXPECT_EQ(recovery_count, std::size_t{2});
  KEYINA_EXPECT_EQ(recovery[0].ki.wVk, static_cast<WORD>('V'));
  KEYINA_EXPECT_EQ(recovery[1].ki.wVk, static_cast<WORD>(VK_CONTROL));
  for (std::size_t index = 0; index < recovery_count; ++index) {
    KEYINA_EXPECT_TRUE(
        (recovery[index].ki.dwFlags & KEYEVENTF_KEYUP) != 0);
    KEYINA_EXPECT_EQ(
        recovery[index].ki.dwExtraInfo,
        keyina::windows::kKeyinaInjectionMarker);
  }
}

KEYINA_TEST(native_partial_input_recovery_handles_unicode_key_down) {
  std::array<INPUT, 4> submitted{};
  const std::size_t submitted_count =
      keyina::windows::BuildLiteralUnicodeInputSequence(U'😀', submitted);
  KEYINA_EXPECT_EQ(submitted_count, std::size_t{4});

  std::array<INPUT, 4> recovery{};
  const std::size_t recovery_count =
      keyina::windows::BuildPartialInputRecoverySequence(
          std::span<const INPUT>(submitted.data(), submitted_count),
          3,
          recovery);
  KEYINA_EXPECT_EQ(recovery_count, std::size_t{1});
  KEYINA_EXPECT_EQ(recovery[0].ki.wScan, static_cast<WORD>(0xDE00));
  KEYINA_EXPECT_EQ(
      recovery[0].ki.dwFlags,
      static_cast<DWORD>(KEYEVENTF_UNICODE | KEYEVENTF_KEYUP));
}

KEYINA_TEST(native_clipboard_restore_requires_the_same_clipboard_sequence) {
  KEYINA_EXPECT_TRUE(keyina::windows::ShouldRestoreClipboard(42, 42));
  KEYINA_EXPECT_TRUE(!keyina::windows::ShouldRestoreClipboard(42, 43));
  KEYINA_EXPECT_TRUE(!keyina::windows::ShouldRestoreClipboard(0, 0));
}

KEYINA_TEST(native_text_delivery_policy_is_synchronous_and_deterministic) {
  using keyina::windows::TextDeliveryMode;

  KEYINA_EXPECT_EQ(
      keyina::windows::ChooseTextDeliveryMode(false, false),
      TextDeliveryMode::Keyboard);
  KEYINA_EXPECT_EQ(
      keyina::windows::ChooseTextDeliveryMode(false, true),
      TextDeliveryMode::SelectionReplacement);
  KEYINA_EXPECT_EQ(
      keyina::windows::ChooseTextDeliveryMode(true, false),
      TextDeliveryMode::Clipboard);
  KEYINA_EXPECT_EQ(
      keyina::windows::ChooseTextDeliveryMode(true, true),
      TextDeliveryMode::Clipboard);
}

KEYINA_TEST(native_chromium_owned_text_stream_requires_safe_selection_delivery) {
  KEYINA_EXPECT_TRUE(keyina::windows::ShouldOwnTextStream(
      true, false, false, true));
  KEYINA_EXPECT_TRUE(!keyina::windows::ShouldOwnTextStream(
      false, false, false, true));
  KEYINA_EXPECT_TRUE(!keyina::windows::ShouldOwnTextStream(
      true, true, false, true));
  KEYINA_EXPECT_TRUE(!keyina::windows::ShouldOwnTextStream(
      true, false, true, true));
  KEYINA_EXPECT_TRUE(!keyina::windows::ShouldOwnTextStream(
      true, false, false, false));
}

KEYINA_TEST(native_literal_unicode_sequence_encodes_bmp_and_non_bmp_scalars) {
  std::array<INPUT, 4> destination;
  std::memset(destination.data(), 0xA5, sizeof(destination));
  for (auto& input : destination) {
    input.type = INPUT_MOUSE;
  }

  const auto bmp_count = keyina::windows::BuildLiteralUnicodeInputSequence(
      U'a', destination);
  KEYINA_EXPECT_EQ(bmp_count, std::size_t{2});
  KEYINA_EXPECT_EQ(destination[0].type, static_cast<DWORD>(INPUT_KEYBOARD));
  KEYINA_EXPECT_EQ(destination[0].ki.wVk, static_cast<WORD>(0));
  KEYINA_EXPECT_EQ(destination[0].ki.wScan, static_cast<WORD>(L'a'));
  KEYINA_EXPECT_EQ(
      destination[0].ki.dwFlags,
      static_cast<DWORD>(KEYEVENTF_UNICODE));
  KEYINA_EXPECT_EQ(
      destination[1].ki.dwFlags,
      static_cast<DWORD>(KEYEVENTF_UNICODE | KEYEVENTF_KEYUP));
  KEYINA_EXPECT_EQ(
      destination[0].ki.dwExtraInfo,
      keyina::windows::kKeyinaInjectionMarker);
  KEYINA_EXPECT_EQ(
      destination[1].ki.dwExtraInfo,
      keyina::windows::kKeyinaInjectionMarker);

  const auto non_bmp_count =
      keyina::windows::BuildLiteralUnicodeInputSequence(U'😀', destination);
  KEYINA_EXPECT_EQ(non_bmp_count, std::size_t{4});
  KEYINA_EXPECT_EQ(destination[0].ki.wScan, static_cast<WORD>(0xD83D));
  KEYINA_EXPECT_EQ(destination[1].ki.wScan, static_cast<WORD>(0xD83D));
  KEYINA_EXPECT_EQ(destination[2].ki.wScan, static_cast<WORD>(0xDE00));
  KEYINA_EXPECT_EQ(destination[3].ki.wScan, static_cast<WORD>(0xDE00));
  for (std::size_t index = 0; index < non_bmp_count; ++index) {
    KEYINA_EXPECT_EQ(
        destination[index].type,
        static_cast<DWORD>(INPUT_KEYBOARD));
    KEYINA_EXPECT_EQ(
        destination[index].ki.dwExtraInfo,
        keyina::windows::kKeyinaInjectionMarker);
    const DWORD expected_flags = (index & 1u) == 0
        ? static_cast<DWORD>(KEYEVENTF_UNICODE)
        : static_cast<DWORD>(KEYEVENTF_UNICODE | KEYEVENTF_KEYUP);
    KEYINA_EXPECT_EQ(destination[index].ki.dwFlags, expected_flags);
  }
}

KEYINA_TEST(native_literal_unicode_sequence_rejects_invalid_or_small_destination) {
  std::array<INPUT, 3> destination;
  std::memset(destination.data(), 0xCC, sizeof(destination));
  for (auto& input : destination) {
    input.type = INPUT_MOUSE;
  }

  KEYINA_EXPECT_EQ(
      keyina::windows::BuildLiteralUnicodeInputSequence(U'\0', destination),
      std::size_t{0});
  KEYINA_EXPECT_EQ(
      keyina::windows::BuildLiteralUnicodeInputSequence(
          static_cast<char32_t>(0xD800), destination),
      std::size_t{0});
  KEYINA_EXPECT_EQ(
      keyina::windows::BuildLiteralUnicodeInputSequence(
          static_cast<char32_t>(0x110000), destination),
      std::size_t{0});
  KEYINA_EXPECT_EQ(
      keyina::windows::BuildLiteralUnicodeInputSequence(U'😀', destination),
      std::size_t{0});
  for (const auto& input : destination) {
    KEYINA_EXPECT_EQ(input.type, static_cast<DWORD>(INPUT_MOUSE));
  }
}

KEYINA_TEST(native_standard_edit_classes_use_synchronous_replacement) {
  KEYINA_EXPECT_TRUE(keyina::windows::IsStandardEditableWindowClass(L"Edit"));
  KEYINA_EXPECT_TRUE(keyina::windows::IsStandardEditableWindowClass(
      L"WindowsForms10.EDIT.app.0.141b42a_r8_ad1"));
  KEYINA_EXPECT_TRUE(keyina::windows::IsStandardEditableWindowClass(
      L"RICHEDIT50W"));
  KEYINA_EXPECT_TRUE(keyina::windows::IsStandardEditableWindowClass(
      L"WindowsForms10.RichEdit20W.app.0.141b42a_r8_ad1"));
  KEYINA_EXPECT_TRUE(!keyina::windows::IsStandardEditableWindowClass(
      L"Chrome_RenderWidgetHostHWND"));
  KEYINA_EXPECT_TRUE(!keyina::windows::IsStandardEditableWindowClass(L"UnityWndClass"));
}

KEYINA_TEST(native_chromium_windows_require_selection_replacement) {
  KEYINA_EXPECT_TRUE(
      keyina::windows::RequiresSelectionReplacementForWindowClass(
          L"Chrome_WidgetWin_1"));
  KEYINA_EXPECT_TRUE(
      keyina::windows::RequiresSelectionReplacementForWindowClass(
          L"Chrome_RenderWidgetHostHWND"));
  KEYINA_EXPECT_TRUE(
      !keyina::windows::RequiresSelectionReplacementForWindowClass(L"Edit"));
  KEYINA_EXPECT_TRUE(
      !keyina::windows::RequiresSelectionReplacementForWindowClass(
          L"ApplicationFrameWindow"));
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

KEYINA_TEST(native_input_injection_rejects_corrupt_insert_length_without_writes) {
  keyina::windows::InputDecision decision{};
  decision.suppress = true;
  decision.insert_units = static_cast<std::uint16_t>(
      decision.insert.size() + 1);
  std::array<INPUT, 8> destination;
  std::memset(destination.data(), 0xCC, sizeof(destination));
  for (auto& input : destination) {
    input.type = INPUT_MOUSE;
  }

  KEYINA_EXPECT_EQ(
      keyina::windows::BuildKeyboardInputSequence(decision, destination),
      std::size_t{0});
  for (const auto& input : destination) {
    KEYINA_EXPECT_EQ(input.type, static_cast<DWORD>(INPUT_MOUSE));
  }

  KEYINA_EXPECT_EQ(
      keyina::windows::BuildSelectionReplacementSequence(
          decision, destination),
      std::size_t{0});
  for (const auto& input : destination) {
    KEYINA_EXPECT_EQ(input.type, static_cast<DWORD>(INPUT_MOUSE));
  }
}
