#include <keyina/windows/keystroke_overlay_window.h>

#include <d2d1.h>
#include <dwrite.h>
#include <dwmapi.h>

#include <algorithm>
#include <cmath>
#include <cstdint>

namespace keyina::windows {
namespace {

constexpr wchar_t kOverlayWindowClassName[] =
    L"KeyinaKeystrokeOverlayWindow";
constexpr UINT_PTR kAnimationTimerIdentifier = 1;
constexpr UINT kAnimationFrameMilliseconds = 16;
constexpr float kCompositionFontSize = 18.0F;
constexpr float kTokenFontSize = 14.0F;
constexpr float kHorizontalPadding = 12.0F;
constexpr float kCornerRadius = 12.0F;
constexpr BYTE kNormalWindowAlpha = 245;

float ClampProgress(float value) noexcept {
  return std::clamp(value, 0.0F, 1.0F);
}

float EaseOutCubic(float value) noexcept {
  const float inverse = 1.0F - ClampProgress(value);
  return 1.0F - (inverse * inverse * inverse);
}

template <typename Interface>
void ReleaseInterface(Interface*& value) noexcept {
  if (value != nullptr) {
    value->Release();
    value = nullptr;
  }
}

D2D1_COLOR_F ColorFromSystem(COLORREF color, float alpha = 1.0F) noexcept {
  return D2D1::ColorF(
      static_cast<float>(GetRValue(color)) / 255.0F,
      static_cast<float>(GetGValue(color)) / 255.0F,
      static_cast<float>(GetBValue(color)) / 255.0F,
      alpha);
}

bool ReadAppsUseLightTheme() noexcept {
  DWORD value = 1;
  DWORD bytes = sizeof(value);
  const LSTATUS status = RegGetValueW(
      HKEY_CURRENT_USER,
      L"Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize",
      L"AppsUseLightTheme",
      RRF_RT_REG_DWORD,
      nullptr,
      &value,
      &bytes);
  return status != ERROR_SUCCESS || value != 0;
}

}  // namespace

KeystrokeOverlayWindow::~KeystrokeOverlayWindow() {
  HideAndReleaseTransientState();
  if (window_ != nullptr) {
    DestroyWindow(window_);
    window_ = nullptr;
  }
  DiscardRenderTarget();
  ReleaseDeviceIndependentResources();
}

bool KeystrokeOverlayWindow::Initialize(HINSTANCE instance) noexcept {
  if (initialized_) {
    return window_ != nullptr;
  }
  if (instance == nullptr || !EnsureDeviceIndependentResources()) {
    return false;
  }

  WNDCLASSEXW window_class{};
  window_class.cbSize = sizeof(window_class);
  window_class.style = CS_HREDRAW | CS_VREDRAW;
  window_class.lpfnWndProc = &KeystrokeOverlayWindow::WindowProcedure;
  window_class.hInstance = instance;
  window_class.hCursor = LoadCursorW(nullptr, IDC_ARROW);
  window_class.lpszClassName = kOverlayWindowClassName;
  if (RegisterClassExW(&window_class) == 0 &&
      GetLastError() != ERROR_CLASS_ALREADY_EXISTS) {
    ReleaseDeviceIndependentResources();
    return false;
  }

  instance_ = instance;
  window_ = CreateWindowExW(
      WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE |
          WS_EX_TRANSPARENT | WS_EX_LAYERED,
      kOverlayWindowClassName,
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
    ReleaseDeviceIndependentResources();
    instance_ = nullptr;
    return false;
  }

  static_cast<void>(SetLayeredWindowAttributes(
      window_, 0, kNormalWindowAlpha, LWA_ALPHA));
#if defined(DWMWA_WINDOW_CORNER_PREFERENCE)
  const DWM_WINDOW_CORNER_PREFERENCE corner = DWMWCP_ROUND;
  static_cast<void>(DwmSetWindowAttribute(
      window_,
      DWMWA_WINDOW_CORNER_PREFERENCE,
      &corner,
      sizeof(corner)));
#endif
  ShowWindow(window_, SW_HIDE);
  RefreshTheme();
  initialized_ = true;
  return true;
}

void KeystrokeOverlayWindow::Present(
    const KeystrokeOverlayState& state,
    const KeystrokeOverlayPlacement& placement,
    const KeystrokeOverlayMotionDecision& motion) noexcept {
  if (!initialized_ || window_ == nullptr) {
    return;
  }
  if (!state.visible || state.suppressed || !placement.valid ||
      placement.bounds.right <= placement.bounds.left ||
      placement.bounds.bottom <= placement.bounds.top) {
    HideAndReleaseTransientState();
    return;
  }

  RECT previous{};
  const bool was_visible = visible_ &&
      GetWindowRect(window_, &previous) != FALSE;
  previous_window_bounds_ = was_visible
      ? OverlayRectangle{
            previous.left, previous.top, previous.right, previous.bottom}
      : placement.bounds;

  latest_state_ = state;
  latest_placement_ = placement;
  latest_motion_ = motion;
  BuildDisplayText();
  RefreshTheme();
  ReleaseInterface(text_layout_);

  const int width = placement.bounds.right - placement.bounds.left;
  const int height = placement.bounds.bottom - placement.bounds.top;
  ApplyRoundedRegion(width, height);

  animation_started_at_ = GetTickCount64();
  const bool animate = motion.duration.count() > 0;
  if (animate) {
    animation_from_opacity_ = was_visible ? current_opacity_ : 0.82F;
    if (motion.translate) {
      if (was_visible) {
        animation_from_offset_x_ = std::clamp(
            static_cast<int>(previous.left) - placement.bounds.left, -5, 5);
        animation_from_offset_y_ = std::clamp(
            static_cast<int>(previous.top) - placement.bounds.top, -5, 5);
      } else {
        animation_from_offset_x_ = 0;
        animation_from_offset_y_ = placement.placed_above ? 4 : -4;
      }
    } else {
      animation_from_offset_x_ = 0;
      animation_from_offset_y_ = 0;
    }
    StopAnimation();
    animation_timer_ = SetTimer(
        window_,
        kAnimationTimerIdentifier,
        kAnimationFrameMilliseconds,
        nullptr);
    if (animation_timer_ == 0) {
      animation_from_opacity_ = 1.0F;
      animation_from_offset_x_ = 0;
      animation_from_offset_y_ = 0;
    }
  } else {
    StopAnimation();
    animation_from_opacity_ = 1.0F;
    animation_from_offset_x_ = 0;
    animation_from_offset_y_ = 0;
  }

  visible_ = true;
  ApplyWindowFrame(animate && animation_timer_ != 0 ? 0.0F : 1.0F);
  Render();
}

void KeystrokeOverlayWindow::HideAndReleaseTransientState() noexcept {
  StopAnimation();
  ReleaseInterface(text_layout_);
  display_text_units_ = 0;
  latest_state_ = {};
  latest_placement_ = {};
  latest_motion_ = {};
  current_opacity_ = 1.0F;
  animation_from_opacity_ = 1.0F;
  animation_from_offset_x_ = 0;
  animation_from_offset_y_ = 0;
  visible_ = false;
  if (window_ != nullptr) {
    ShowWindow(window_, SW_HIDE);
  }
}

bool KeystrokeOverlayWindow::IsVisibleForTesting() const noexcept {
  return visible_ && window_ != nullptr &&
      IsWindowVisible(window_) != FALSE;
}

void KeystrokeOverlayWindow::SimulateDeviceLossForTesting() noexcept {
  if (!initialized_ || window_ == nullptr || !visible_) {
    return;
  }
  force_device_loss_for_testing_ = true;
  Render();
}

LRESULT CALLBACK KeystrokeOverlayWindow::WindowProcedure(
    HWND window,
    UINT message,
    WPARAM w_param,
    LPARAM l_param) noexcept {
  if (message == WM_NCCREATE) {
    const auto* create = reinterpret_cast<const CREATESTRUCTW*>(l_param);
    SetWindowLongPtrW(
        window,
        GWLP_USERDATA,
        reinterpret_cast<LONG_PTR>(create->lpCreateParams));
  }
  auto* overlay = reinterpret_cast<KeystrokeOverlayWindow*>(
      GetWindowLongPtrW(window, GWLP_USERDATA));
  if (overlay == nullptr) {
    return DefWindowProcW(window, message, w_param, l_param);
  }
  const LRESULT result =
      overlay->HandleWindowMessage(window, message, w_param, l_param);
  if (message == WM_NCDESTROY) {
    SetWindowLongPtrW(window, GWLP_USERDATA, 0);
  }
  return result;
}

LRESULT KeystrokeOverlayWindow::HandleWindowMessage(
    HWND window,
    UINT message,
    WPARAM w_param,
    LPARAM l_param) noexcept {
  switch (message) {
    case WM_PAINT: {
      PAINTSTRUCT paint{};
      BeginPaint(window, &paint);
      Render();
      EndPaint(window, &paint);
      return 0;
    }
    case WM_ERASEBKGND:
      return 1;
    case WM_TIMER:
      if (w_param == kAnimationTimerIdentifier) {
        UpdateAnimationFrame();
        return 0;
      }
      break;
    case WM_NCHITTEST:
      return HTTRANSPARENT;
    case WM_MOUSEACTIVATE:
      return MA_NOACTIVATE;
    case WM_DPICHANGED:
      DiscardRenderTarget();
      ReleaseInterface(text_layout_);
      ApplyRoundedRegion(
          latest_placement_.bounds.right - latest_placement_.bounds.left,
          latest_placement_.bounds.bottom - latest_placement_.bounds.top);
      Render();
      return 0;
    case WM_SETTINGCHANGE:
    case WM_THEMECHANGED:
      RefreshTheme();
      Render();
      return 0;
    case WM_CLOSE:
      HideAndReleaseTransientState();
      return 0;
    case WM_NCDESTROY:
      StopAnimation();
      if (window_ == window) {
        window_ = nullptr;
      }
      initialized_ = false;
      return DefWindowProcW(window, message, w_param, l_param);
    default:
      break;
  }
  return DefWindowProcW(window, message, w_param, l_param);
}

bool KeystrokeOverlayWindow::EnsureDeviceIndependentResources() noexcept {
  if (d2d_factory_ == nullptr &&
      FAILED(D2D1CreateFactory(
          D2D1_FACTORY_TYPE_SINGLE_THREADED,
          &d2d_factory_))) {
    return false;
  }
  if (write_factory_ == nullptr &&
      FAILED(DWriteCreateFactory(
          DWRITE_FACTORY_TYPE_SHARED,
          __uuidof(IDWriteFactory),
          reinterpret_cast<IUnknown**>(&write_factory_)))) {
    return false;
  }
  if (token_text_format_ == nullptr &&
      FAILED(write_factory_->CreateTextFormat(
          L"Segoe UI Variable Text",
          nullptr,
          DWRITE_FONT_WEIGHT_MEDIUM,
          DWRITE_FONT_STYLE_NORMAL,
          DWRITE_FONT_STRETCH_NORMAL,
          kTokenFontSize,
          L"",
          &token_text_format_))) {
    return false;
  }
  if (composition_text_format_ == nullptr &&
      FAILED(write_factory_->CreateTextFormat(
          L"Segoe UI Variable Text",
          nullptr,
          DWRITE_FONT_WEIGHT_SEMI_BOLD,
          DWRITE_FONT_STYLE_NORMAL,
          DWRITE_FONT_STRETCH_NORMAL,
          kCompositionFontSize,
          L"",
          &composition_text_format_))) {
    return false;
  }
  token_text_format_->SetTextAlignment(DWRITE_TEXT_ALIGNMENT_CENTER);
  token_text_format_->SetParagraphAlignment(
      DWRITE_PARAGRAPH_ALIGNMENT_CENTER);
  composition_text_format_->SetTextAlignment(DWRITE_TEXT_ALIGNMENT_CENTER);
  composition_text_format_->SetParagraphAlignment(
      DWRITE_PARAGRAPH_ALIGNMENT_CENTER);
  return true;
}

bool KeystrokeOverlayWindow::EnsureRenderTarget() noexcept {
  if (window_ == nullptr || !EnsureDeviceIndependentResources()) {
    return false;
  }

  RECT client{};
  if (GetClientRect(window_, &client) == FALSE) {
    return false;
  }
  const UINT width = static_cast<UINT>(std::max(client.right - client.left, 1L));
  const UINT height = static_cast<UINT>(std::max(client.bottom - client.top, 1L));
  if (render_target_ != nullptr) {
    const D2D1_SIZE_U current = render_target_->GetPixelSize();
    if (current.width != width || current.height != height) {
      const HRESULT resized = render_target_->Resize(D2D1::SizeU(width, height));
      if (resized == D2DERR_RECREATE_TARGET) {
        DiscardRenderTarget();
        ++device_recovery_count_;
      } else if (FAILED(resized)) {
        return false;
      } else {
        ReleaseInterface(text_layout_);
      }
    }
  }
  if (render_target_ == nullptr) {
    const D2D1_RENDER_TARGET_PROPERTIES properties =
        D2D1::RenderTargetProperties(
            D2D1_RENDER_TARGET_TYPE_DEFAULT,
            D2D1::PixelFormat(
                DXGI_FORMAT_UNKNOWN,
                D2D1_ALPHA_MODE_IGNORE));
    const D2D1_HWND_RENDER_TARGET_PROPERTIES window_properties =
        D2D1::HwndRenderTargetProperties(
            window_,
            D2D1::SizeU(width, height),
            D2D1_PRESENT_OPTIONS_RETAIN_CONTENTS);
    if (FAILED(d2d_factory_->CreateHwndRenderTarget(
            properties,
            window_properties,
            &render_target_))) {
      return false;
    }
    const UINT dpi = GetDpiForWindow(window_);
    if (dpi != 0) {
      render_target_->SetDpi(
          static_cast<float>(dpi),
          static_cast<float>(dpi));
    }
  }

  if (background_brush_ == nullptr || text_brush_ == nullptr) {
    const D2D1_COLOR_F background = high_contrast_
        ? ColorFromSystem(GetSysColor(COLOR_WINDOW))
        : (light_theme_
               ? D2D1::ColorF(0xF7F7F8, 0.97F)
               : D2D1::ColorF(0x202126, 0.97F));
    const D2D1_COLOR_F foreground = high_contrast_
        ? ColorFromSystem(GetSysColor(COLOR_WINDOWTEXT))
        : (light_theme_
               ? D2D1::ColorF(0x17181B)
               : D2D1::ColorF(0xF7F7F8));
    if (background_brush_ == nullptr &&
        FAILED(render_target_->CreateSolidColorBrush(
            background,
            &background_brush_))) {
      return false;
    }
    if (text_brush_ == nullptr &&
        FAILED(render_target_->CreateSolidColorBrush(
            foreground,
            &text_brush_))) {
      return false;
    }
  }
  return true;
}

bool KeystrokeOverlayWindow::RebuildTextLayout() noexcept {
  ReleaseInterface(text_layout_);
  if (display_text_units_ == 0) {
    return true;
  }
  if (write_factory_ == nullptr || window_ == nullptr) {
    return false;
  }
  RECT client{};
  if (GetClientRect(window_, &client) == FALSE) {
    return false;
  }
  const UINT dpi = GetDpiForWindow(window_);
  const float scale = dpi == 0 ? 1.0F : 96.0F / static_cast<float>(dpi);
  const float width = std::max(
      1.0F,
      static_cast<float>(client.right - client.left) * scale -
          (kHorizontalPadding * 2.0F));
  const float height = std::max(
      1.0F,
      static_cast<float>(client.bottom - client.top) * scale);
  IDWriteTextFormat* format = latest_state_.text.empty()
      ? token_text_format_
      : composition_text_format_;
  if (format == nullptr ||
      FAILED(write_factory_->CreateTextLayout(
          display_text_.data(),
          static_cast<UINT32>(display_text_units_),
          format,
          width,
          height,
          &text_layout_))) {
    return false;
  }
  text_layout_->SetWordWrapping(DWRITE_WORD_WRAPPING_NO_WRAP);
  DWRITE_TRIMMING trimming{};
  trimming.granularity = DWRITE_TRIMMING_GRANULARITY_CHARACTER;
  static_cast<void>(text_layout_->SetTrimming(&trimming, nullptr));
  return true;
}

void KeystrokeOverlayWindow::DiscardRenderTarget() noexcept {
  ReleaseInterface(background_brush_);
  ReleaseInterface(text_brush_);
  ReleaseInterface(render_target_);
}

void KeystrokeOverlayWindow::ReleaseDeviceIndependentResources() noexcept {
  ReleaseInterface(text_layout_);
  ReleaseInterface(token_text_format_);
  ReleaseInterface(composition_text_format_);
  ReleaseInterface(write_factory_);
  ReleaseInterface(d2d_factory_);
}

void KeystrokeOverlayWindow::Render() noexcept {
  if (!visible_ || window_ == nullptr) {
    return;
  }
  if (force_device_loss_for_testing_) {
    force_device_loss_for_testing_ = false;
    DiscardRenderTarget();
    ++device_recovery_count_;
  }
  if (!EnsureRenderTarget()) {
    return;
  }
  if (text_layout_ == nullptr && !RebuildTextLayout()) {
    return;
  }

  auto draw = [this]() noexcept {
    render_target_->BeginDraw();
    render_target_->SetTransform(D2D1::Matrix3x2F::Identity());
    render_target_->Clear(D2D1::ColorF(0.0F, 0.0F, 0.0F, 0.0F));
    const D2D1_SIZE_F size = render_target_->GetSize();
    const float radius = high_contrast_ ? 2.0F : kCornerRadius;
    const D2D1_ROUNDED_RECT surface = D2D1::RoundedRect(
        D2D1::RectF(0.0F, 0.0F, size.width, size.height),
        radius,
        radius);
    render_target_->FillRoundedRectangle(surface, background_brush_);
    if (text_layout_ != nullptr && text_brush_ != nullptr) {
      render_target_->DrawTextLayout(
          D2D1::Point2F(kHorizontalPadding, 0.0F),
          text_layout_,
          text_brush_,
          D2D1_DRAW_TEXT_OPTIONS_CLIP);
    }
    return render_target_->EndDraw();
  };

  HRESULT result = draw();
  if (result == D2DERR_RECREATE_TARGET) {
    DiscardRenderTarget();
    ++device_recovery_count_;
    if (EnsureRenderTarget()) {
      result = draw();
    }
  }
  if (SUCCEEDED(result)) {
    last_rendered_generation_ = latest_state_.generation;
  }
}

void KeystrokeOverlayWindow::UpdateAnimationFrame() noexcept {
  if (animation_timer_ == 0 || !visible_) {
    StopAnimation();
    return;
  }
  const auto duration = latest_motion_.duration.count();
  if (duration <= 0) {
    ApplyWindowFrame(1.0F);
    StopAnimation();
    Render();
    return;
  }
  const ULONGLONG elapsed = GetTickCount64() - animation_started_at_;
  const float progress = ClampProgress(
      static_cast<float>(elapsed) / static_cast<float>(duration));
  ApplyWindowFrame(progress);
  Render();
  if (progress >= 1.0F) {
    StopAnimation();
  }
}

void KeystrokeOverlayWindow::StopAnimation() noexcept {
  if (animation_timer_ != 0 && window_ != nullptr) {
    KillTimer(window_, animation_timer_);
  }
  animation_timer_ = 0;
}

void KeystrokeOverlayWindow::ApplyWindowFrame(float progress) noexcept {
  if (window_ == nullptr || !latest_placement_.valid) {
    return;
  }
  const float eased = EaseOutCubic(progress);
  current_opacity_ = animation_from_opacity_ +
      ((1.0F - animation_from_opacity_) * eased);
  const int offset_x = static_cast<int>(std::lround(
      static_cast<float>(animation_from_offset_x_) * (1.0F - eased)));
  const int offset_y = static_cast<int>(std::lround(
      static_cast<float>(animation_from_offset_y_) * (1.0F - eased)));
  const int width = latest_placement_.bounds.right -
      latest_placement_.bounds.left;
  const int height = latest_placement_.bounds.bottom -
      latest_placement_.bounds.top;
  static_cast<void>(SetWindowPos(
      window_,
      HWND_TOPMOST,
      latest_placement_.bounds.left + offset_x,
      latest_placement_.bounds.top + offset_y,
      width,
      height,
      SWP_NOACTIVATE | SWP_SHOWWINDOW));
  const BYTE base_alpha = high_contrast_ ? 255 : kNormalWindowAlpha;
  const BYTE alpha = static_cast<BYTE>(std::clamp(
      static_cast<int>(std::lround(base_alpha * current_opacity_)),
      0,
      255));
  static_cast<void>(SetLayeredWindowAttributes(
      window_, 0, alpha, LWA_ALPHA));
}

void KeystrokeOverlayWindow::RefreshTheme() noexcept {
  HIGHCONTRASTW contrast{};
  contrast.cbSize = sizeof(contrast);
  const bool high_contrast =
      SystemParametersInfoW(
          SPI_GETHIGHCONTRAST,
          sizeof(contrast),
          &contrast,
          0) != FALSE &&
      (contrast.dwFlags & HCF_HIGHCONTRASTON) != 0;
  const bool light_theme = ReadAppsUseLightTheme();
  if (high_contrast_ == high_contrast && light_theme_ == light_theme) {
    return;
  }
  high_contrast_ = high_contrast;
  light_theme_ = light_theme;
  DiscardRenderTarget();
}

void KeystrokeOverlayWindow::ApplyRoundedRegion(
    int width,
    int height) noexcept {
  if (window_ == nullptr || width <= 0 || height <= 0) {
    return;
  }
  const UINT dpi = GetDpiForWindow(window_);
  const int radius = std::max(
      2,
      MulDiv(static_cast<int>(kCornerRadius * 2.0F),
             dpi == 0 ? 96 : static_cast<int>(dpi),
             96));
  HRGN region = CreateRoundRectRgn(0, 0, width + 1, height + 1, radius, radius);
  if (region != nullptr && SetWindowRgn(window_, region, FALSE) == 0) {
    DeleteObject(region);
  }
}

void KeystrokeOverlayWindow::BuildDisplayText() noexcept {
  display_text_units_ = 0;
  auto append = [this](std::u16string_view text) noexcept {
    for (const char16_t character : text) {
      if (display_text_units_ == display_text_.size()) {
        return false;
      }
      display_text_[display_text_units_++] = static_cast<wchar_t>(character);
    }
    return true;
  };

  if (!latest_state_.text.empty()) {
    static_cast<void>(append(latest_state_.text.view()));
  } else {
    for (std::size_t index = 0; index < latest_state_.token_count; ++index) {
      if (index != 0) {
        if (!append(u"  ")) {
          break;
        }
      }
      if (!append(latest_state_.tokens[index].view())) {
        break;
      }
    }
  }
  if (latest_state_.truncated && display_text_units_ != 0) {
    display_text_[display_text_units_ - 1] = L'\u2026';
  }
}

}  // namespace keyina::windows
