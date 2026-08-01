#pragma once

#include <keyina/windows/keystroke_overlay_model.h>

#include <array>
#include <cstddef>
#include <cstdint>
#include <span>

namespace keyina::windows {

inline constexpr std::size_t kLegacyRuntimeInputProfileSize = 36;
inline constexpr std::size_t kRuntimeInputProfileSize = 40;
inline constexpr std::size_t kRuntimeHotkeyCount = 6;

enum class RuntimeHotkeyGesture : std::uint8_t {
  Press = 0,
  Hold = 1,
  ModifierGesture = 2,
};

enum class RuntimeInputProfileError : std::uint8_t {
  None = 0,
  InvalidLength,
  InvalidMagic,
  UnsupportedVersion,
  InvalidHeader,
  InvalidChecksum,
  InvalidSchema,
  InvalidHotkey,
};

struct RuntimeHotkeyBinding {
  RuntimeHotkeyGesture gesture{RuntimeHotkeyGesture::Press};
  std::uint8_t modifiers{};
  std::uint8_t virtual_key{};

  friend bool operator==(const RuntimeHotkeyBinding&,
                         const RuntimeHotkeyBinding&) = default;
};

struct RuntimeInputProfile {
  bool vietnamese_enabled{true};
  bool speech_enabled{false};
  bool translation_enabled{false};
  bool traditional_tone_placement{false};
  bool quick_telex_letters{false};
  bool standalone_w_to_u_horn{true};
  bool restore_invalid_word{false};
  bool clipboard_compatibility_enabled{false};
  std::int32_t source_schema_version{1};
  std::array<RuntimeHotkeyBinding, kRuntimeHotkeyCount> hotkeys{};
  KeystrokeOverlayPreferences keystroke_overlay{};
};

struct RuntimeInputProfileResult {
  RuntimeInputProfile profile{};
  RuntimeInputProfileError error{RuntimeInputProfileError::None};

  [[nodiscard]] explicit operator bool() const noexcept {
    return error == RuntimeInputProfileError::None;
  }
};

[[nodiscard]] RuntimeInputProfile DefaultRuntimeInputProfile() noexcept;
[[nodiscard]] RuntimeInputProfileResult DecodeRuntimeInputProfile(
    std::span<const std::byte> bytes) noexcept;

}  // namespace keyina::windows
