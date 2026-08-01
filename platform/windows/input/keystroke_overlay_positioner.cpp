#include <keyina/windows/keystroke_overlay_positioner.h>

#include <algorithm>
#include <cstdint>
#include <cstdlib>

namespace keyina::windows {
namespace {

bool IsValidWorkArea(const OverlayRectangle& value) noexcept {
  return value.right > value.left && value.bottom > value.top;
}

bool IsValidCaret(const OverlayRectangle& value) noexcept {
  return value.right >= value.left && value.bottom >= value.top;
}

bool Intersects(
    const OverlayRectangle& left,
    const OverlayRectangle& right) noexcept {
  return left.right > right.left && left.left < right.right &&
      left.bottom > right.top && left.top < right.bottom;
}

int ClampCoordinate(
    std::int64_t value,
    int minimum,
    int maximum) noexcept {
  return static_cast<int>(std::clamp<std::int64_t>(
      value,
      static_cast<std::int64_t>(minimum),
      static_cast<std::int64_t>(maximum)));
}

OverlayRectangle BuildClampedBounds(
    std::int64_t proposed_left,
    std::int64_t proposed_top,
    int width,
    int height,
    const OverlayRectangle& work_area) noexcept {
  const int maximum_left = work_area.right - width;
  const int maximum_top = work_area.bottom - height;
  const int left = ClampCoordinate(
      proposed_left,
      work_area.left,
      maximum_left);
  const int top = ClampCoordinate(
      proposed_top,
      work_area.top,
      maximum_top);
  return {left, top, left + width, top + height};
}

KeystrokeOverlayPlacement BuildFallback(
    const KeystrokeOverlayPlacementInput& input,
    int width,
    int height,
    int margin) noexcept {
  const bool right_aligned =
      input.fallback_corner ==
          KeystrokeOverlayFallbackCorner::BottomRight ||
      input.fallback_corner ==
          KeystrokeOverlayFallbackCorner::TopRight;
  const bool bottom_aligned =
      input.fallback_corner ==
          KeystrokeOverlayFallbackCorner::BottomRight ||
      input.fallback_corner ==
          KeystrokeOverlayFallbackCorner::BottomLeft;

  const std::int64_t left = right_aligned
      ? static_cast<std::int64_t>(input.monitor_work_area.right) - margin -
          width
      : static_cast<std::int64_t>(input.monitor_work_area.left) + margin;
  const std::int64_t top = bottom_aligned
      ? static_cast<std::int64_t>(input.monitor_work_area.bottom) - margin -
          height
      : static_cast<std::int64_t>(input.monitor_work_area.top) + margin;

  return {
      BuildClampedBounds(
          left,
          top,
          width,
          height,
          input.monitor_work_area),
      KeystrokeOverlayPlacementSource::FallbackCorner,
      input.monitor_id,
      false,
      true,
  };
}

}  // namespace

KeystrokeOverlayPlacement ResolveKeystrokeOverlayPlacement(
    const KeystrokeOverlayPlacementInput& input) noexcept {
  if (!IsValidWorkArea(input.monitor_work_area) ||
      input.overlay_width <= 0 || input.overlay_height <= 0) {
    return {};
  }

  const std::int64_t work_width =
      static_cast<std::int64_t>(input.monitor_work_area.right) -
      input.monitor_work_area.left;
  const std::int64_t work_height =
      static_cast<std::int64_t>(input.monitor_work_area.bottom) -
      input.monitor_work_area.top;
  const int width = static_cast<int>(std::min<std::int64_t>(
      input.overlay_width,
      work_width));
  const int height = static_cast<int>(std::min<std::int64_t>(
      input.overlay_height,
      work_height));
  const int margin = std::max(input.margin, 0);
  const int threshold = std::max(input.stability_threshold, 0);

  const bool stable_anchor_available =
      input.has_last_stable_placement &&
      input.last_monitor_id == input.monitor_id &&
      Intersects(input.last_stable_bounds, input.monitor_work_area);

  if (input.caret_reliable && IsValidCaret(input.caret_bounds)) {
    const std::int64_t below_top =
        static_cast<std::int64_t>(input.caret_bounds.bottom) + margin;
    const std::int64_t above_top =
        static_cast<std::int64_t>(input.caret_bounds.top) - margin - height;
    const bool below_fits =
        below_top + height <= input.monitor_work_area.bottom;
    const bool above_fits = above_top >= input.monitor_work_area.top;

    bool placed_above = false;
    std::int64_t proposed_top = below_top;
    if (!below_fits && above_fits) {
      proposed_top = above_top;
      placed_above = true;
    } else if (!below_fits && !above_fits) {
      const std::int64_t below_space =
          static_cast<std::int64_t>(input.monitor_work_area.bottom) -
          input.caret_bounds.bottom;
      const std::int64_t above_space =
          static_cast<std::int64_t>(input.caret_bounds.top) -
          input.monitor_work_area.top;
      placed_above = above_space > below_space;
      proposed_top = placed_above ? above_top : below_top;
    }

    const OverlayRectangle proposed = BuildClampedBounds(
        input.caret_bounds.left,
        proposed_top,
        width,
        height,
        input.monitor_work_area);
    if (stable_anchor_available) {
      const std::int64_t delta_x = std::llabs(
          static_cast<std::int64_t>(proposed.left) -
          input.last_stable_bounds.left);
      const std::int64_t delta_y = std::llabs(
          static_cast<std::int64_t>(proposed.top) -
          input.last_stable_bounds.top);
      if (delta_x <= threshold && delta_y <= threshold) {
        return {
            BuildClampedBounds(
                input.last_stable_bounds.left,
                input.last_stable_bounds.top,
                width,
                height,
                input.monitor_work_area),
            KeystrokeOverlayPlacementSource::StableAnchor,
            input.monitor_id,
            placed_above,
            true,
        };
      }
    }
    return {
        proposed,
        KeystrokeOverlayPlacementSource::Caret,
        input.monitor_id,
        placed_above,
        true,
    };
  }

  if (stable_anchor_available) {
    return {
        BuildClampedBounds(
            input.last_stable_bounds.left,
            input.last_stable_bounds.top,
            width,
            height,
            input.monitor_work_area),
        KeystrokeOverlayPlacementSource::StableAnchor,
        input.monitor_id,
        false,
        true,
    };
  }

  return BuildFallback(input, width, height, margin);
}

}  // namespace keyina::windows
