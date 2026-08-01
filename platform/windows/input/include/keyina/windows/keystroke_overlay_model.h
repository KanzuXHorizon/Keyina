#pragma once

#include <array>
#include <cstddef>
#include <cstdint>
#include <string_view>

namespace keyina::windows {

inline constexpr std::size_t kMaximumOverlayTokens = 16;
inline constexpr std::size_t kMaximumOverlayCodeUnits = 64;

class BoundedKeystrokeOverlayText {
 public:
  void assign(std::u16string_view text) noexcept;
  void clear() noexcept;

  [[nodiscard]] std::size_t size() const noexcept { return size_; }
  [[nodiscard]] bool empty() const noexcept { return size_ == 0; }
  [[nodiscard]] bool truncated() const noexcept { return truncated_; }
  [[nodiscard]] std::u16string_view view() const noexcept {
    return {storage_.data(), size_};
  }

 private:
  std::array<char16_t, kMaximumOverlayCodeUnits> storage_{};
  std::uint8_t size_{};
  bool truncated_{false};
};

enum class KeystrokeOverlayEventKind : std::uint8_t {
  Token = 0,
  CompositionUpdated,
  CompositionCommitted,
  Cleared,
  Suppressed,
};

enum class KeystrokeOverlayMotionLevel : std::uint8_t {
  Adaptive = 0,
  Full,
  Reduced,
  Off,
};

enum class KeystrokeOverlayFallbackCorner : std::uint8_t {
  BottomRight = 0,
  BottomLeft,
  TopRight,
  TopLeft,
};

struct KeystrokeOverlayPreferences {
  bool enabled{false};
  KeystrokeOverlayMotionLevel motion{
      KeystrokeOverlayMotionLevel::Adaptive};
  std::uint16_t size_percent{100};
  std::uint16_t opacity_percent{100};
  std::uint16_t hide_delay_milliseconds{900};
  KeystrokeOverlayFallbackCorner fallback_corner{
      KeystrokeOverlayFallbackCorner::BottomRight};
  bool presentation_mode{false};
};

struct KeystrokeOverlayEvent {
  KeystrokeOverlayEventKind kind{KeystrokeOverlayEventKind::Cleared};
  std::uint64_t generation{};
  BoundedKeystrokeOverlayText text{};
};

struct KeystrokeOverlayState {
  std::uint64_t generation{};
  KeystrokeOverlayEventKind last_event{
      KeystrokeOverlayEventKind::Cleared};
  BoundedKeystrokeOverlayText text{};
  std::array<BoundedKeystrokeOverlayText, kMaximumOverlayTokens> tokens{};
  std::size_t token_count{};
  bool visible{false};
  bool suppressed{false};
  bool truncated{false};
};

class KeystrokeOverlayReducer {
 public:
  [[nodiscard]] KeystrokeOverlayState Apply(
      const KeystrokeOverlayState& current,
      const KeystrokeOverlayEvent& event) const noexcept;
};

}  // namespace keyina::windows
