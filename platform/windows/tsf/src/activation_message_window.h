#pragma once

#include <windows.h>

#include <functional>

namespace keyina::tsf {

class ActivationMessageWindow final {
 public:
  using Callback = std::function<void()>;

  ActivationMessageWindow() = default;
  ~ActivationMessageWindow();

  ActivationMessageWindow(const ActivationMessageWindow&) = delete;
  ActivationMessageWindow& operator=(const ActivationMessageWindow&) = delete;

  [[nodiscard]] bool Create(Callback callback) noexcept;
  [[nodiscard]] bool Post() const noexcept;
  void Destroy() noexcept;

  static LRESULT CALLBACK WindowProcedure(
      HWND window,
      UINT message,
      WPARAM word,
      LPARAM long_value) noexcept;

 private:
  HWND window_{nullptr};
  DWORD owner_thread_id_{};
  Callback callback_;
};

}  // namespace keyina::tsf
