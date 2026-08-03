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
  state.text.Assign(u"nguyễn");
  KeystrokeOverlayPlacement placement{};
  placement.bounds = {100, 100, 340, 156};
  const auto motion = ResolveKeystrokeOverlayMotion(
      {KeystrokeOverlayMotionLevel::Off, false, false, false});
  overlay.Present(state, placement, motion, {}, 144);

  KEYINA_EXPECT_TRUE(overlay.IsVisibleForTesting());
  KEYINA_EXPECT_EQ(overlay.CurrentDpiForTesting(), 144u);
  KEYINA_EXPECT_EQ(GetForegroundWindow(), foreground_before);
  overlay.HideAndReleaseTransientState();
  KEYINA_EXPECT_TRUE(!overlay.IsVisibleForTesting());
  KEYINA_EXPECT_TRUE(!overlay.HasActiveAnimationForTesting());
}

KEYINA_TEST(keystroke_overlay_window_applies_per_monitor_dpi_change) {
  KeystrokeOverlayWindow overlay;
  KEYINA_EXPECT_TRUE(overlay.Initialize(GetModuleHandleW(nullptr)));

  KeystrokeOverlayState state{};
  state.visible = true;
  state.text.Assign(u"nguyễn");
  KeystrokeOverlayPlacement placement{};
  placement.bounds = {100, 100, 340, 156};
  overlay.Present(
      state,
      placement,
      ResolveKeystrokeOverlayMotion(
          {KeystrokeOverlayMotionLevel::Off, false, false, false}),
      {},
      96);

  RECT suggested{200, 220, 560, 304};
  SendMessageW(
      overlay.window_for_testing(),
      WM_DPICHANGED,
      MAKEWPARAM(144, 144),
      reinterpret_cast<LPARAM>(&suggested));

  RECT actual{};
  GetWindowRect(overlay.window_for_testing(), &actual);
  KEYINA_EXPECT_EQ(overlay.CurrentDpiForTesting(), 144u);
  KEYINA_EXPECT_EQ(actual.left, suggested.left);
  KEYINA_EXPECT_EQ(actual.top, suggested.top);
  KEYINA_EXPECT_EQ(actual.right, suggested.right);
  KEYINA_EXPECT_EQ(actual.bottom, suggested.bottom);
}

KEYINA_TEST(keystroke_overlay_window_retargets_and_recovers_device_loss) {
  KeystrokeOverlayWindow overlay;
  KEYINA_EXPECT_TRUE(overlay.Initialize(GetModuleHandleW(nullptr)));

  KeystrokeOverlayState state{};
  state.visible = true;
  state.text.Assign(u"nguyên");
  KeystrokeOverlayPlacement placement{};
  placement.bounds = {100, 100, 340, 156};
  overlay.Present(state, placement,
                  ResolveKeystrokeOverlayMotion({}), {}, 96);
  KEYINA_EXPECT_TRUE(overlay.HasActiveAnimationForTesting());

  state.text.Assign(u"nguyễn");
  overlay.Present(state, placement,
                  ResolveKeystrokeOverlayMotion({}), {}, 192);
  KEYINA_EXPECT_EQ(overlay.CurrentDpiForTesting(), 192u);
  overlay.SimulateDeviceLossForTesting();
  KEYINA_EXPECT_TRUE(overlay.IsVisibleForTesting());

  overlay.HideAndReleaseTransientState();
  KEYINA_EXPECT_TRUE(!overlay.HasActiveAnimationForTesting());
}
