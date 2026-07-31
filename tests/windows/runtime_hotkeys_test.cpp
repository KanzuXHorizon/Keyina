#include <keyina/windows/runtime_hotkeys.h>

#include "../test_support.h"

#include <string_view>

namespace {

keyina::windows::RuntimeInputProfile DefaultProfile() {
  keyina::windows::RuntimeInputProfile profile{};
  profile.hotkeys = {
      keyina::windows::RuntimeHotkeyBinding{
          keyina::windows::RuntimeHotkeyGesture::ModifierGesture, 0x03, 0x00},
      keyina::windows::RuntimeHotkeyBinding{
          keyina::windows::RuntimeHotkeyGesture::Hold, 0x05, 0x20},
      keyina::windows::RuntimeHotkeyBinding{
          keyina::windows::RuntimeHotkeyGesture::Press, 0x05, 0x56},
      keyina::windows::RuntimeHotkeyBinding{
          keyina::windows::RuntimeHotkeyGesture::Press, 0x05, 0x54},
      keyina::windows::RuntimeHotkeyBinding{
          keyina::windows::RuntimeHotkeyGesture::Press, 0x05, 0x5A},
      keyina::windows::RuntimeHotkeyBinding{
          keyina::windows::RuntimeHotkeyGesture::Press, 0x00, 0x1B},
  };
  return profile;
}

keyina::windows::PhysicalKeyEvent Key(
    std::uint16_t virtual_key,
    bool key_down,
    bool control = false,
    bool shift = false,
    bool alt = false) {
  return keyina::windows::PhysicalKeyEvent{
      virtual_key,
      U'\0',
      key_down,
      false,
      shift,
      control,
      alt,
      false,
  };
}

}  // namespace

KEYINA_TEST(native_hotkey_router_emits_press_and_matching_release_once) {
  auto profile = DefaultProfile();
  keyina::windows::RuntimeHotkeyRouter router;

  const auto pressed = router.Process(
      Key(0x20, true, true, false, true),
      profile,
      false,
      false);
  KEYINA_EXPECT_TRUE(pressed.suppress);
  KEYINA_EXPECT_EQ(
      pressed.command,
      keyina::windows::RuntimeCommand::PushToTalkPressed);

  const auto repeated = router.Process(
      Key(0x20, true, true, false, true),
      profile,
      true,
      false);
  KEYINA_EXPECT_TRUE(repeated.suppress);
  KEYINA_EXPECT_EQ(repeated.command, keyina::windows::RuntimeCommand::None);

  const auto released = router.Process(
      Key(0x20, false, true, false, true),
      profile,
      false,
      true);
  KEYINA_EXPECT_TRUE(released.suppress);
  KEYINA_EXPECT_EQ(
      released.command,
      keyina::windows::RuntimeCommand::PushToTalkReleased);
}

KEYINA_TEST(native_hotkey_router_releases_hold_when_modifier_is_released_first) {
  auto profile = DefaultProfile();
  keyina::windows::RuntimeHotkeyRouter router;

  const auto pressed = router.Process(
      Key(0x20, true, true, false, true),
      profile,
      false,
      false);
  KEYINA_EXPECT_TRUE(pressed.suppress);

  const auto modifier_released = router.Process(
      Key(0xA4, false, true, false, false),
      profile,
      false,
      true);
  KEYINA_EXPECT_TRUE(!modifier_released.suppress);
  KEYINA_EXPECT_EQ(
      modifier_released.command,
      keyina::windows::RuntimeCommand::PushToTalkReleased);

  const auto primary_released = router.Process(
      Key(0x20, false, true, false, false),
      profile,
      false,
      true);
  KEYINA_EXPECT_TRUE(primary_released.suppress);
  KEYINA_EXPECT_EQ(
      primary_released.command,
      keyina::windows::RuntimeCommand::None);
}

KEYINA_TEST(native_hotkey_router_matches_exact_press_chords_without_repeat) {
  auto profile = DefaultProfile();
  keyina::windows::RuntimeHotkeyRouter router;

  const auto wrong = router.Process(
      Key('V', true, true, true, true),
      profile,
      false,
      false);
  KEYINA_EXPECT_TRUE(!wrong.suppress);

  const auto matched = router.Process(
      Key('V', true, true, false, true),
      profile,
      false,
      false);
  KEYINA_EXPECT_TRUE(matched.suppress);
  KEYINA_EXPECT_EQ(
      matched.command,
      keyina::windows::RuntimeCommand::ToggleDictation);

  const auto repeated = router.Process(
      Key('V', true, true, false, true),
      profile,
      true,
      false);
  KEYINA_EXPECT_TRUE(repeated.suppress);
  KEYINA_EXPECT_EQ(repeated.command, keyina::windows::RuntimeCommand::None);

  const auto released = router.Process(
      Key('V', false, true, false, true),
      profile,
      false,
      false);
  KEYINA_EXPECT_TRUE(released.suppress);
  KEYINA_EXPECT_EQ(released.command, keyina::windows::RuntimeCommand::None);
}

KEYINA_TEST(native_hotkey_router_never_steals_escape_or_undo_without_companion) {
  auto profile = DefaultProfile();
  keyina::windows::RuntimeHotkeyRouter router;

  const auto undo = router.Process(
      Key('Z', true, true, false, true),
      profile,
      false,
      false);
  KEYINA_EXPECT_TRUE(!undo.suppress);

  const auto escape = router.Process(
      Key(0x1B, true),
      profile,
      false,
      false);
  KEYINA_EXPECT_TRUE(!escape.suppress);

  const auto active_undo = router.Process(
      Key('Z', true, true, false, true),
      profile,
      false,
      true);
  KEYINA_EXPECT_TRUE(active_undo.suppress);
  KEYINA_EXPECT_EQ(
      active_undo.command,
      keyina::windows::RuntimeCommand::UndoTranslation);

  const auto active_escape = router.Process(
      Key(0x1B, true),
      profile,
      false,
      true);
  KEYINA_EXPECT_TRUE(!active_escape.suppress);
  KEYINA_EXPECT_EQ(
      active_escape.command,
      keyina::windows::RuntimeCommand::None);
}

KEYINA_TEST(native_hotkey_router_cancels_suppression_when_launch_fails) {
  auto profile = DefaultProfile();
  keyina::windows::RuntimeHotkeyRouter router;

  const auto matched = router.Process(
      Key('T', true, true, false, true),
      profile,
      false,
      false);
  KEYINA_EXPECT_TRUE(matched.suppress);
  router.CancelSuppression('T');

  const auto released = router.Process(
      Key('T', false, true, false, true),
      profile,
      false,
      false);
  KEYINA_EXPECT_TRUE(!released.suppress);
}

KEYINA_TEST(native_runtime_command_arguments_match_managed_protocol) {
  using keyina::windows::RuntimeCommand;
  KEYINA_EXPECT_EQ(
      std::wstring_view(keyina::windows::RuntimeCommandArgument(
          RuntimeCommand::SetVietnameseEnabled)),
      std::wstring_view(
          L"--companion-command=set-vietnamese-enabled"));
  KEYINA_EXPECT_EQ(
      std::wstring_view(keyina::windows::RuntimeCommandArgument(
          RuntimeCommand::SetVietnameseDisabled)),
      std::wstring_view(
          L"--companion-command=set-vietnamese-disabled"));
  KEYINA_EXPECT_EQ(
      std::wstring_view(keyina::windows::RuntimeCommandArgument(
          RuntimeCommand::PushToTalkReleased)),
      std::wstring_view(L"--companion-command=push-to-talk-released"));
  KEYINA_EXPECT_EQ(
      std::wstring_view(keyina::windows::RuntimeCommandArgument(
          RuntimeCommand::CancelActiveCommand)),
      std::wstring_view(L"--companion-command=cancel-active-command"));
}
