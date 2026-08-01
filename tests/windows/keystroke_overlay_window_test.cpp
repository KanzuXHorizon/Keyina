#include <keyina/windows/keystroke_overlay_window.h>

#include "../test_support.h"

#include <windows.h>

using namespace keyina::windows;

KEYINA_TEST(keystroke_overlay_window_is_no_activate_and_click_through) {
  const HWND foreground_before = GetForegroundWindow();
  KeystrokeOverlayWindow overlay;
  KEYINA_EXPECT_TRUE(overlay.Initialize(GetModuleHandleW(nullptr)));

  const LONG_PTR style = GetWindowLongPtrW(
      overlay.window_for_testing(), GWL_EXSTYLE);
  KEYINA_EXPECT_TRUE((style & WS_EX_NOACTIVATE) != 0);
  KEYINA_EXPECT_TRUE((style & WS_EX_TRANSPARENT) != 0);

  KeystrokeOverlayState state{};
  state.visible = true;
  state.text = u"nguyễn";
  KeystrokeOverlayPlacement placement{};
  placement.bounds = {100, 100, 340, 156};
  const auto motion = ResolveKeystrokeOverlayMotion(
      {KeystrokeOverlayMotionLevel::Off, false, false, false});
  overlay.Present(state, placement, motion, {});

  KEYINA_EXPECT_TRUE(overlay.IsVisibleForTesting());
  KEYINA_EXPECT_EQ(GetForegroundWindow(), foreground_before);
  overlay.HideAndReleaseTransientState();
  KEYINA_EXPECT_TRUE(!overlay.IsVisibleForTesting());
  KEYINA_EXPECT_TRUE(!overlay.HasActiveAnimationForTesting());
}

KEYINA_TEST(keystroke_overlay_window_retargets_and_recovers_device_loss) {
  KeystrokeOverlayWindow overlay;
  KEYINA_EXPECT_TRUE(overlay.Initialize(GetModuleHandleW(nullptr)));

  KeystrokeOverlayState state{};
  state.visible = true;
  state.text = u"nguyên";
  KeystrokeOverlayPlacement placement{};
  placement.bounds = {100, 100, 340, 156};
  overlay.Present(state, placement,
                  ResolveKeystrokeOverlayMotion({}), {});
  KEYINA_EXPECT_TRUE(overlay.HasActiveAnimationForTesting());

  state.text = u"nguyễn";
  overlay.Present(state, placement,
                  ResolveKeystrokeOverlayMotion({}), {});
  overlay.SimulateDeviceLossForTesting();
  KEYINA_EXPECT_TRUE(overlay.IsVisibleForTesting());

  overlay.HideAndReleaseTransientState();
  KEYINA_EXPECT_TRUE(!overlay.HasActiveAnimationForTesting());
}
