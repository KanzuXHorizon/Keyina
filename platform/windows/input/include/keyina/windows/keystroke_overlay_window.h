#pragma once

#include <keyina/windows/keystroke_overlay_model.h>
#include <keyina/windows/keystroke_overlay_positioner.h>

#include <windows.h>

#include <cstdint>
#include <string>

struct ID2D1Factory;
struct ID2D1HwndRenderTarget;
struct ID2D1SolidColorBrush;
struct IDWriteFactory;
struct IDWriteTextFormat;

namespace keyina::windows {

class KeystrokeOverlayWindow {
 public:
  KeystrokeOverlayWindow() noexcept = default;
  ~KeystrokeOverlayWindow();

  KeystrokeOverlayWindow(const KeystrokeOverlayWindow&) = delete;
  KeystrokeOverlayWindow& operator=(const KeystrokeOverlayWindow&) = delete;

  [[nodiscard]] bool Initialize(HINSTANCE instance) noexcept;
  void Present(const KeystrokeOverlayState& state,
               const KeystrokeOverlayPlacement& placement,
               const KeystrokeOverlayMotionDecision& motion,
               const KeystrokeOverlayPreferences& preferences,
               std::uint32_t dpi) noexcept;
  void HideAndReleaseTransientState() noexcept;
  void Shutdown() noexcept;

  [[nodiscard]] bool IsVisibleForTesting() const noexcept;
  [[nodiscard]] bool HasActiveAnimationForTesting() const noexcept;
  [[nodiscard]] HWND window_for_testing() const noexcept { return window_; }
  [[nodiscard]] std::uint32_t CurrentDpiForTesting() const noexcept {
    return current_dpi_;
  }
  void SimulateDeviceLossForTesting() noexcept;

 private:
  static constexpr UINT_PTR kAnimationTimerId = 1;
  static constexpr UINT kAnimationTimerIntervalMs = 16;

  static LRESULT CALLBACK WindowProcedure(HWND window, UINT message,
                                           WPARAM w_param,
                                           LPARAM l_param) noexcept;
  LRESULT HandleMessage(UINT message, WPARAM w_param, LPARAM l_param) noexcept;
  [[nodiscard]] bool EnsureDeviceResources() noexcept;
  [[nodiscard]] bool EnsureTextFormat(std::uint32_t dpi) noexcept;
  void ReleaseDeviceResources() noexcept;
  void Render() noexcept;
  void TickAnimation() noexcept;
  void UpdateDisplayText(const KeystrokeOverlayState& state);
  void ApplyAlpha(std::uint8_t alpha) noexcept;

  HINSTANCE instance_{};
  HWND window_{};
  ID2D1Factory* d2d_factory_{};
  IDWriteFactory* dwrite_factory_{};
  ID2D1HwndRenderTarget* render_target_{};
  ID2D1SolidColorBrush* surface_brush_{};
  ID2D1SolidColorBrush* text_brush_{};
  ID2D1SolidColorBrush* accent_brush_{};
  IDWriteTextFormat* text_format_{};
  std::u16string display_text_{};
  KeystrokeOverlayMotionDecision motion_{};
  KeystrokeOverlayPreferences preferences_{};
  std::uint64_t animation_started_tick_{};
  std::uint32_t current_dpi_{96};
  float current_translation_y_{};
  std::uint8_t current_alpha_{};
  bool visible_{};
  bool animation_active_{};
  bool simulate_device_loss_{};
};

}  // namespace keyina::windows
