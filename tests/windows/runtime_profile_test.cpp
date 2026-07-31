#include <keyina/windows/runtime_profile.h>

#include "../test_support.h"

#include <array>
#include <bit>
#include <cstddef>
#include <cstdint>
#include <span>

namespace {

using keyina::windows::RuntimeHotkeyGesture;
using keyina::windows::RuntimeInputProfileError;

constexpr std::array<std::uint8_t, 36> kDefaultVector{
    0x4B, 0x49, 0x52, 0x50, 0x02, 0x24, 0x11, 0x06,
    0x02, 0x03, 0x00, 0x01, 0x05, 0x20, 0x00, 0x05,
    0x56, 0x00, 0x05, 0x54, 0x00, 0x05, 0x5A, 0x00,
    0x00, 0x1B, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00,
    0xB6, 0xCD, 0x5D, 0xCA,
};

std::span<const std::byte> AsBytes(
    const std::array<std::uint8_t, 36>& value) {
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

void RewriteChecksum(std::array<std::uint8_t, 36>& bytes) {
  const auto checksum = ComputeFnv1a(std::span{bytes}.first<32>());
  bytes[32] = static_cast<std::uint8_t>(checksum & 0xFFu);
  bytes[33] = static_cast<std::uint8_t>((checksum >> 8u) & 0xFFu);
  bytes[34] = static_cast<std::uint8_t>((checksum >> 16u) & 0xFFu);
  bytes[35] = static_cast<std::uint8_t>((checksum >> 24u) & 0xFFu);
}

}  // namespace

KEYINA_TEST(default_native_profile_restores_invalid_latin_tokens) {
  const auto profile = keyina::windows::DefaultRuntimeInputProfile();

  KEYINA_EXPECT_TRUE(profile.vietnamese_enabled);
  KEYINA_EXPECT_TRUE(profile.restore_invalid_word);
}

KEYINA_TEST(runtime_input_profile_decodes_managed_default_vector) {
  const auto result = keyina::windows::DecodeRuntimeInputProfile(
      AsBytes(kDefaultVector));

  KEYINA_EXPECT_EQ(result.error, RuntimeInputProfileError::None);
  KEYINA_EXPECT_TRUE(result.profile.vietnamese_enabled);
  KEYINA_EXPECT_TRUE(!result.profile.speech_enabled);
  KEYINA_EXPECT_TRUE(!result.profile.translation_enabled);
  KEYINA_EXPECT_TRUE(result.profile.restore_invalid_word);
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

KEYINA_TEST(runtime_input_profile_rejects_corruption) {
  auto checksum_corrupt = kDefaultVector;
  checksum_corrupt[8] ^= 0x01;
  KEYINA_EXPECT_EQ(
      keyina::windows::DecodeRuntimeInputProfile(AsBytes(checksum_corrupt)).error,
      RuntimeInputProfileError::InvalidChecksum);

  auto unknown_version = kDefaultVector;
  unknown_version[4] = 3;
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

  auto unknown_flag = kDefaultVector;
  unknown_flag[6] |= 0x80;
  RewriteChecksum(unknown_flag);
  KEYINA_EXPECT_EQ(
      keyina::windows::DecodeRuntimeInputProfile(AsBytes(unknown_flag)).error,
      RuntimeInputProfileError::InvalidHeader);
}

KEYINA_TEST(runtime_input_profile_rejects_wrong_size) {
  const auto short_bytes = AsBytes(kDefaultVector).first(35);
  KEYINA_EXPECT_EQ(
      keyina::windows::DecodeRuntimeInputProfile(short_bytes).error,
      RuntimeInputProfileError::InvalidLength);
}
