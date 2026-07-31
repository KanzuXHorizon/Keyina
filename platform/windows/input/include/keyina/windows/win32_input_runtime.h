#pragma once

#include <keyina/windows/native_latency_histogram.h>
#include <keyina/windows/resident_input_controller.h>
#include <keyina/windows/runtime_hotkeys.h>

#include <windows.h>
#include <shellapi.h>

#include <array>
#include <cstdint>
#include <string>

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
  explicit Win32InputRuntime(
      RuntimeInputProfile profile,
      bool enable_tray,
      bool reload_profiles = true,
      bool profile_callback_latency = false) noexcept;
  ~Win32InputRuntime();

  Win32InputRuntime(const Win32InputRuntime&) = delete;
  Win32InputRuntime& operator=(const Win32InputRuntime&) = delete;

  [[nodiscard]] bool Start() noexcept;
  int Run() noexcept;
  void Stop() noexcept;
  void PumpMessagesFor(DWORD duration_milliseconds) noexcept;
  void RequestOpenSettings() noexcept;

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

  [[nodiscard]] std::uint64_t suppressed_edit_count() const noexcept {
    return suppressed_edit_count_;
  }

  [[nodiscard]] std::uint64_t successful_injection_count() const noexcept {
    return successful_injection_count_;
  }

  [[nodiscard]] std::uint64_t failed_injection_count() const noexcept {
    return failed_injection_count_;
  }

  [[nodiscard]] std::uint64_t bypass_context_count() const noexcept {
    return bypass_context_count_;
  }

  [[nodiscard]] std::uint64_t context_change_count() const noexcept {
    return context_change_count_;
  }

  [[nodiscard]] std::uint64_t pointer_reset_count() const noexcept {
    return pointer_reset_count_;
  }

  [[nodiscard]] std::uint64_t standard_edit_replace_count() const noexcept {
    return standard_edit_replace_count_;
  }

  [[nodiscard]] std::uint64_t typing_context_capture_count() const noexcept {
    return typing_context_capture_count_;
  }

  [[nodiscard]] NativeLatencySnapshot callback_latency_snapshot()
      const noexcept {
    return callback_latency_histogram_.Snapshot();
  }

  [[nodiscard]] NativeLatencySnapshot callback_stage_latency_snapshot(
      NativeCallbackLatencyStage stage) const noexcept {
    const auto index = static_cast<std::size_t>(stage);
    return index < callback_stage_latency_histograms_.size()
               ? callback_stage_latency_histograms_[index].Snapshot()
               : NativeLatencySnapshot{};
  }

  void ClearCallbackLatency() noexcept {
    callback_latency_histogram_.Clear();
    for (auto& histogram : callback_stage_latency_histograms_) {
      histogram.Clear();
    }
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
  [[nodiscard]] bool IsDeferredInputTarget(
      std::uintptr_t focus_window) noexcept;
  [[nodiscard]] bool Inject(
      const InputDecision& decision,
      std::uintptr_t target_focus_window = 0) noexcept;
  [[nodiscard]] bool InjectWithSelectionReplacement(
      const InputDecision& decision) noexcept;
  [[nodiscard]] bool InjectViaClipboard(
      const InputDecision& decision,
      std::uintptr_t target_focus_window) noexcept;
  [[nodiscard]] bool QueueDeferredInput(
      const InputDecision& decision,
      const TypingContext& context,
      bool selection_replacement) noexcept;
  void ProcessDeferredInput() noexcept;
  void RestorePendingClipboard() noexcept;
  [[nodiscard]] bool IsPointerResetPacket(HRAWINPUT input) noexcept;
  void RequestPointerRegistration(bool active) noexcept;
  void ApplyPointerRegistration() noexcept;
  void ProcessToggleGesture(const PhysicalKeyEvent& event) noexcept;
  void HandleSnippetCommand(
      RuntimeSnippetCommand command,
      std::u16string_view payload,
      std::uint32_t target_process_id,
      std::uintptr_t target_focus_window) noexcept;
  [[nodiscard]] bool LaunchExternalSnippetCommand(
      std::u16string_view payload,
      std::uint32_t target_process_id,
      std::uintptr_t target_focus_window) noexcept;
  [[nodiscard]] bool QueueManagedCommand(RuntimeCommand command) noexcept;
  [[nodiscard]] bool LaunchManagedCommand(RuntimeCommand command) noexcept;
  [[nodiscard]] bool IsCommandCompanionActive() const noexcept;
  void ReloadProfileIfChanged() noexcept;
  void RefreshModifierState() noexcept;
  void UpdateSnippetOverlay() noexcept;
  void HideSnippetOverlay() noexcept;
  void UpdateTray() noexcept;
  void ShowTrayMenu() noexcept;
  void OpenManagedSettings() noexcept;
  void RequestExit() noexcept;

  RuntimeInputProfile profile_{};
  ResidentInputController controller_;
  RuntimeHotkeyRouter hotkey_router_;
  bool enable_tray_{false};
  bool reload_profiles_{true};
  bool profile_callback_latency_{false};
  std::uint64_t performance_counter_frequency_{};
  NativeLatencyHistogram callback_latency_histogram_{};
  std::array<
      NativeLatencyHistogram,
      static_cast<std::size_t>(NativeCallbackLatencyStage::Count)>
      callback_stage_latency_histograms_{};
  HWND window_{nullptr};
  HWND snippet_overlay_window_{nullptr};
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
  HWND cached_focus_window_{nullptr};
  HWND cached_deferred_target_window_{nullptr};
  std::uint32_t cached_process_id_{};
  std::uint64_t cached_application_hash_{};
  bool cached_deferred_target_{};
  std::uint8_t modifier_state_{};
  bool caps_lock_{false};
  bool pointer_registration_desired_{false};
  bool pointer_registered_{false};
  bool tray_added_{false};
  bool toggle_chord_active_{false};
  bool toggle_chord_contaminated_{false};
  bool stopping_{false};
  FILETIME profile_write_time_{};
  bool profile_write_time_known_{false};
  FILETIME snippet_profile_write_time_{};
  bool snippet_profile_write_time_known_{false};
  UINT_PTR profile_timer_{};
  UINT_PTR clipboard_restore_timer_{};
  InputDecision pending_input_decision_{};
  TypingContext pending_input_context_{};
  std::u16string pending_input_extended_insert_{};
  bool pending_input_available_{false};
  bool pending_input_selection_replacement_{false};
  std::wstring pending_clipboard_text_{};
  DWORD pending_clipboard_sequence_{};
  bool pending_clipboard_text_present_{false};
  NativeRuntimeStartupStage startup_stage_{NativeRuntimeStartupStage::None};
  DWORD startup_error_{};
  std::uint64_t processed_keyboard_events_{};
  std::uint64_t suppressed_edit_count_{};
  std::uint64_t successful_injection_count_{};
  std::uint64_t failed_injection_count_{};
  std::uint64_t bypass_context_count_{};
  TypingContext last_key_down_context_{};
  bool last_key_down_context_known_{false};
  std::uint64_t context_change_count_{};
  std::uint64_t pointer_reset_count_{};
  std::uint64_t standard_edit_replace_count_{};
  std::uint64_t typing_context_capture_count_{};

  static Win32InputRuntime* active_runtime_;
};

[[nodiscard]] RuntimeInputProfile LoadRuntimeInputProfileOrDefault() noexcept;
[[nodiscard]] NativeResidentResourceSnapshot MeasureNativeResidentResources(
    Win32InputRuntime& runtime,
    DWORD duration_milliseconds,
    std::uint32_t baseline_thread_count) noexcept;

}  // namespace keyina::windows
