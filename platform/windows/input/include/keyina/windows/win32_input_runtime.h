#pragma once

#include <keyina/windows/bounded_spsc_queue.h>
#include <keyina/windows/clipboard_privacy.h>
#include <keyina/windows/keystroke_overlay_model.h>
#include <keyina/windows/keystroke_overlay_positioner.h>
#include <keyina/windows/keystroke_overlay_sound.h>
#include <keyina/windows/keystroke_overlay_window.h>
#include <keyina/windows/native_latency_histogram.h>
#include <keyina/windows/resident_input_controller.h>
#include <keyina/windows/runtime_hotkeys.h>

#include <windows.h>
#include <ole2.h>
#include <shellapi.h>

#include <array>
#include <atomic>
#include <cstddef>
#include <cstdint>
#include <memory>
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
      bool profile_callback_latency = false,
      ULONG_PTR accepted_input_marker = 0,
      bool force_selection_replacement_for_self_test = false) noexcept;
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

  [[nodiscard]] std::uint64_t dropped_external_command_count()
      const noexcept {
    return dropped_external_command_count_.load(std::memory_order_relaxed);
  }

  [[nodiscard]] std::uint64_t clipboard_privacy_write_count()
      const noexcept {
    return clipboard_privacy_write_count_;
  }

  [[nodiscard]] std::uint64_t clipboard_privacy_failure_count()
      const noexcept {
    return clipboard_privacy_failure_count_;
  }

  [[nodiscard]] std::uint64_t deferred_clipboard_queue_full_count()
      const noexcept {
    return deferred_clipboard_queue_full_count_;
  }

  [[nodiscard]] std::uint64_t deferred_clipboard_fallback_count()
      const noexcept {
    return deferred_clipboard_fallback_count_;
  }

  [[nodiscard]] std::uint64_t deferred_literal_injection_count()
      const noexcept {
    return deferred_literal_injection_count_;
  }

  [[nodiscard]] std::uint64_t deferred_virtual_key_injection_count()
      const noexcept {
    return deferred_virtual_key_injection_count_;
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
  enum class TargetInjectionResult : std::uint8_t {
    Succeeded = 0,
    FailedBeforeMutation,
    FailedAfterPossibleMutation,
  };

  enum class DeferredClipboardWorkKind : std::uint8_t {
    Transform = 0,
    Literal,
    VirtualKey,
    Command,
  };

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
  static DWORD WINAPI ExternalCommandWorkerProcedure(void* context) noexcept;

  LRESULT HandleWindowMessage(HWND window, UINT message, WPARAM w_param,
                              LPARAM l_param) noexcept;
  LRESULT HandleKeyboardEvent(int code, WPARAM message,
                              LPARAM data) noexcept;
  [[nodiscard]] TypingContext CaptureTypingContext() noexcept;
  [[nodiscard]] bool RequiresSelectionReplacementTarget(
      std::uintptr_t focus_window) noexcept;
  [[nodiscard]] bool Inject(
      const InputDecision& decision) noexcept;
  [[nodiscard]] bool InjectWithSelectionReplacement(
      const InputDecision& decision) noexcept;
  [[nodiscard]] TargetInjectionResult InjectViaClipboard(
      const InputDecision& decision,
      std::uint32_t target_process_id,
      std::uintptr_t target_focus_window) noexcept;
  [[nodiscard]] TargetInjectionResult InjectDeferredFallback(
      char32_t fallback_character,
      std::uint32_t target_process_id,
      std::uintptr_t target_focus_window) noexcept;
  [[nodiscard]] TargetInjectionResult InjectDeferredVirtualKey(
      std::uint16_t virtual_key,
      std::uint32_t target_process_id,
      std::uintptr_t target_focus_window) noexcept;
  [[nodiscard]] TargetInjectionResult InjectDeferredCommand(
      RuntimeSnippetCommand command,
      std::uint16_t backspace_count,
      std::u16string_view payload,
      std::uint32_t target_process_id,
      std::uintptr_t target_focus_window) noexcept;
  void RestorePendingClipboard() noexcept;
  [[nodiscard]] bool IsPointerResetPacket(HRAWINPUT input) noexcept;
  void RequestPointerRegistration(bool active) noexcept;
  void ApplyPointerRegistration() noexcept;
  void ProcessToggleGesture(const PhysicalKeyEvent& event) noexcept;
  void RequestSnippetOverlayUpdate() noexcept;
  void PublishKeystrokeOverlayEvent(
      const KeystrokeOverlayEvent& event) noexcept;
  void PublishKeystrokeOverlayEvent(
      KeystrokeOverlayEventKind kind,
      std::u16string_view text,
      char16_t token,
      std::uint64_t generation) noexcept;
  void RequestKeystrokeOverlayUpdate() noexcept;
  void UpdateKeystrokeOverlay() noexcept;
  void ClearKeystrokeOverlay() noexcept;
  void SuppressKeystrokeOverlay() noexcept;
  void UpdateKeystrokeOverlayComposition(
      const PhysicalKeyEvent& event,
      const TypingContext& context,
      const InputDecision& decision) noexcept;
  [[nodiscard]] KeystrokeOverlayPlacement ResolveKeystrokeOverlayPlacementForCurrentContext() noexcept;
  void RequestTrayUpdate() noexcept;
  [[nodiscard]] bool QueueDeferredClipboardInjection(
      const InputDecision& decision,
      char32_t fallback_character,
      std::uint16_t source_virtual_key,
      const TypingContext& context) noexcept;
  [[nodiscard]] bool QueueDeferredLiteralInjection(
      char32_t character,
      std::uint16_t source_virtual_key,
      const TypingContext& context) noexcept;
  [[nodiscard]] bool QueueDeferredVirtualKeyInjection(
      std::uint16_t virtual_key,
      const TypingContext& context) noexcept;
  [[nodiscard]] bool QueueDeferredCommandInjection(
      const InputDecision& decision,
      char32_t fallback_character,
      std::uint16_t source_virtual_key,
      const TypingContext& context) noexcept;
  void RequestDeferredClipboardDrain() noexcept;
  void HandleDeferredClipboardInjections() noexcept;
  [[nodiscard]] bool PrepareDeferredSnippetCommand(
      const InputDecision& decision) noexcept;
  [[nodiscard]] bool ReserveExternalSnippetCommand(
      std::u16string_view payload,
      std::uint32_t target_process_id,
      std::uintptr_t target_focus_window) noexcept;
  void CommitDeferredSnippetCommand(
      RuntimeSnippetCommand command) noexcept;
  void HandleDeferredSnippetActions() noexcept;
  void ExecuteSnippetAction(RuntimeSnippetCommand command) noexcept;
  [[nodiscard]] bool StartExternalCommandWorker() noexcept;
  void StopExternalCommandWorker() noexcept;
  DWORD RunExternalCommandWorker() noexcept;
  [[nodiscard]] bool IsDeferredTargetCurrent(
      std::uint32_t target_process_id,
      std::uintptr_t target_focus_window) const noexcept;
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
  ULONG_PTR accepted_input_marker_{};
  bool force_selection_replacement_for_self_test_{false};
  std::uint64_t performance_counter_frequency_{};
  NativeLatencyHistogram callback_latency_histogram_{};
  std::array<
      NativeLatencyHistogram,
      static_cast<std::size_t>(NativeCallbackLatencyStage::Count)>
      callback_stage_latency_histograms_{};
  HWND window_{nullptr};
  HWND snippet_overlay_window_{nullptr};
  bool snippet_overlay_visible_{false};
  KeystrokeOverlayReducer keystroke_overlay_reducer_{};
  KeystrokeOverlayWindow keystroke_overlay_window_{};
  KeystrokeOverlaySoundPlayer keystroke_overlay_sound_{};
  KeystrokeOverlayState keystroke_overlay_state_{};
  KeystrokeOverlayEvent pending_keystroke_overlay_event_{};
  BoundedKeystrokeOverlayText keystroke_overlay_composition_{};
  OverlayPoint keystroke_overlay_stable_anchor_{};
  HMONITOR keystroke_overlay_monitor_{nullptr};
  std::uint32_t keystroke_overlay_dpi_{96};
  std::uint64_t keystroke_overlay_generation_{};
  UINT_PTR keystroke_overlay_hide_timer_{};
  bool keystroke_overlay_update_posted_{false};
  bool keystroke_overlay_has_stable_anchor_{false};
  HHOOK hook_{nullptr};
  HICON active_icon_{nullptr};
  HICON inactive_icon_{nullptr};
  HMODULE shell_module_{nullptr};
  BOOL(WINAPI* shell_notify_icon_)(DWORD, PNOTIFYICONDATAW){nullptr};
  HINSTANCE(WINAPI* shell_execute_)(
      HWND, LPCWSTR, LPCWSTR, LPCWSTR, LPCWSTR, INT){nullptr};
  NOTIFYICONDATAW tray_data_{};
  KeyStateSet pressed_keys_{};
  KeyStateSet owned_text_keys_{};
  HWND cached_active_window_{nullptr};
  HWND cached_focus_window_{nullptr};
  HWND cached_selection_replacement_target_window_{nullptr};
  std::uint32_t cached_process_id_{};
  std::uint64_t cached_application_hash_{};
  bool cached_selection_replacement_target_{};
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
  std::wstring pending_clipboard_text_{};
  IDataObject* pending_clipboard_data_object_{nullptr};
  DWORD pending_clipboard_sequence_{};
  bool pending_clipboard_text_present_{false};
  bool ole_initialized_{false};
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
  std::uint64_t clipboard_privacy_write_count_{};
  std::uint64_t clipboard_privacy_failure_count_{};
  ClipboardPrivacyFormats clipboard_privacy_formats_{};
  bool snippet_overlay_update_posted_{false};
  bool tray_update_posted_{false};
  bool deferred_clipboard_message_posted_{false};
  std::atomic_uint32_t pending_snippet_actions_{};

  struct DeferredClipboardWorkItem {
    std::array<char16_t, kMaximumRuntimeSnippetExpansionUtf8Bytes>
        text_storage{};
    DeferredClipboardWorkKind kind{DeferredClipboardWorkKind::Transform};
    std::size_t text_units{};
    std::uint16_t backspace_count{};
    std::uint16_t source_virtual_key{};
    char32_t fallback_character{};
    std::uint32_t target_process_id{};
    std::uintptr_t target_focus_window{};
    RuntimeSnippetCommand snippet_command{RuntimeSnippetCommand::None};
  };
  // Sixty-four queued keystrokes cover large SendInput bursts before the
  // posted transaction message runs. The queue is heap-backed once at Start,
  // so this adds no callback allocation and stays within the 10 MiB budget.
  static constexpr std::size_t kDeferredClipboardQueueStorage = 65;
  using DeferredClipboardQueue = BoundedSpscQueue<
      DeferredClipboardWorkItem,
      kDeferredClipboardQueueStorage>;
  std::unique_ptr<DeferredClipboardQueue> deferred_clipboard_queue_{};
  std::uint64_t deferred_clipboard_queue_full_count_{};
  std::uint64_t deferred_clipboard_fallback_count_{};
  std::uint64_t deferred_literal_injection_count_{};
  std::uint64_t deferred_virtual_key_injection_count_{};

  struct ExternalSnippetWorkItem {
    std::array<char16_t, kMaximumRuntimeSnippetExpansionUtf8Bytes> payload{};
    std::size_t payload_units{};
    std::uint32_t target_process_id{};
    std::uintptr_t target_focus_window{};
  };
  static constexpr std::size_t kExternalCommandQueueStorage = 5;
  BoundedSpscQueue<
      ExternalSnippetWorkItem,
      kExternalCommandQueueStorage> external_command_queue_{};
  HANDLE external_command_event_{nullptr};
  HANDLE external_command_stop_event_{nullptr};
  HANDLE external_command_thread_{nullptr};
  std::atomic_uint64_t dropped_external_command_count_{};

  static Win32InputRuntime* active_runtime_;
};

[[nodiscard]] RuntimeInputProfile LoadRuntimeInputProfileOrDefault() noexcept;
[[nodiscard]] NativeResidentResourceSnapshot MeasureNativeResidentResources(
    Win32InputRuntime& runtime,
    DWORD duration_milliseconds,
    std::uint32_t baseline_thread_count) noexcept;

}  // namespace keyina::windows
