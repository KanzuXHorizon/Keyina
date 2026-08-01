#include <keyina/windows/keystroke_overlay_positioner.h>

#include <algorithm>
#include <cstdlib>

namespace keyina::windows {
namespace {

int ClampAxis(int value, int minimum, int maximum) noexcept {
  if (maximum < minimum) {
    return minimum;
  }
  return std::clamp(value, minimum, maximum);
}

OverlayPoint ResolveFallbackAnchor(
    const KeystrokeOverlayPlacementInput& input) noexcept {
  const auto& area = input.monitor_work_area;
  const int left = area.left + input.margin;
  const int right = area.right - input.margin - input.overlay_size.width;
  const int top = area.top + input.margin;
  const int bottom = area.bottom - input.margin - input.overlay_size.height;

  switch (input.fallback_corner) {
    case KeystrokeOverlayFallbackCorner::BottomLeft:
      return {left, bottom};
    case KeystrokeOverlayFallbackCorner::TopRight:
      return {right, top};
    case KeystrokeOverlayFallbackCorner::TopLeft:
      return {left, top};
    case KeystrokeOverlayFallbackCorner::BottomRight:
    default:
      return {right, bottom};
  }
}

bool IsNear(OverlayPoint first, OverlayPoint second, int threshold) noexcept {
  return std::abs(first.x - second.x) <= threshold &&
         std::abs(first.y - second.y) <= threshold;
}

}  // namespace

KeystrokeOverlayPlacement ResolveKeystrokeOverlayPlacement(
    const KeystrokeOverlayPlacementInput& input) noexcept {
  KeystrokeOverlayPlacement placement{};
  if (!input.monitor_work_area.IsValid() || input.overlay_size.width <= 0 ||
      input.overlay_size.height <= 0) {
    return placement;
  }

  OverlayPoint anchor{};
  bool placed_above = false;
  bool fallback = !input.caret_reliable || !input.caret.IsValid();

  if (fallback) {
    anchor = ResolveFallbackAnchor(input);
  } else {
    const int centered_x = input.caret.left +
                           (input.caret.Width() - input.overlay_size.width) / 2;
    const int below_y = input.caret.bottom + input.margin;
    const int above_y = input.caret.top - input.margin - input.overlay_size.height;
    const bool fits_below = below_y + input.overlay_size.height <=
                            input.monitor_work_area.bottom - input.margin;
    const bool fits_above = above_y >= input.monitor_work_area.top + input.margin;

    anchor.x = centered_x;
    if (fits_below) {
      anchor.y = below_y;
    } else if (fits_above) {
      anchor.y = above_y;
      placed_above = true;
    } else {
      anchor.y = below_y;
    }

    if (input.has_last_stable_anchor && !input.monitor_changed &&
        IsNear(anchor, input.last_stable_anchor, input.stability_threshold)) {
      anchor = input.last_stable_anchor;
    }
  }

  const int minimum_x = input.monitor_work_area.left + input.margin;
  const int maximum_x = input.monitor_work_area.right - input.margin -
                        input.overlay_size.width;
  const int minimum_y = input.monitor_work_area.top + input.margin;
  const int maximum_y = input.monitor_work_area.bottom - input.margin -
                        input.overlay_size.height;
  anchor.x = ClampAxis(anchor.x, minimum_x, maximum_x);
  anchor.y = ClampAxis(anchor.y, minimum_y, maximum_y);

  placement.bounds = {
      anchor.x,
      anchor.y,
      anchor.x + input.overlay_size.width,
      anchor.y + input.overlay_size.height};
  placement.stable_anchor = anchor;
  placement.used_fallback = fallback;
  placement.placed_above = placed_above;
  return placement;
}

}  // namespace keyina::windows
