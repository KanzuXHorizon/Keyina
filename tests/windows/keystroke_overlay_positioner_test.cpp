#include <keyina/windows/keystroke_overlay_positioner.h>

#include "../test_support.h"

using namespace keyina::windows;

namespace {

KeystrokeOverlayPlacementInput BaseInput() {
  KeystrokeOverlayPlacementInput input{};
  input.monitor_work_area = {0, 0, 1920, 1080};
  input.caret = {900, 500, 902, 524};
  input.overlay_size = {240, 56};
  input.margin = 12;
  input.stability_threshold = 8;
  input.caret_reliable = true;
  return input;
}

}  // namespace

KEYINA_TEST(keystroke_overlay_positioner_prefers_below_caret) {
  const auto placement = ResolveKeystrokeOverlayPlacement(BaseInput());
  KEYINA_EXPECT_TRUE(!placement.used_fallback);
  KEYINA_EXPECT_TRUE(!placement.placed_above);
  KEYINA_EXPECT_EQ(placement.bounds.top, 536);
  KEYINA_EXPECT_TRUE(placement.bounds.left >= 0);
}

KEYINA_TEST(keystroke_overlay_positioner_uses_above_near_bottom) {
  auto input = BaseInput();
  input.caret = {900, 1030, 902, 1054};
  const auto placement = ResolveKeystrokeOverlayPlacement(input);
  KEYINA_EXPECT_TRUE(placement.placed_above);
  KEYINA_EXPECT_EQ(placement.bounds.bottom, 1018);
}

KEYINA_TEST(keystroke_overlay_positioner_supports_fallback_corners) {
  auto input = BaseInput();
  input.caret_reliable = false;

  input.fallback_corner = KeystrokeOverlayFallbackCorner::BottomRight;
  auto placement = ResolveKeystrokeOverlayPlacement(input);
  KEYINA_EXPECT_EQ(placement.bounds.right, 1908);
  KEYINA_EXPECT_EQ(placement.bounds.bottom, 1068);

  input.fallback_corner = KeystrokeOverlayFallbackCorner::TopLeft;
  placement = ResolveKeystrokeOverlayPlacement(input);
  KEYINA_EXPECT_EQ(placement.bounds.left, 12);
  KEYINA_EXPECT_EQ(placement.bounds.top, 12);
}

KEYINA_TEST(keystroke_overlay_positioner_clamps_long_overlay) {
  auto input = BaseInput();
  input.caret = {10, 10, 12, 34};
  input.overlay_size = {2500, 56};
  const auto placement = ResolveKeystrokeOverlayPlacement(input);
  KEYINA_EXPECT_EQ(placement.bounds.left, 12);
}

KEYINA_TEST(keystroke_overlay_positioner_reuses_stable_anchor) {
  auto input = BaseInput();
  const auto first = ResolveKeystrokeOverlayPlacement(input);
  input.has_last_stable_anchor = true;
  input.last_stable_anchor = first.stable_anchor;
  input.caret.left += 4;
  input.caret.right += 4;
  const auto stable = ResolveKeystrokeOverlayPlacement(input);
  KEYINA_EXPECT_EQ(stable.stable_anchor.x, first.stable_anchor.x);

  input.caret.left += 20;
  input.caret.right += 20;
  const auto moved = ResolveKeystrokeOverlayPlacement(input);
  KEYINA_EXPECT_TRUE(moved.stable_anchor.x != first.stable_anchor.x);
}

KEYINA_TEST(keystroke_overlay_positioner_discards_anchor_on_monitor_change) {
  auto input = BaseInput();
  input.has_last_stable_anchor = true;
  input.last_stable_anchor = {781, 536};
  input.monitor_changed = true;
  input.caret.left += 4;
  input.caret.right += 4;
  const auto placement = ResolveKeystrokeOverlayPlacement(input);
  KEYINA_EXPECT_TRUE(placement.stable_anchor.x != 781);
}
