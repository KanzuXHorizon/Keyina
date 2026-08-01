#pragma once

#include <keyina/windows/keystroke_overlay_model.h>

#include <cstdint>

namespace keyina::windows {

struct OverlayRectangle {
  int left{};
  int top{};
  int right{};
  int bottom{};

  [[nodiscard]] bool operator==(const OverlayRectangle&) const noexcept =
      default;
};

enum class KeystrokeOverlayPlacementSource : std::uint8_t {
  Caret = 0,
  StableAnchor,
  FallbackCorner,
};

struct KeystrokeOverlayPlacementInput {
  bool caret_reliable{false};
  OverlayRectangle caret_bounds{};
  OverlayRectangle monitor_work_area{};
  int overlay_width{};
  int overlay_height{};
  int margin{8};
  int stability_threshold{10};
  std::uint64_t monitor_id{};
  bool has_last_stable_placement{false};
  OverlayRectangle last_stable_bounds{};
  std::uint64_t last_monitor_id{};
  KeystrokeOverlayFallbackCorner fallback_corner{
      KeystrokeOverlayFallbackCorner::BottomRight};
};

struct KeystrokeOverlayPlacement {
  OverlayRectangle bounds{};
  KeystrokeOverlayPlacementSource source{
      KeystrokeOverlayPlacementSource::FallbackCorner};
  std::uint64_t monitor_id{};
  bool placed_above{false};
  bool valid{false};
};

[[nodiscard]] KeystrokeOverlayPlacement ResolveKeystrokeOverlayPlacement(
    const KeystrokeOverlayPlacementInput& input) noexcept;

}  // namespace keyina::windows
