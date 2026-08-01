#include <keyina/windows/runtime_profile.h>

#include <bit>
#include <cstddef>
#include <cstdint>

namespace keyina::windows {
namespace {

constexpr std::array<std::byte, 4> kMagic{
    std::byte{'K'}, std::byte{'I'}, std::byte{'R'}, std::byte{'P'}};
constexpr std::uint8_t kLegacyFormatVersion = 2;
constexpr std::uint8_t kFormatVersion = 3;
constexpr std::uint8_t kBindingCount = 6;
constexpr std::size_t kLegacyChecksumOffset = 32;
constexpr std::size_t kChecksumOffset = 36;
constexpr std::uint8_t kOverlayEnabledFlag = 1u << 0u;
constexpr std::uint8_t kOverlayMotionMask = 0b00000110u;
constexpr std::uint8_t kOverlayCornerMask = 0b00011000u;
constexpr std::uint8_t kOverlayPresentationFlag = 1u << 5u;
constexpr std::uint8_t kKnownOverlayFlags =
    kOverlayEnabledFlag | kOverlayMotionMask | kOverlayCornerMask |
    kOverlayPresentationFlag;
constexpr std::uint8_t kVietnameseEnabledFlag = 1u << 0u;
constexpr std::uint8_t kSpeechEnabledFlag = 1u << 1u;
constexpr std::uint8_t kTranslationEnabledFlag = 1u << 2u;
constexpr std::uint8_t kTraditionalTonePlacementFlag = 1u << 3u;
constexpr std::uint8_t kRestoreInvalidWordFlag = 1u << 4u;
constexpr std::uint8_t kClipboardCompatibilityFlag = 1u << 5u;
constexpr std::uint8_t kQuickTelexLettersFlag = 1u << 6u;
constexpr std::uint8_t kDisableStandaloneWFlag = 1u << 7u;
constexpr std::uint8_t kKnownFlags =
    kVietnameseEnabledFlag | kSpeechEnabledFlag | kTranslationEnabledFlag |
    kTraditionalTonePlacementFlag | kRestoreInvalidWordFlag |
    kClipboardCompatibilityFlag | kQuickTelexLettersFlag |
    kDisableStandaloneWFlag;
constexpr std::uint8_t kControlModifier = 1u << 0u;
constexpr std::uint8_t kShiftModifier = 1u << 1u;
constexpr std::uint8_t kAltModifier = 1u << 2u;
constexpr std::uint8_t kWindowsModifier = 1u << 3u;
constexpr std::uint8_t kKnownModifiers =
    kControlModifier | kShiftModifier | kAltModifier | kWindowsModifier;

std::uint8_t ByteAt(std::span<const std::byte> bytes,
                    std::size_t index) noexcept {
  return std::to_integer<std::uint8_t>(bytes[index]);
}

std::uint16_t ReadUInt16LittleEndian(std::span<const std::byte> bytes,
                                     std::size_t offset) noexcept {
  return static_cast<std::uint16_t>(ByteAt(bytes, offset)) |
         (static_cast<std::uint16_t>(ByteAt(bytes, offset + 1)) << 8u);
}

std::uint32_t ReadUInt32LittleEndian(std::span<const std::byte> bytes,
                                     std::size_t offset) noexcept {
  return static_cast<std::uint32_t>(ByteAt(bytes, offset)) |
         (static_cast<std::uint32_t>(ByteAt(bytes, offset + 1)) << 8u) |
         (static_cast<std::uint32_t>(ByteAt(bytes, offset + 2)) << 16u) |
         (static_cast<std::uint32_t>(ByteAt(bytes, offset + 3)) << 24u);
}

std::int32_t ReadInt32LittleEndian(std::span<const std::byte> bytes,
                                   std::size_t offset) noexcept {
  return std::bit_cast<std::int32_t>(ReadUInt32LittleEndian(bytes, offset));
}

std::uint32_t ComputeFnv1a(std::span<const std::byte> bytes) noexcept {
  std::uint32_t hash = 2166136261u;
  for (const auto value : bytes) {
    hash ^= std::to_integer<std::uint8_t>(value);
    hash *= 16777619u;
  }
  return hash;
}

bool IsModifierKey(std::uint8_t key) noexcept {
  return key == 0x5B || key == 0x5C ||
         (key >= 0xA0 && key <= 0xA5);
}

bool RequiresModifier(std::uint8_t key) noexcept {
  return (key >= 0x30 && key <= 0x5A) || key == 0x20 ||
         (key >= 0xBA && key <= 0xC0) ||
         (key >= 0xDB && key <= 0xDE);
}

bool HasDuplicateChord(
    const std::array<RuntimeHotkeyBinding, kRuntimeHotkeyCount>& bindings,
    std::size_t current) noexcept {
  for (std::size_t index = 0; index < current; ++index) {
    if (bindings[index].modifiers == bindings[current].modifiers &&
        bindings[index].virtual_key == bindings[current].virtual_key) {
      return true;
    }
  }
  return false;
}

bool ValidateBinding(
    const std::array<RuntimeHotkeyBinding, kRuntimeHotkeyCount>& bindings,
    std::size_t index) noexcept {
  const auto& binding = bindings[index];
  if ((binding.modifiers & ~kKnownModifiers) != 0 ||
      (binding.modifiers & kWindowsModifier) != 0 ||
      HasDuplicateChord(bindings, index)) {
    return false;
  }

  if (index == 0) {
    return binding.gesture == RuntimeHotkeyGesture::ModifierGesture &&
           binding.virtual_key == 0 &&
           std::popcount(binding.modifiers) >= 2;
  }

  const auto expected_gesture =
      index == 1 ? RuntimeHotkeyGesture::Hold : RuntimeHotkeyGesture::Press;
  if (binding.gesture != expected_gesture || binding.virtual_key == 0 ||
      IsModifierKey(binding.virtual_key)) {
    return false;
  }
  if (index == 1 && binding.modifiers == 0) {
    return false;
  }
  if (index != 5 && binding.virtual_key == 0x1B) {
    return false;
  }
  if (binding.modifiers == 0 && RequiresModifier(binding.virtual_key)) {
    return false;
  }
  return true;
}

RuntimeInputProfileError DecodeBindings(
    std::span<const std::byte> bytes,
    RuntimeInputProfile& profile) noexcept {
  for (std::size_t index = 0; index < kRuntimeHotkeyCount; ++index) {
    const auto offset = 8 + (index * 3);
    const auto gesture = ByteAt(bytes, offset);
    if (gesture > static_cast<std::uint8_t>(
                      RuntimeHotkeyGesture::ModifierGesture)) {
      return RuntimeInputProfileError::InvalidHotkey;
    }
    profile.hotkeys[index] = RuntimeHotkeyBinding{
        static_cast<RuntimeHotkeyGesture>(gesture),
        ByteAt(bytes, offset + 1),
        ByteAt(bytes, offset + 2),
    };
    if (!ValidateBinding(profile.hotkeys, index)) {
      return RuntimeInputProfileError::InvalidHotkey;
    }
  }
  return RuntimeInputProfileError::None;
}

}  // namespace

RuntimeInputProfile DefaultRuntimeInputProfile() noexcept {
  RuntimeInputProfile profile{};
  profile.vietnamese_enabled = true;
  profile.restore_invalid_word = true;
  profile.hotkeys = {
      RuntimeHotkeyBinding{
          RuntimeHotkeyGesture::ModifierGesture, 0x03, 0x00},
      RuntimeHotkeyBinding{
          RuntimeHotkeyGesture::Hold, 0x05, 0x20},
      RuntimeHotkeyBinding{
          RuntimeHotkeyGesture::Press, 0x05, 0x56},
      RuntimeHotkeyBinding{
          RuntimeHotkeyGesture::Press, 0x05, 0x54},
      RuntimeHotkeyBinding{
          RuntimeHotkeyGesture::Press, 0x05, 0x5A},
      RuntimeHotkeyBinding{
          RuntimeHotkeyGesture::Press, 0x00, 0x1B},
  };
  return profile;
}

RuntimeInputProfileResult DecodeRuntimeInputProfile(
    std::span<const std::byte> bytes) noexcept {
  RuntimeInputProfileResult result{};
  if (bytes.size() != kLegacyRuntimeInputProfileSize &&
      bytes.size() != kRuntimeInputProfileSize) {
    result.error = RuntimeInputProfileError::InvalidLength;
    return result;
  }
  for (std::size_t index = 0; index < kMagic.size(); ++index) {
    if (bytes[index] != kMagic[index]) {
      result.error = RuntimeInputProfileError::InvalidMagic;
      return result;
    }
  }

  const auto version = ByteAt(bytes, 4);
  const bool legacy = version == kLegacyFormatVersion;
  if (!legacy && version != kFormatVersion) {
    result.error = RuntimeInputProfileError::UnsupportedVersion;
    return result;
  }
  const auto expected_size = legacy ? kLegacyRuntimeInputProfileSize
                                    : kRuntimeInputProfileSize;
  const auto checksum_offset = legacy ? kLegacyChecksumOffset
                                      : kChecksumOffset;
  const auto flags = ByteAt(bytes, 6);
  if (bytes.size() != expected_size || ByteAt(bytes, 5) != expected_size ||
      ByteAt(bytes, 7) != kBindingCount || (flags & ~kKnownFlags) != 0) {
    result.error = RuntimeInputProfileError::InvalidHeader;
    return result;
  }
  if (legacy && (ByteAt(bytes, 26) != 0 || ByteAt(bytes, 27) != 0)) {
    result.error = RuntimeInputProfileError::InvalidHeader;
    return result;
  }
  if (ReadUInt32LittleEndian(bytes, checksum_offset) !=
      ComputeFnv1a(bytes.first(checksum_offset))) {
    result.error = RuntimeInputProfileError::InvalidChecksum;
    return result;
  }

  result.profile.source_schema_version = ReadInt32LittleEndian(bytes, 28);
  if (result.profile.source_schema_version <= 0) {
    result.error = RuntimeInputProfileError::InvalidSchema;
    return result;
  }
  result.profile.vietnamese_enabled =
      (flags & kVietnameseEnabledFlag) != 0;
  result.profile.speech_enabled = (flags & kSpeechEnabledFlag) != 0;
  result.profile.translation_enabled =
      (flags & kTranslationEnabledFlag) != 0;
  result.profile.traditional_tone_placement =
      (flags & kTraditionalTonePlacementFlag) != 0;
  result.profile.quick_telex_letters =
      (flags & kQuickTelexLettersFlag) != 0;
  result.profile.standalone_w_to_u_horn =
      (flags & kDisableStandaloneWFlag) == 0;
  result.profile.restore_invalid_word =
      (flags & kRestoreInvalidWordFlag) != 0;
  result.profile.clipboard_compatibility_enabled =
      (flags & kClipboardCompatibilityFlag) != 0;

  if (!legacy) {
    const auto overlay_flags = ByteAt(bytes, 26);
    if ((overlay_flags & ~kKnownOverlayFlags) != 0) {
      result.error = RuntimeInputProfileError::InvalidHeader;
      return result;
    }
    result.profile.keystroke_overlay.enabled =
        (overlay_flags & kOverlayEnabledFlag) != 0;
    result.profile.keystroke_overlay.motion =
        static_cast<KeystrokeOverlayMotionLevel>((overlay_flags >> 1u) & 0x03u);
    result.profile.keystroke_overlay.fallback_corner =
        static_cast<KeystrokeOverlayFallbackCorner>((overlay_flags >> 3u) & 0x03u);
    result.profile.keystroke_overlay.presentation_mode =
        (overlay_flags & kOverlayPresentationFlag) != 0;
    result.profile.keystroke_overlay.size_percent = ByteAt(bytes, 27);
    result.profile.keystroke_overlay.opacity_percent = ByteAt(bytes, 32);
    result.profile.keystroke_overlay.hide_delay_milliseconds =
        ReadUInt16LittleEndian(bytes, 33);
    result.profile.keystroke_overlay.per_key_sound_enabled =
        (ByteAt(bytes, 35) & 0x80u) != 0;
    result.profile.keystroke_overlay.sound_volume_percent =
        ByteAt(bytes, 35) & 0x7Fu;
    if (!result.profile.keystroke_overlay.IsValid()) {
      result.error = RuntimeInputProfileError::InvalidHeader;
      return result;
    }
  }

  result.error = DecodeBindings(bytes, result.profile);
  return result;
}

}  // namespace keyina::windows
