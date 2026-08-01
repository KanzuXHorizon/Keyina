#include <keyina/windows/keystroke_overlay_positioner.h>

#include "../test_support.h"

namespace {

keyina::windows::KeystrokeOverlayPlacementInput DefaultInput() noexcept {
  keyina::windows::KeystrokeOverlayPlacementInput input{};
  input.caret_reliable = true;
  input.caret_bounds = {100, 100, 102, 120};
  input.monitor_work_area = {0, 0, 1000, 800};
  input.overlay_width = 200;
  input.overlay_height = 50;
  input.margin = 8;
  input.stability_threshold = 10;
  input.monitor_id = 1;
  input.fallback_corner =
      keyina::windows::KeystrokeOverlayFallbackCorner::BottomRight;
  return input;
}

}  // namespace

KEYINA_TEST(keystroke_overlay_positioner_prefers_below_the_caret) {
  const auto placement = keyina::windows::ResolveKeystrokeOverlayPlacement(
      DefaultInput());

  KEYINA_EXPECT_TRUE(placement.valid);
  KEYINA_EXPECT_EQ(
      placement.source,
      keyina::windows::KeystrokeOverlayPlacementSource::Caret);
  KEYINA_EXPECT_TRUE(!placement.placed_above);
  KEYINA_EXPECT_EQ(
      placement.bounds,
      (keyina::windows::OverlayRectangle{100, 128, 300, 178}));
}

KEYINA_TEST(keystroke_overlay_positioner_moves_above_when_below_does_not_fit) {
  auto input = DefaultInput();
  input.caret_bounds = {300, 760, 302, 780};

  const auto placement =
      keyina::windows::ResolveKeystrokeOverlayPlacement(input);

  KEYINA_EXPECT_TRUE(placement.valid);
  KEYINA_EXPECT_TRUE(placement.placed_above);
  KEYINA_EXPECT_EQ(
      placement.bounds,
      (keyina::windows::OverlayRectangle{300, 702, 500, 752}));
}

KEYINA_TEST(keystroke_overlay_positioner_supports_all_fallback_corners) {
  auto input = DefaultInput();
  input.caret_reliable = false;
  input.monitor_work_area = {10, 20, 1010, 820};
  input.margin = 12;

  input.fallback_corner =
      keyina::windows::KeystrokeOverlayFallbackCorner::BottomRight;
  KEYINA_EXPECT_EQ(
      keyina::windows::ResolveKeystrokeOverlayPlacement(input).bounds,
      (keyina::windows::OverlayRectangle{798, 758, 998, 808}));

  input.fallback_corner =
      keyina::windows::KeystrokeOverlayFallbackCorner::BottomLeft;
  KEYINA_EXPECT_EQ(
      keyina::windows::ResolveKeystrokeOverlayPlacement(input).bounds,
      (keyina::windows::OverlayRectangle{22, 758, 222, 808}));

  input.fallback_corner =
      keyina::windows::KeystrokeOverlayFallbackCorner::TopRight;
  KEYINA_EXPECT_EQ(
      keyina::windows::ResolveKeystrokeOverlayPlacement(input).bounds,
      (keyina::windows::OverlayRectangle{798, 32, 998, 82}));

  input.fallback_corner =
      keyina::windows::KeystrokeOverlayFallbackCorner::TopLeft;
  KEYINA_EXPECT_EQ(
      keyina::windows::ResolveKeystrokeOverlayPlacement(input).bounds,
      (keyina::windows::OverlayRectangle{22, 32, 222, 82}));
}

KEYINA_TEST(keystroke_overlay_positioner_clamps_oversized_overlay_to_monitor) {
  auto input = DefaultInput();
  input.caret_bounds = {900, 300, 902, 320};
  input.overlay_width = 1200;

  const auto placement =
      keyina::windows::ResolveKeystrokeOverlayPlacement(input);

  KEYINA_EXPECT_TRUE(placement.valid);
  KEYINA_EXPECT_EQ(placement.bounds.left, 0);
  KEYINA_EXPECT_EQ(placement.bounds.right, 1000);
  KEYINA_EXPECT_TRUE(placement.bounds.top >= 0);
  KEYINA_EXPECT_TRUE(placement.bounds.bottom <= 800);
}

KEYINA_TEST(keystroke_overlay_positioner_keeps_stable_anchor_below_threshold) {
  auto input = DefaultInput();
  input.caret_bounds = {104, 102, 106, 122};
  input.has_last_stable_placement = true;
  input.last_stable_bounds = {100, 128, 300, 178};
  input.last_monitor_id = input.monitor_id;

  const auto placement =
      keyina::windows::ResolveKeystrokeOverlayPlacement(input);

  KEYINA_EXPECT_EQ(
      placement.source,
      keyina::windows::KeystrokeOverlayPlacementSource::StableAnchor);
  KEYINA_EXPECT_EQ(placement.bounds, input.last_stable_bounds);
}

KEYINA_TEST(keystroke_overlay_positioner_retargets_beyond_threshold) {
  auto input = DefaultInput();
  input.caret_bounds = {160, 170, 162, 190};
  input.has_last_stable_placement = true;
  input.last_stable_bounds = {100, 128, 300, 178};
  input.last_monitor_id = input.monitor_id;

  const auto placement =
      keyina::windows::ResolveKeystrokeOverlayPlacement(input);

  KEYINA_EXPECT_EQ(
      placement.source,
      keyina::windows::KeystrokeOverlayPlacementSource::Caret);
  KEYINA_EXPECT_EQ(
      placement.bounds,
      (keyina::windows::OverlayRectangle{160, 198, 360, 248}));
}

KEYINA_TEST(keystroke_overlay_positioner_drops_stable_anchor_after_monitor_change) {
  auto input = DefaultInput();
  input.has_last_stable_placement = true;
  input.last_stable_bounds = {600, 600, 800, 650};
  input.last_monitor_id = 2;

  const auto placement =
      keyina::windows::ResolveKeystrokeOverlayPlacement(input);

  KEYINA_EXPECT_EQ(
      placement.source,
      keyina::windows::KeystrokeOverlayPlacementSource::Caret);
  KEYINA_EXPECT_EQ(
      placement.bounds,
      (keyina::windows::OverlayRectangle{100, 128, 300, 178}));
}

KEYINA_TEST(keystroke_overlay_positioner_uses_last_anchor_without_caret) {
  auto input = DefaultInput();
  input.caret_reliable = false;
  input.has_last_stable_placement = true;
  input.last_stable_bounds = {700, 600, 900, 650};
  input.last_monitor_id = input.monitor_id;

  const auto placement =
      keyina::windows::ResolveKeystrokeOverlayPlacement(input);

  KEYINA_EXPECT_EQ(
      placement.source,
      keyina::windows::KeystrokeOverlayPlacementSource::StableAnchor);
  KEYINA_EXPECT_EQ(placement.bounds, input.last_stable_bounds);
}

KEYINA_TEST(keystroke_overlay_positioner_rejects_invalid_work_area) {
  auto input = DefaultInput();
  input.monitor_work_area = {100, 100, 100, 200};

  const auto placement =
      keyina::windows::ResolveKeystrokeOverlayPlacement(input);

  KEYINA_EXPECT_TRUE(!placement.valid);
}
