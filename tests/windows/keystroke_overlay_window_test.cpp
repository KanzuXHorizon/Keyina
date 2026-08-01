#include <keyina/windows/keystroke_overlay_window.h>

#include "../test_support.h"

#include <chrono>

namespace {

void PumpMessagesFor(DWORD duration_milliseconds) noexcept {
  const ULONGLONG deadline = GetTickCount64() + duration_milliseconds;
  do {
    MSG message{};
    while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE) != FALSE) {
      TranslateMessage(&message);
      DispatchMessageW(&message);
    }
    Sleep(1);
  } while (GetTickCount64() < deadline);
}

keyina::windows::KeystrokeOverlayState VisibleState(
    std::uint64_t generation) noexcept {
  keyina::windows::KeystrokeOverlayState state{};
  state.generation = generation;
  state.last_event =
      keyina::windows::KeystrokeOverlayEventKind::CompositionUpdated;
  state.text.assign(u"nguyễn");
  state.visible = true;
  return state;
}

keyina::windows::KeystrokeOverlayPlacement Placement() noexcept {
  keyina::windows::KeystrokeOverlayPlacement placement{};
  placement.bounds = {80, 80, 320, 132};
  placement.source =
      keyina::windows::KeystrokeOverlayPlacementSource::FallbackCorner;
  placement.monitor_id = 1;
  placement.valid = true;
  return placement;
}

}  // namespace

KEYINA_TEST(keystroke_overlay_window_is_no_activate_and_input_transparent) {
  const HWND foreground_before = GetForegroundWindow();
  keyina::windows::KeystrokeOverlayWindow window;

  KEYINA_EXPECT_TRUE(window.Initialize(GetModuleHandleW(nullptr)));
  const HWND handle = window.window_for_testing();
  KEYINA_EXPECT_TRUE(handle != nullptr);
  const LONG_PTR style = GetWindowLongPtrW(handle, GWL_EXSTYLE);
  KEYINA_EXPECT_TRUE((style & WS_EX_NOACTIVATE) != 0);
  KEYINA_EXPECT_TRUE((style & WS_EX_TRANSPARENT) != 0);
  KEYINA_EXPECT_TRUE((style & WS_EX_TOOLWINDOW) != 0);
  KEYINA_EXPECT_EQ(GetForegroundWindow(), foreground_before);
}

KEYINA_TEST(keystroke_overlay_window_present_never_steals_foreground) {
  const HWND foreground_before = GetForegroundWindow();
  keyina::windows::KeystrokeOverlayWindow window;
  KEYINA_EXPECT_TRUE(window.Initialize(GetModuleHandleW(nullptr)));

  window.Present(
      VisibleState(4),
      Placement(),
      keyina::windows::KeystrokeOverlayMotionDecision{
          std::chrono::milliseconds{0}, false, false});
  PumpMessagesFor(20);

  KEYINA_EXPECT_TRUE(window.IsVisibleForTesting());
  KEYINA_EXPECT_EQ(window.last_rendered_generation_for_testing(),
                   std::uint64_t{4});
  KEYINA_EXPECT_EQ(GetForegroundWindow(), foreground_before);
}

KEYINA_TEST(keystroke_overlay_window_animation_stops_and_hide_is_idle) {
  keyina::windows::KeystrokeOverlayWindow window;
  KEYINA_EXPECT_TRUE(window.Initialize(GetModuleHandleW(nullptr)));

  window.Present(
      VisibleState(5),
      Placement(),
      keyina::windows::KeystrokeOverlayMotionDecision{
          std::chrono::milliseconds{40}, true, true});
  KEYINA_EXPECT_TRUE(window.HasActiveAnimationForTesting());
  PumpMessagesFor(80);
  KEYINA_EXPECT_TRUE(!window.HasActiveAnimationForTesting());

  window.Present(
      VisibleState(6),
      Placement(),
      keyina::windows::KeystrokeOverlayMotionDecision{
          std::chrono::milliseconds{100}, true, true});
  KEYINA_EXPECT_TRUE(window.HasActiveAnimationForTesting());
  window.HideAndReleaseTransientState();

  KEYINA_EXPECT_TRUE(!window.IsVisibleForTesting());
  KEYINA_EXPECT_TRUE(!window.HasActiveAnimationForTesting());
}

KEYINA_TEST(keystroke_overlay_window_retargets_to_newest_generation) {
  const HWND foreground_before = GetForegroundWindow();
  keyina::windows::KeystrokeOverlayWindow window;
  KEYINA_EXPECT_TRUE(window.Initialize(GetModuleHandleW(nullptr)));

  window.Present(
      VisibleState(7),
      Placement(),
      keyina::windows::KeystrokeOverlayMotionDecision{
          std::chrono::milliseconds{100}, true, true});
  auto moved = Placement();
  moved.bounds = {84, 84, 324, 136};
  window.Present(
      VisibleState(8),
      moved,
      keyina::windows::KeystrokeOverlayMotionDecision{
          std::chrono::milliseconds{40}, true, true});
  PumpMessagesFor(80);

  KEYINA_EXPECT_EQ(window.last_rendered_generation_for_testing(),
                   std::uint64_t{8});
  KEYINA_EXPECT_TRUE(!window.HasActiveAnimationForTesting());
  KEYINA_EXPECT_EQ(GetForegroundWindow(), foreground_before);
}

KEYINA_TEST(keystroke_overlay_window_recovers_latest_state_after_device_loss) {
  keyina::windows::KeystrokeOverlayWindow window;
  KEYINA_EXPECT_TRUE(window.Initialize(GetModuleHandleW(nullptr)));
  window.Present(
      VisibleState(9),
      Placement(),
      keyina::windows::KeystrokeOverlayMotionDecision{
          std::chrono::milliseconds{0}, false, false});
  const auto recoveries_before =
      window.device_recovery_count_for_testing();

  window.SimulateDeviceLossForTesting();
  PumpMessagesFor(20);

  KEYINA_EXPECT_TRUE(
      window.device_recovery_count_for_testing() > recoveries_before);
  KEYINA_EXPECT_EQ(window.last_rendered_generation_for_testing(),
                   std::uint64_t{9});
  KEYINA_EXPECT_TRUE(window.IsVisibleForTesting());
}
