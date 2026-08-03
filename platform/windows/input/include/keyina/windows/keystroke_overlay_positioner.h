#pragma once

#include <keyina/windows/keystroke_overlay_model.h>

namespace keyina::windows {

struct OverlayRectangle {
  int left{};
  int top{};
  int right{};
  int bottom{};

  [[nodiscard]] int Width() const noexcept { return right - left; }
  [[nodiscard]] int Height() const noexcept { return bottom - top; }
  [[nodiscard]] bool IsValid() const noexcept {
    return right > left && bottom > top;
  }
};

struct OverlaySize {
  int width{};
  int height{};
};

struct OverlayPoint {
  int x{};
  int y{};
};

struct KeystrokeOverlayPlacementInput {
  OverlayRectangle monitor_work_area{};
  OverlayRectangle caret{};
  OverlayPoint last_stable_anchor{};
  OverlaySize overlay_size{};
  KeystrokeOverlayFallbackCorner fallback_corner{
      KeystrokeOverlayFallbackCorner::BottomRight};
  int margin{12};
  int stability_threshold{8};
  bool caret_reliable{};
  bool has_last_stable_anchor{};
  bool monitor_changed{};
};

struct KeystrokeOverlayPlacement {
  OverlayRectangle bounds{};
  OverlayPoint stable_anchor{};
  bool used_fallback{};
  bool placed_above{};
};

[[nodiscard]] int ScaleKeystrokeOverlayMetric(
    int value_at_96_dpi,
    std::uint32_t dpi) noexcept;

[[nodiscard]] bool DidKeystrokeOverlayMonitorChange(
    std::uintptr_t previous_monitor,
    std::uintptr_t current_monitor) noexcept;

[[nodiscard]] KeystrokeOverlayPlacement ResolveKeystrokeOverlayPlacement(
    const KeystrokeOverlayPlacementInput& input) noexcept;

}  // namespace keyina::windows
