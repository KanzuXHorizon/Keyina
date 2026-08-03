#pragma once

#include <array>
#include <chrono>
#include <cstddef>
#include <cstdint>
#include <string_view>

namespace keyina::windows {

inline constexpr std::size_t kMaximumOverlayTokens = 16;
inline constexpr std::size_t kMaximumOverlayCodeUnits = 64;

class BoundedKeystrokeOverlayText {
 public:
  void Assign(
      std::u16string_view value,
      bool force_truncated = false) noexcept;
  void Clear() noexcept;
  [[nodiscard]] bool Append(char16_t value) noexcept;
  void EraseLast(std::size_t count) noexcept;

  [[nodiscard]] std::u16string_view View() const noexcept {
    return {storage_.data(), size_};
  }
  [[nodiscard]] std::size_t size() const noexcept { return size_; }
  [[nodiscard]] bool empty() const noexcept { return size_ == 0; }
  [[nodiscard]] bool truncated() const noexcept { return truncated_; }

  friend bool operator==(const BoundedKeystrokeOverlayText& left,
                         const BoundedKeystrokeOverlayText& right) noexcept {
    return left.View() == right.View() &&
        left.truncated_ == right.truncated_;
  }

 private:
  std::array<char16_t, kMaximumOverlayCodeUnits> storage_{};
  std::uint8_t size_{};
  bool truncated_{};
};

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
  BottomCenter,
  TopCenter,
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

  friend bool operator==(const KeystrokeOverlayPreferences&,
                         const KeystrokeOverlayPreferences&) = default;
};

struct KeystrokeOverlayEvent {
  KeystrokeOverlayEventKind kind{KeystrokeOverlayEventKind::Cleared};
  BoundedKeystrokeOverlayText text{};
  char16_t token{};
  std::uint64_t generation{};

  void SetText(std::u16string_view value) noexcept {
    text.Assign(value);
  }
  [[nodiscard]] std::u16string_view Text() const noexcept {
    return text.View();
  }
};

struct KeystrokeOverlayState {
  std::array<char16_t, kMaximumOverlayTokens> tokens{};
  BoundedKeystrokeOverlayText text{};
  std::size_t token_count{};
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

[[nodiscard]] bool ShouldShowKeystrokeOverlayCompositionText(
    std::u16string_view composition,
    bool transformed) noexcept;

[[nodiscard]] bool ShouldClearKeystrokeOverlayComposition(
    std::uint16_t virtual_key,
    bool control,
    bool alt,
    bool windows) noexcept;

}  // namespace keyina::windows
