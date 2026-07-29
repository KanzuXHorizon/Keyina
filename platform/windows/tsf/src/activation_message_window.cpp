#include "activation_message_window.h"

#include <array>
#include <atomic>
#include <mutex>
#include <utility>

namespace keyina::tsf {
namespace {

constexpr UINT kDispatchMessage = WM_APP + 0x4B;
constexpr wchar_t kWindowClassName[] = L"Keyina.Tsf.ActivationMessageWindow.v1";
std::once_flag g_window_class_once;
std::atomic<bool> g_window_class_ready{false};

bool EnsureWindowClass() noexcept {
  std::call_once(g_window_class_once, [] {
    WNDCLASSEXW window_class{};
    window_class.cbSize = sizeof(window_class);
    window_class.lpfnWndProc = ActivationMessageWindow::WindowProcedure;
    window_class.hInstance = GetModuleHandleW(nullptr);
    window_class.lpszClassName = kWindowClassName;
    const ATOM atom = RegisterClassExW(&window_class);
    g_window_class_ready.store(
        atom != 0 || GetLastError() == ERROR_CLASS_ALREADY_EXISTS,
        std::memory_order_release);
  });
  return g_window_class_ready.load(std::memory_order_acquire);
}

}  // namespace

ActivationMessageWindow::~ActivationMessageWindow() { Destroy(); }

bool ActivationMessageWindow::Create(Callback callback) noexcept {
  if (window_ != nullptr || !callback || !EnsureWindowClass()) {
    return false;
  }

  callback_ = std::move(callback);
  owner_thread_id_ = GetCurrentThreadId();
  window_ = CreateWindowExW(
      0, kWindowClassName, L"", 0,
      0, 0, 0, 0, HWND_MESSAGE, nullptr,
      GetModuleHandleW(nullptr), this);
  if (window_ == nullptr) {
    owner_thread_id_ = 0;
    callback_ = {};
    return false;
  }
  return true;
}

bool ActivationMessageWindow::Post() const noexcept {
  return window_ != nullptr &&
         PostMessageW(window_, kDispatchMessage, 0, 0) != FALSE;
}

void ActivationMessageWindow::Destroy() noexcept {
  HWND window = window_;
  window_ = nullptr;
  if (window == nullptr) {
    return;
  }

  if (GetCurrentThreadId() == owner_thread_id_) {
    DestroyWindow(window);
  } else {
    SendMessageW(window, WM_CLOSE, 0, 0);
  }
  owner_thread_id_ = 0;
  callback_ = {};
}

LRESULT CALLBACK ActivationMessageWindow::WindowProcedure(
    HWND window,
    UINT message,
    WPARAM word,
    LPARAM long_value) noexcept {
  auto* self = reinterpret_cast<ActivationMessageWindow*>(
      GetWindowLongPtrW(window, GWLP_USERDATA));
  if (message == WM_NCCREATE) {
    const auto* create = reinterpret_cast<const CREATESTRUCTW*>(long_value);
    self = static_cast<ActivationMessageWindow*>(create->lpCreateParams);
    SetWindowLongPtrW(
        window, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(self));
  }

  if (self != nullptr && message == kDispatchMessage) {
    try {
      self->callback_();
    } catch (...) {
      // A failed auxiliary callback must never escape into an application loop.
    }
    return 0;
  }
  if (message == WM_CLOSE) {
    DestroyWindow(window);
    return 0;
  }
  if (message == WM_NCDESTROY) {
    SetWindowLongPtrW(window, GWLP_USERDATA, 0);
  }
  return DefWindowProcW(window, message, word, long_value);
}

}  // namespace keyina::tsf
