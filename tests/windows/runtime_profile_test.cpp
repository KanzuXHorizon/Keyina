#include <keyina/windows/runtime_profile.h>

#include "../test_support.h"

#include <array>
#include <bit>
#include <cstddef>
#include <cstdint>
#include <span>

namespace {

using keyina::windows::KeystrokeOverlayFallbackCorner;
using keyina::windows::RuntimeHotkeyGesture;
using keyina::windows::RuntimeInputProfileError;

constexpr std::array<std::uint8_t, 36> kLegacyDefaultVector{
    0x4B, 0x49, 0x52, 0x50, 0x02, 0x24, 0x11, 0x06,
    0x02, 0x03, 0x00, 0x01, 0x05, 0x20, 0x00, 0x05,
    0x56, 0x00, 0x05, 0x54, 0x00, 0x05, 0x5A, 0x00,
    0x00, 0x1B, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00,
    0xB6, 0xCD, 0x5D, 0xCA,
};

constexpr std::array<std::uint8_t, 40> kPreviousDefaultVector{
    0x4B, 0x49, 0x52, 0x50, 0x03, 0x28, 0x11, 0x06,
    0x02, 0x03, 0x00, 0x01, 0x05, 0x20, 0x00, 0x05,
    0x56, 0x00, 0x05, 0x54, 0x00, 0x05, 0x5A, 0x00,
    0x00, 0x1B, 0x00, 0x64, 0x01, 0x00, 0x00, 0x00,
    0x5C, 0x84, 0x03, 0x1E, 0xE6, 0x8F, 0xA6, 0xBC,
};

constexpr std::array<std::uint8_t, 40> kDefaultVector{
    0x4B, 0x49, 0x52, 0x50, 0x04, 0x28, 0x11, 0x06,
    0x02, 0x03, 0x00, 0x01, 0x05, 0x20, 0x00, 0x05,
    0x56, 0x00, 0x05, 0x54, 0x00, 0x05, 0x5A, 0x00,
    0x00, 0x1B, 0x00, 0x64, 0x01, 0x00, 0x00, 0x00,
    0x5C, 0x84, 0x03, 0x1E, 0xB9, 0x97, 0x01, 0xEA,
};

template <std::size_t Size>
std::span<const std::byte> AsBytes(
    const std::array<std::uint8_t, Size>& value) {
  return std::as_bytes(std::span{value});
}

std::uint32_t ComputeFnv1a(std::span<const std::uint8_t> bytes) {
  std::uint32_t hash = 2166136261u;
  for (const auto value : bytes) {
    hash ^= value;
    hash *= 16777619u;
  }
  return hash;
}

template <std::size_t Size>
void RewriteChecksum(std::array<std::uint8_t, Size>& bytes) {
  const std::size_t offset = Size == 36 ? 32 : 36;
  const auto checksum = ComputeFnv1a(std::span{bytes}.first(offset));
  bytes[offset] = static_cast<std::uint8_t>(checksum & 0xFFu);
  bytes[offset + 1] = static_cast<std::uint8_t>((checksum >> 8u) & 0xFFu);
  bytes[offset + 2] = static_cast<std::uint8_t>((checksum >> 16u) & 0xFFu);
  bytes[offset + 3] = static_cast<std::uint8_t>((checksum >> 24u) & 0xFFu);
}

}  // namespace

KEYINA_TEST(default_native_profile_restores_invalid_latin_tokens) {
  const auto profile = keyina::windows::DefaultRuntimeInputProfile();

  KEYINA_EXPECT_TRUE(profile.vietnamese_enabled);
  KEYINA_EXPECT_TRUE(profile.restore_invalid_word);
}

KEYINA_TEST(runtime_input_profile_decodes_legacy_managed_vector) {
  const auto result = keyina::windows::DecodeRuntimeInputProfile(
      AsBytes(kLegacyDefaultVector));
  KEYINA_EXPECT_EQ(result.error, RuntimeInputProfileError::None);
  KEYINA_EXPECT_TRUE(!result.profile.keystroke_overlay.enabled);
  KEYINA_EXPECT_EQ(result.profile.keystroke_overlay.size_percent, 100);
}

KEYINA_TEST(runtime_input_profile_decodes_previous_managed_default_vector) {
  const auto result = keyina::windows::DecodeRuntimeInputProfile(
      AsBytes(kPreviousDefaultVector));

  KEYINA_EXPECT_EQ(result.error, RuntimeInputProfileError::None);
  KEYINA_EXPECT_TRUE(!result.profile.keystroke_overlay.enabled);
  KEYINA_EXPECT_EQ(result.profile.keystroke_overlay.size_percent, 100);
}

KEYINA_TEST(runtime_input_profile_preserves_previous_overlay_flags) {
  auto previous = kPreviousDefaultVector;
  previous[26] = 0x39;
  RewriteChecksum(previous);

  const auto result = keyina::windows::DecodeRuntimeInputProfile(
      AsBytes(previous));
  KEYINA_EXPECT_EQ(result.error, RuntimeInputProfileError::None);
  KEYINA_EXPECT_TRUE(result.profile.keystroke_overlay.enabled);
  KEYINA_EXPECT_TRUE(result.profile.keystroke_overlay.presentation_mode);
  KEYINA_EXPECT_EQ(
      result.profile.keystroke_overlay.fallback_corner,
      KeystrokeOverlayFallbackCorner::TopLeft);
}

KEYINA_TEST(runtime_input_profile_decodes_managed_default_vector) {
  const auto result = keyina::windows::DecodeRuntimeInputProfile(
      AsBytes(kDefaultVector));

  KEYINA_EXPECT_EQ(result.error, RuntimeInputProfileError::None);
  KEYINA_EXPECT_TRUE(result.profile.vietnamese_enabled);
  KEYINA_EXPECT_TRUE(!result.profile.speech_enabled);
  KEYINA_EXPECT_TRUE(!result.profile.translation_enabled);
  KEYINA_EXPECT_TRUE(result.profile.restore_invalid_word);
  KEYINA_EXPECT_TRUE(!result.profile.keystroke_overlay.enabled);
  KEYINA_EXPECT_EQ(result.profile.keystroke_overlay.size_percent, 100);
  KEYINA_EXPECT_EQ(result.profile.keystroke_overlay.opacity_percent, 92);
  KEYINA_EXPECT_EQ(result.profile.keystroke_overlay.hide_delay_milliseconds, 900);
  KEYINA_EXPECT_TRUE(!result.profile.keystroke_overlay.per_key_sound_enabled);
  KEYINA_EXPECT_EQ(result.profile.keystroke_overlay.sound_volume_percent, 30);
  KEYINA_EXPECT_EQ(result.profile.source_schema_version, 1);
  KEYINA_EXPECT_EQ(result.profile.hotkeys[0].gesture,
                   RuntimeHotkeyGesture::ModifierGesture);
  KEYINA_EXPECT_EQ(result.profile.hotkeys[0].modifiers, 0x03);
  KEYINA_EXPECT_EQ(result.profile.hotkeys[0].virtual_key, 0x00);
  KEYINA_EXPECT_EQ(result.profile.hotkeys[1].gesture,
                   RuntimeHotkeyGesture::Hold);
  KEYINA_EXPECT_EQ(result.profile.hotkeys[1].virtual_key, 0x20);
  KEYINA_EXPECT_EQ(result.profile.hotkeys[4].gesture,
                   RuntimeHotkeyGesture::Press);
  KEYINA_EXPECT_EQ(result.profile.hotkeys[4].virtual_key, 0x5A);
  KEYINA_EXPECT_EQ(result.profile.hotkeys[5].gesture,
                   RuntimeHotkeyGesture::Press);
  KEYINA_EXPECT_EQ(result.profile.hotkeys[5].virtual_key, 0x1B);
}

KEYINA_TEST(runtime_input_profile_decodes_center_overlay_positions) {
  auto centered = kDefaultVector;
  centered[26] = 0x69;
  RewriteChecksum(centered);

  const auto result = keyina::windows::DecodeRuntimeInputProfile(
      AsBytes(centered));
  KEYINA_EXPECT_EQ(result.error, RuntimeInputProfileError::None);
  KEYINA_EXPECT_TRUE(result.profile.keystroke_overlay.enabled);
  KEYINA_EXPECT_TRUE(result.profile.keystroke_overlay.presentation_mode);
  KEYINA_EXPECT_EQ(
      result.profile.keystroke_overlay.fallback_corner,
      KeystrokeOverlayFallbackCorner::TopCenter);
}

KEYINA_TEST(runtime_input_profile_decodes_quick_telex_flag) {
  auto quick_telex = kDefaultVector;
  quick_telex[6] |= 0x40;
  RewriteChecksum(quick_telex);

  const auto result = keyina::windows::DecodeRuntimeInputProfile(
      AsBytes(quick_telex));
  KEYINA_EXPECT_EQ(result.error, RuntimeInputProfileError::None);
  KEYINA_EXPECT_TRUE(result.profile.quick_telex_letters);
}

KEYINA_TEST(runtime_input_profile_decodes_disabled_standalone_w_flag) {
  auto simple_telex = kDefaultVector;
  simple_telex[6] |= 0x80;
  RewriteChecksum(simple_telex);

  const auto result = keyina::windows::DecodeRuntimeInputProfile(
      AsBytes(simple_telex));
  KEYINA_EXPECT_EQ(result.error, RuntimeInputProfileError::None);
  KEYINA_EXPECT_TRUE(!result.profile.standalone_w_to_u_horn);
}

KEYINA_TEST(runtime_input_profile_rejects_corruption) {
  auto checksum_corrupt = kDefaultVector;
  checksum_corrupt[8] ^= 0x01;
  KEYINA_EXPECT_EQ(
      keyina::windows::DecodeRuntimeInputProfile(AsBytes(checksum_corrupt)).error,
      RuntimeInputProfileError::InvalidChecksum);

  auto unknown_version = kDefaultVector;
  unknown_version[4] = 99;
  RewriteChecksum(unknown_version);
  KEYINA_EXPECT_EQ(
      keyina::windows::DecodeRuntimeInputProfile(AsBytes(unknown_version)).error,
      RuntimeInputProfileError::UnsupportedVersion);

  auto unsupported_gesture = kDefaultVector;
  unsupported_gesture[8] = 0x7F;
  RewriteChecksum(unsupported_gesture);
  KEYINA_EXPECT_EQ(
      keyina::windows::DecodeRuntimeInputProfile(AsBytes(unsupported_gesture)).error,
      RuntimeInputProfileError::InvalidHotkey);

  auto invalid_sound_volume = kDefaultVector;
  invalid_sound_volume[35] = 127;
  RewriteChecksum(invalid_sound_volume);
  KEYINA_EXPECT_EQ(
      keyina::windows::DecodeRuntimeInputProfile(AsBytes(invalid_sound_volume)).error,
      RuntimeInputProfileError::InvalidHeader);
}

KEYINA_TEST(runtime_input_profile_rejects_wrong_size) {
  const auto short_bytes = AsBytes(kDefaultVector).first(39);
  KEYINA_EXPECT_EQ(
      keyina::windows::DecodeRuntimeInputProfile(short_bytes).error,
      RuntimeInputProfileError::InvalidLength);
}
