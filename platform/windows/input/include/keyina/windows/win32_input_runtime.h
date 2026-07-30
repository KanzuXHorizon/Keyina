#pragma once

#include <keyina/windows/resident_input_controller.h>

#include <windows.h>
#include <shellapi.h>

#include <array>
#include <cstdint>

namespace keyina::windows {

enum class NativeRuntimeStartupStage : std::uint8_t {
  None = 0,
  RegisterWindowClass,
  CreateMessageWindow,
  InstallKeyboardHook,
};

struct NativeResidentResourceSnapshot {
  std::uint64_t working_set_bytes{};
  std::uint64_t private_working_set_bytes{};
  std::uint64_t private_memory_bytes{};
  std::uint32_t thread_count{};
  std::uint32_t thread_count_delta{};
  std::uint32_t handle_count{};
  double cpu_percent{};
  std::uint64_t processed_keyboard_events{};
  bool hook_running{false};
  bool contaminated_by_input{false};
  bool budget_pass{false};
};

class Win32InputRuntime {
 public:
  explicit Win32InputRuntime(RuntimeInputProfile profile,
                             bool enable_tray) noexcept;
  ~Win32InputRuntime();

  Win32InputRuntime(const Win32InputRuntime&) = delete;
  Win32InputRuntime& operator=(const Win32InputRuntime&) = delete;

  [[nodiscard]] bool Start() noexcept;
  int Run() noexcept;
  void Stop() noexcept;
  void PumpMessagesFor(DWORD duration_milliseconds) noexcept;

  [[nodiscard]] NativeRuntimeStartupStage startup_stage() const noexcept {
    return startup_stage_;
  }

  [[nodiscard]] DWORD startup_error() const noexcept {
    return startup_error_;
  }

  [[nodiscard]] bool hook_running() const noexcept {
    return hook_ != nullptr;
  }

  [[nodiscard]] std::uint64_t processed_keyboard_events() const noexcept {
    return processed_keyboard_events_;
  }

  [[nodiscard]] RuntimeInputProfile profile() const noexcept {
    return profile_;
  }

 private:
  class KeyStateSet {
   public:
    [[nodiscard]] bool Get(std::uint16_t key) const noexcept;
    void Set(std::uint16_t key, bool value) noexcept;
    void Clear() noexcept;

   private:
    std::array<std::uint64_t, 4> segments_{};
  };

  static LRESULT CALLBACK WindowProcedure(HWND window, UINT message,
                                           WPARAM w_param,
                                           LPARAM l_param) noexcept;
  static LRESULT CALLBACK KeyboardProcedure(int code, WPARAM message,
                                             LPARAM data) noexcept;

  LRESULT HandleWindowMessage(HWND window, UINT message, WPARAM w_param,
                              LPARAM l_param) noexcept;
  LRESULT HandleKeyboardEvent(int code, WPARAM message,
                              LPARAM data) noexcept;
  [[nodiscard]] TypingContext CaptureTypingContext() noexcept;
  [[nodiscard]] bool Inject(const InputDecision& decision) noexcept;
  [[nodiscard]] bool IsPointerResetPacket(HRAWINPUT input) noexcept;
  void RequestPointerRegistration(bool active) noexcept;
  void ApplyPointerRegistration() noexcept;
  void ProcessToggleGesture(const PhysicalKeyEvent& event) noexcept;
  void RefreshModifierState() noexcept;
  void UpdateTray() noexcept;
  void ShowTrayMenu() noexcept;
  void OpenManagedSettings() noexcept;
  void RequestExit() noexcept;

  RuntimeInputProfile profile_{};
  ResidentInputController controller_;
  bool enable_tray_{false};
  HWND window_{nullptr};
  HHOOK hook_{nullptr};
  HICON active_icon_{nullptr};
  HICON inactive_icon_{nullptr};
  HMODULE shell_module_{nullptr};
  BOOL(WINAPI* shell_notify_icon_)(DWORD, PNOTIFYICONDATAW){nullptr};
  HINSTANCE(WINAPI* shell_execute_)(
      HWND, LPCWSTR, LPCWSTR, LPCWSTR, LPCWSTR, INT){nullptr};
  NOTIFYICONDATAW tray_data_{};
  KeyStateSet pressed_keys_{};
  HWND cached_active_window_{nullptr};
  std::uint32_t cached_process_id_{};
  std::uint8_t modifier_state_{};
  bool caps_lock_{false};
  bool pointer_registration_desired_{false};
  bool pointer_registered_{false};
  bool tray_added_{false};
  bool toggle_chord_active_{false};
  bool toggle_chord_contaminated_{false};
  bool stopping_{false};
  NativeRuntimeStartupStage startup_stage_{NativeRuntimeStartupStage::None};
  DWORD startup_error_{};
  std::uint64_t processed_keyboard_events_{};

  static Win32InputRuntime* active_runtime_;
};

[[nodiscard]] RuntimeInputProfile DefaultRuntimeInputProfile() noexcept;
[[nodiscard]] RuntimeInputProfile LoadRuntimeInputProfileOrDefault() noexcept;
[[nodiscard]] NativeResidentResourceSnapshot MeasureNativeResidentResources(
    Win32InputRuntime& runtime,
    DWORD duration_milliseconds,
    std::uint32_t baseline_thread_count) noexcept;

}  // namespace keyina::windows
