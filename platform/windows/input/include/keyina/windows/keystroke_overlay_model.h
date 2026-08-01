#pragma once

#include <array>
#include <chrono>
#include <cstddef>
#include <cstdint>
#include <string>
#include <string_view>

namespace keyina::windows {

inline constexpr std::size_t kMaximumOverlayTokens = 16;
inline constexpr std::size_t kMaximumOverlayCodeUnits = 64;

enum class KeystrokeOverlayEventKind : std::uint8_t {
  Token,
  CompositionUpdated,
  CompositionCommitted,
  Cleared,
  Suppressed,
};

enum class KeystrokeOverlayMotionLevel : std::uint8_t {
  Adaptive,
  Full,
  Reduced,
  Off,
};

enum class KeystrokeOverlayFallbackCorner : std::uint8_t {
  BottomRight,
  BottomLeft,
  TopRight,
  TopLeft,
};

struct KeystrokeOverlayPreferences {
  bool enabled{false};
  KeystrokeOverlayMotionLevel motion{KeystrokeOverlayMotionLevel::Adaptive};
  std::uint16_t size_percent{100};
  std::uint8_t opacity_percent{92};
  std::uint16_t hide_delay_milliseconds{900};
  KeystrokeOverlayFallbackCorner fallback_corner{
      KeystrokeOverlayFallbackCorner::BottomRight};
  bool presentation_mode{false};
  bool per_key_sound_enabled{false};
  std::uint8_t sound_volume_percent{30};

  [[nodiscard]] bool IsValid() const noexcept;
};

struct KeystrokeOverlayEvent {
  KeystrokeOverlayEventKind kind{KeystrokeOverlayEventKind::Cleared};
  std::array<char16_t, kMaximumOverlayCodeUnits> text{};
  std::size_t text_length{};
  char16_t token{};
  std::uint64_t generation{};
  bool text_truncated{};

  void SetText(std::u16string_view value) noexcept;
  [[nodiscard]] std::u16string_view Text() const noexcept {
    return {text.data(), text_length};
  }
};

struct KeystrokeOverlayState {
  std::array<char16_t, kMaximumOverlayTokens> tokens{};
  std::size_t token_count{};
  std::u16string text{};
  std::uint64_t generation{};
  KeystrokeOverlayEventKind transition{KeystrokeOverlayEventKind::Cleared};
  bool visible{};
  bool truncated{};
};

class KeystrokeOverlayReducer {
 public:
  [[nodiscard]] KeystrokeOverlayState Apply(
      const KeystrokeOverlayState& current,
      const KeystrokeOverlayEvent& event) const noexcept;
};

struct KeystrokeOverlayPrivacyContext {
  bool classification_known{};
  bool editable_text{};
  bool password{};
  bool protected_input{};
  bool secure_desktop{};
  bool excluded_application{};
};

enum class KeystrokeOverlayPrivacyDecision : std::uint8_t {
  Allow,
  Suppress,
};

[[nodiscard]] KeystrokeOverlayPrivacyDecision EvaluateKeystrokeOverlayPrivacy(
    const KeystrokeOverlayPrivacyContext& context) noexcept;

struct KeystrokeOverlayMotionContext {
  KeystrokeOverlayMotionLevel level{KeystrokeOverlayMotionLevel::Adaptive};
  bool rapid_input{};
  bool low_power{};
  bool system_reduced_motion{};
};

struct KeystrokeOverlayMotionDecision {
  std::chrono::milliseconds duration{};
  bool translate{};
  bool emphasize_changed_glyphs{};
};

[[nodiscard]] KeystrokeOverlayMotionDecision ResolveKeystrokeOverlayMotion(
    const KeystrokeOverlayMotionContext& context) noexcept;

}  // namespace keyina::windows
