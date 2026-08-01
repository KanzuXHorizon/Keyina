#pragma once

#include <keyina/windows/keystroke_overlay_model.h>
#include <keyina/windows/keystroke_overlay_positioner.h>

#include <windows.h>

#include <array>
#include <cstddef>
#include <cstdint>

struct ID2D1Factory;
struct ID2D1HwndRenderTarget;
struct ID2D1SolidColorBrush;
struct IDWriteFactory;
struct IDWriteTextFormat;
struct IDWriteTextLayout;

namespace keyina::windows {

class KeystrokeOverlayWindow {
 public:
  KeystrokeOverlayWindow() noexcept = default;
  ~KeystrokeOverlayWindow();

  KeystrokeOverlayWindow(const KeystrokeOverlayWindow&) = delete;
  KeystrokeOverlayWindow& operator=(const KeystrokeOverlayWindow&) = delete;

  [[nodiscard]] bool Initialize(HINSTANCE instance) noexcept;
  void Present(
      const KeystrokeOverlayState& state,
      const KeystrokeOverlayPlacement& placement,
      const KeystrokeOverlayMotionDecision& motion) noexcept;
  void HideAndReleaseTransientState() noexcept;

  [[nodiscard]] bool IsVisibleForTesting() const noexcept;
  [[nodiscard]] bool HasActiveAnimationForTesting() const noexcept {
    return animation_timer_ != 0;
  }
  [[nodiscard]] HWND window_for_testing() const noexcept { return window_; }
  [[nodiscard]] std::uint64_t last_rendered_generation_for_testing()
      const noexcept {
    return last_rendered_generation_;
  }
  [[nodiscard]] std::uint64_t device_recovery_count_for_testing()
      const noexcept {
    return device_recovery_count_;
  }
  void SimulateDeviceLossForTesting() noexcept;

 private:
  static LRESULT CALLBACK WindowProcedure(
      HWND window,
      UINT message,
      WPARAM w_param,
      LPARAM l_param) noexcept;
  LRESULT HandleWindowMessage(
      HWND window,
      UINT message,
      WPARAM w_param,
      LPARAM l_param) noexcept;

  [[nodiscard]] bool EnsureDeviceIndependentResources() noexcept;
  [[nodiscard]] bool EnsureRenderTarget() noexcept;
  [[nodiscard]] bool RebuildTextLayout() noexcept;
  void DiscardRenderTarget() noexcept;
  void ReleaseDeviceIndependentResources() noexcept;
  void Render() noexcept;
  void UpdateAnimationFrame() noexcept;
  void StopAnimation() noexcept;
  void ApplyWindowFrame(float progress) noexcept;
  void RefreshTheme() noexcept;
  void ApplyRoundedRegion(int width, int height) noexcept;
  void BuildDisplayText() noexcept;

  HINSTANCE instance_{};
  HWND window_{};
  ID2D1Factory* d2d_factory_{};
  ID2D1HwndRenderTarget* render_target_{};
  ID2D1SolidColorBrush* background_brush_{};
  ID2D1SolidColorBrush* text_brush_{};
  IDWriteFactory* write_factory_{};
  IDWriteTextFormat* token_text_format_{};
  IDWriteTextFormat* composition_text_format_{};
  IDWriteTextLayout* text_layout_{};

  KeystrokeOverlayState latest_state_{};
  KeystrokeOverlayPlacement latest_placement_{};
  KeystrokeOverlayMotionDecision latest_motion_{};
  OverlayRectangle previous_window_bounds_{};
  std::array<wchar_t,
             (kMaximumOverlayTokens * kMaximumOverlayCodeUnits) +
                 (kMaximumOverlayTokens * 2)>
      display_text_{};
  std::size_t display_text_units_{};

  UINT_PTR animation_timer_{};
  ULONGLONG animation_started_at_{};
  float animation_from_opacity_{1.0F};
  float current_opacity_{1.0F};
  int animation_from_offset_x_{};
  int animation_from_offset_y_{};
  bool initialized_{false};
  bool visible_{false};
  bool force_device_loss_for_testing_{false};
  bool light_theme_{false};
  bool high_contrast_{false};
  std::uint64_t last_rendered_generation_{};
  std::uint64_t device_recovery_count_{};
};

}  // namespace keyina::windows
