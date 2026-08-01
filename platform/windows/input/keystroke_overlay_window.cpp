#include <keyina/windows/keystroke_overlay_window.h>

#include <d2d1.h>
#include <dwrite.h>

#include <algorithm>
#include <cmath>
#include <utility>

namespace keyina::windows {
namespace {

constexpr wchar_t kOverlayWindowClass[] = L"KeyinaKeystrokeOverlayWindow";

void SafeRelease(IUnknown*& value) noexcept {
  if (value != nullptr) {
    value->Release();
    value = nullptr;
  }
}

template <typename T>
void SafeReleaseTyped(T*& value) noexcept {
  IUnknown* unknown = value;
  SafeRelease(unknown);
  value = nullptr;
}

D2D1_COLOR_F SurfaceColor() noexcept {
  return D2D1::ColorF(0.075F, 0.082F, 0.102F, 0.96F);
}

D2D1_COLOR_F TextColor() noexcept {
  return D2D1::ColorF(0.96F, 0.97F, 0.99F, 1.0F);
}

D2D1_COLOR_F AccentColor() noexcept {
  return D2D1::ColorF(0.43F, 0.68F, 1.0F, 1.0F);
}

}  // namespace

KeystrokeOverlayWindow::~KeystrokeOverlayWindow() {
  Shutdown();
}

bool KeystrokeOverlayWindow::Initialize(HINSTANCE instance) noexcept {
  if (window_ != nullptr) {
    return true;
  }
  instance_ = instance;

  WNDCLASSEXW window_class{};
  window_class.cbSize = sizeof(window_class);
  window_class.style = CS_HREDRAW | CS_VREDRAW;
  window_class.lpfnWndProc = &KeystrokeOverlayWindow::WindowProcedure;
  window_class.hInstance = instance_;
  window_class.hCursor = LoadCursorW(nullptr, IDC_ARROW);
  window_class.lpszClassName = kOverlayWindowClass;
  if (RegisterClassExW(&window_class) == 0 &&
      GetLastError() != ERROR_CLASS_ALREADY_EXISTS) {
    return false;
  }

  window_ = CreateWindowExW(
      WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW |
          WS_EX_TOPMOST | WS_EX_LAYERED,
      kOverlayWindowClass,
      L"",
      WS_POPUP,
      0,
      0,
      1,
      1,
      nullptr,
      nullptr,
      instance_,
      this);
  if (window_ == nullptr) {
    return false;
  }

  if (FAILED(D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED,
                               &d2d_factory_))) {
    d2d_factory_ = nullptr;
  }
  if (FAILED(DWriteCreateFactory(
          DWRITE_FACTORY_TYPE_SHARED,
          __uuidof(IDWriteFactory),
          reinterpret_cast<IUnknown**>(&dwrite_factory_)))) {
    dwrite_factory_ = nullptr;
  }

  if (dwrite_factory_ != nullptr) {
    HRESULT text_result = dwrite_factory_->CreateTextFormat(
        L"Segoe UI Variable Text",
        nullptr,
        DWRITE_FONT_WEIGHT_SEMI_BOLD,
        DWRITE_FONT_STYLE_NORMAL,
        DWRITE_FONT_STRETCH_NORMAL,
        18.0F,
        L"vi-VN",
        &text_format_);
    if (FAILED(text_result)) {
      text_result = dwrite_factory_->CreateTextFormat(
          L"Segoe UI",
          nullptr,
          DWRITE_FONT_WEIGHT_SEMI_BOLD,
          DWRITE_FONT_STYLE_NORMAL,
          DWRITE_FONT_STRETCH_NORMAL,
          18.0F,
          L"vi-VN",
          &text_format_);
    }
    if (SUCCEEDED(text_result) && text_format_ != nullptr) {
      text_format_->SetTextAlignment(DWRITE_TEXT_ALIGNMENT_CENTER);
      text_format_->SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT_CENTER);
    }
  }
  ApplyAlpha(0);
  return true;
}

void KeystrokeOverlayWindow::Present(
    const KeystrokeOverlayState& state,
    const KeystrokeOverlayPlacement& placement,
    const KeystrokeOverlayMotionDecision& motion,
    const KeystrokeOverlayPreferences& preferences) noexcept {
  if (window_ == nullptr || !state.visible || !placement.bounds.IsValid()) {
    HideAndReleaseTransientState();
    return;
  }

  preferences_ = preferences;
  motion_ = motion;
  UpdateDisplayText(state);
  if (display_text_.empty()) {
    HideAndReleaseTransientState();
    return;
  }

  const auto& bounds = placement.bounds;
  SetWindowPos(window_, HWND_TOPMOST, bounds.left, bounds.top,
               bounds.Width(), bounds.Height(),
               SWP_NOACTIVATE | SWP_SHOWWINDOW);
  visible_ = true;
  current_alpha_ = motion.duration.count() > 0 ? 1 : preferences.opacity_percent;
  ApplyAlpha(current_alpha_);
  animation_started_tick_ = GetTickCount64();
  animation_active_ = motion.duration.count() > 0;
  if (animation_active_) {
    SetTimer(window_, kAnimationTimerId, kAnimationTimerIntervalMs, nullptr);
  } else {
    KillTimer(window_, kAnimationTimerId);
  }
  InvalidateRect(window_, nullptr, FALSE);
  UpdateWindow(window_);
}

void KeystrokeOverlayWindow::HideAndReleaseTransientState() noexcept {
  if (window_ != nullptr) {
    KillTimer(window_, kAnimationTimerId);
    ShowWindow(window_, SW_HIDE);
  }
  animation_active_ = false;
  visible_ = false;
  current_alpha_ = 0;
  display_text_.clear();
  ReleaseDeviceResources();
}

void KeystrokeOverlayWindow::Shutdown() noexcept {
  HideAndReleaseTransientState();
  SafeReleaseTyped(text_format_);
  SafeReleaseTyped(dwrite_factory_);
  SafeReleaseTyped(d2d_factory_);
  if (window_ != nullptr) {
    DestroyWindow(window_);
    window_ = nullptr;
  }
}

bool KeystrokeOverlayWindow::IsVisibleForTesting() const noexcept {
  return visible_ && window_ != nullptr && IsWindowVisible(window_) != FALSE;
}

bool KeystrokeOverlayWindow::HasActiveAnimationForTesting() const noexcept {
  return animation_active_;
}

void KeystrokeOverlayWindow::SimulateDeviceLossForTesting() noexcept {
  simulate_device_loss_ = true;
  if (window_ != nullptr) {
    InvalidateRect(window_, nullptr, FALSE);
    UpdateWindow(window_);
  }
}

LRESULT CALLBACK KeystrokeOverlayWindow::WindowProcedure(
    HWND window, UINT message, WPARAM w_param, LPARAM l_param) noexcept {
  KeystrokeOverlayWindow* self = reinterpret_cast<KeystrokeOverlayWindow*>(
      GetWindowLongPtrW(window, GWLP_USERDATA));
  if (message == WM_NCCREATE) {
    const auto* create = reinterpret_cast<const CREATESTRUCTW*>(l_param);
    self = static_cast<KeystrokeOverlayWindow*>(create->lpCreateParams);
    self->window_ = window;
    SetWindowLongPtrW(window, GWLP_USERDATA,
                      reinterpret_cast<LONG_PTR>(self));
  }
  if (self != nullptr) {
    return self->HandleMessage(message, w_param, l_param);
  }
  return DefWindowProcW(window, message, w_param, l_param);
}

LRESULT KeystrokeOverlayWindow::HandleMessage(
    UINT message, WPARAM w_param, LPARAM l_param) noexcept {
  switch (message) {
    case WM_NCHITTEST:
      return HTTRANSPARENT;
    case WM_ERASEBKGND:
      return 1;
    case WM_PAINT: {
      PAINTSTRUCT paint{};
      BeginPaint(window_, &paint);
      Render();
      EndPaint(window_, &paint);
      return 0;
    }
    case WM_SIZE:
      if (render_target_ != nullptr) {
        const auto width = LOWORD(l_param);
        const auto height = HIWORD(l_param);
        render_target_->Resize(D2D1::SizeU(width, height));
      }
      return 0;
    case WM_TIMER:
      if (w_param == kAnimationTimerId) {
        TickAnimation();
        return 0;
      }
      break;
    case WM_DESTROY:
      ReleaseDeviceResources();
      return 0;
    default:
      break;
  }
  return DefWindowProcW(window_, message, w_param, l_param);
}

bool KeystrokeOverlayWindow::EnsureDeviceResources() noexcept {
  if (render_target_ != nullptr) {
    return true;
  }
  if (d2d_factory_ == nullptr || window_ == nullptr) {
    return false;
  }
  RECT bounds{};
  GetClientRect(window_, &bounds);
  const auto size = D2D1::SizeU(
      static_cast<UINT32>(std::max(1L, bounds.right - bounds.left)),
      static_cast<UINT32>(std::max(1L, bounds.bottom - bounds.top)));
  const auto properties = D2D1::RenderTargetProperties(
      D2D1_RENDER_TARGET_TYPE_DEFAULT,
      D2D1::PixelFormat(DXGI_FORMAT_UNKNOWN,
                        D2D1_ALPHA_MODE_PREMULTIPLIED));
  const auto hwnd_properties = D2D1::HwndRenderTargetProperties(window_, size);
  if (FAILED(d2d_factory_->CreateHwndRenderTarget(
          properties, hwnd_properties, &render_target_))) {
    return false;
  }
  if (FAILED(render_target_->CreateSolidColorBrush(
          SurfaceColor(), &surface_brush_)) ||
      FAILED(render_target_->CreateSolidColorBrush(
          TextColor(), &text_brush_)) ||
      FAILED(render_target_->CreateSolidColorBrush(
          AccentColor(), &accent_brush_))) {
    ReleaseDeviceResources();
    return false;
  }
  return true;
}

void KeystrokeOverlayWindow::ReleaseDeviceResources() noexcept {
  SafeReleaseTyped(accent_brush_);
  SafeReleaseTyped(text_brush_);
  SafeReleaseTyped(surface_brush_);
  SafeReleaseTyped(render_target_);
}

void KeystrokeOverlayWindow::Render() noexcept {
  if (display_text_.empty()) {
    return;
  }

  RECT bounds{};
  GetClientRect(window_, &bounds);
  if (!EnsureDeviceResources() || text_format_ == nullptr) {
    HDC dc = GetDC(window_);
    if (dc != nullptr) {
      HBRUSH surface = CreateSolidBrush(RGB(20, 22, 28));
      FillRect(dc, &bounds, surface);
      DeleteObject(surface);
      SetBkMode(dc, TRANSPARENT);
      SetTextColor(dc, RGB(245, 247, 252));
      DrawTextW(dc,
                reinterpret_cast<const wchar_t*>(display_text_.data()),
                static_cast<int>(display_text_.size()),
                &bounds,
                DT_CENTER | DT_VCENTER | DT_SINGLELINE | DT_END_ELLIPSIS);
      ReleaseDC(window_, dc);
    }
    return;
  }
  const float width = static_cast<float>(bounds.right - bounds.left);
  const float height = static_cast<float>(bounds.bottom - bounds.top);
  const float radius = std::clamp(height * 0.24F, 10.0F, 16.0F);

  render_target_->BeginDraw();
  render_target_->Clear(D2D1::ColorF(0, 0.0F));
  const auto rounded = D2D1::RoundedRect(
      D2D1::RectF(0.5F, 0.5F, width - 0.5F, height - 0.5F),
      radius, radius);
  render_target_->FillRoundedRectangle(rounded, surface_brush_);

  const auto text_bounds = D2D1::RectF(16.0F, 2.0F, width - 16.0F,
                                      height - 2.0F);
  render_target_->DrawTextW(
      reinterpret_cast<const wchar_t*>(display_text_.data()),
      static_cast<UINT32>(display_text_.size()),
      text_format_,
      text_bounds,
      text_brush_,
      D2D1_DRAW_TEXT_OPTIONS_CLIP);

  HRESULT result = render_target_->EndDraw();
  if (simulate_device_loss_) {
    simulate_device_loss_ = false;
    result = D2DERR_RECREATE_TARGET;
  }
  if (result == D2DERR_RECREATE_TARGET) {
    ReleaseDeviceResources();
    if (EnsureDeviceResources()) {
      InvalidateRect(window_, nullptr, FALSE);
    }
  }
}

void KeystrokeOverlayWindow::TickAnimation() noexcept {
  if (!animation_active_ || window_ == nullptr) {
    return;
  }
  const auto duration = std::max<std::int64_t>(1, motion_.duration.count());
  const auto elapsed = static_cast<std::int64_t>(
      GetTickCount64() - animation_started_tick_);
  const double progress = std::clamp(
      static_cast<double>(elapsed) / static_cast<double>(duration),
      0.0,
      1.0);
  const double eased = 1.0 - std::pow(1.0 - progress, 3.0);
  current_alpha_ = static_cast<std::uint8_t>(std::clamp(
      static_cast<int>(std::lround(
          eased * static_cast<double>(preferences_.opacity_percent))),
      1,
      100));
  ApplyAlpha(current_alpha_);
  if (progress >= 1.0) {
    KillTimer(window_, kAnimationTimerId);
    animation_active_ = false;
  }
}

void KeystrokeOverlayWindow::UpdateDisplayText(
    const KeystrokeOverlayState& state) {
  if (!state.text.empty()) {
    display_text_.assign(state.text.View());
    return;
  }
  display_text_.clear();
  display_text_.reserve(state.token_count * 2);
  for (std::size_t index = 0; index < state.token_count; ++index) {
    if (index != 0) {
      display_text_.push_back(u' ');
    }
    display_text_.push_back(state.tokens[index]);
  }
}

void KeystrokeOverlayWindow::ApplyAlpha(std::uint8_t alpha) noexcept {
  if (window_ == nullptr) {
    return;
  }
  const auto byte_alpha = static_cast<BYTE>(
      (static_cast<unsigned>(alpha) * 255U) / 100U);
  SetLayeredWindowAttributes(window_, 0, byte_alpha, LWA_ALPHA);
}

}  // namespace keyina::windows
