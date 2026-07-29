#pragma once

#include <windows.h>

#include <msctf.h>

#include <atomic>
#include <cstdint>
#include <deque>
#include <memory>
#include <mutex>
#include <string>
#include <string_view>

#include <keyina/engine.h>
#include <keyina/ipc_protocol.h>
#include <keyina/tsf/identifiers.h>
#include <keyina/tsf/key_router.h>
#include <keyina/tsf/pipe_client.h>

#include "activation_message_window.h"
#include "key_edit_session.h"

namespace keyina::tsf {

class TextService final : public ITfTextInputProcessorEx,
                          public ITfKeyEventSink,
                          public ITfCompositionSink
#if defined(KEYINA_TSF_TEST_HOOKS)
                          , public IKeyinaTsfTestControl
#endif
{
 public:
  TextService() noexcept;

  TextService(const TextService&) = delete;
  TextService& operator=(const TextService&) = delete;

  HRESULT STDMETHODCALLTYPE QueryInterface(REFIID interface_id,
                                           void** object) override;
  ULONG STDMETHODCALLTYPE AddRef() override;
  ULONG STDMETHODCALLTYPE Release() override;

  HRESULT STDMETHODCALLTYPE Activate(ITfThreadMgr* thread_manager,
                                     TfClientId client_id) override;
  HRESULT STDMETHODCALLTYPE Deactivate() override;
  HRESULT STDMETHODCALLTYPE ActivateEx(ITfThreadMgr* thread_manager,
                                       TfClientId client_id,
                                       DWORD flags) override;

  HRESULT STDMETHODCALLTYPE OnSetFocus(BOOL foreground) override;
  HRESULT STDMETHODCALLTYPE OnTestKeyDown(ITfContext* context,
                                          WPARAM virtual_key,
                                          LPARAM key_data,
                                          BOOL* eaten) override;
  HRESULT STDMETHODCALLTYPE OnTestKeyUp(ITfContext* context,
                                        WPARAM virtual_key,
                                        LPARAM key_data,
                                        BOOL* eaten) override;
  HRESULT STDMETHODCALLTYPE OnKeyDown(ITfContext* context,
                                      WPARAM virtual_key,
                                      LPARAM key_data,
                                      BOOL* eaten) override;
  HRESULT STDMETHODCALLTYPE OnKeyUp(ITfContext* context,
                                    WPARAM virtual_key,
                                    LPARAM key_data,
                                    BOOL* eaten) override;
  HRESULT STDMETHODCALLTYPE OnPreservedKey(ITfContext* context,
                                           REFGUID command,
                                           BOOL* eaten) override;

  HRESULT STDMETHODCALLTYPE OnCompositionTerminated(
      TfEditCookie edit_cookie,
      ITfComposition* composition) override;

#if defined(KEYINA_TSF_TEST_HOOKS)
  HRESULT STDMETHODCALLTYPE GetFocusGeneration(
      ULONGLONG* generation) override;
  HRESULT STDMETHODCALLTYPE ApplyExternalText(
      ULONGLONG focus_generation,
      BSTR expected_suffix,
      BSTR insert_text) override;
  HRESULT STDMETHODCALLTYPE SetPipeNameForTests(BSTR pipe_name) override;
#endif

 private:
  friend class ExternalEditSession;
  friend class KeyEditSession;

  ~TextService();

  [[nodiscard]] KeyRoutingInput BuildRoutingInput(
      WPARAM virtual_key) const noexcept;
  HRESULT RequestRoute(ITfContext* context, KeyRoute route,
                       bool* applied) noexcept;
  HRESULT ApplyRoute(ITfContext* context, TfEditCookie edit_cookie,
                     const KeyRoute& route);
  HRESULT EnsureComposition(ITfContext* context,
                            TfEditCookie edit_cookie);
  HRESULT ApplyEngineEdit(ITfContext* context, TfEditCookie edit_cookie,
                          const TextEdit& edit,
                          std::u32string_view previous_visible);
  HRESULT RequestExternalText(std::uint64_t focus_generation,
                              std::wstring expected_suffix,
                              std::wstring insert_text,
                              bool* applied) noexcept;
  HRESULT ApplyExternalTextInSession(ITfContext* context,
                                     TfEditCookie edit_cookie,
                                     std::uint64_t focus_generation,
                                     std::wstring_view expected_suffix,
                                     std::wstring_view insert_text);
  HRESULT GetFocusedContext(ITfContext** context) const noexcept;
  [[nodiscard]] bool StartIpc() noexcept;
  void StopIpc() noexcept;
  void UpdateIpcFocus(bool focused) noexcept;
  void QueueExternalEnvelope(ipc::Envelope envelope) noexcept;
  void DrainExternalEnvelopes() noexcept;
  void ApplyExternalEnvelope(const ipc::Envelope& envelope) noexcept;
  [[nodiscard]] static bool Utf8ToWide(
      std::string_view value,
      std::wstring& output) noexcept;
  [[nodiscard]] static ipc::SessionId CreateSessionId() noexcept;
  [[nodiscard]] static std::wstring DefaultPipeName() noexcept;
  HRESULT InsertBoundary(ITfContext* context, TfEditCookie edit_cookie,
                         char32_t character);
  HRESULT EndComposition(TfEditCookie edit_cookie) noexcept;
  HRESULT SetCaretAtCompositionEnd(ITfContext* context,
                                   TfEditCookie edit_cookie);
  void AbandonComposition() noexcept;
  void ReleaseActivationState() noexcept;
  static HRESULT PassThrough(BOOL* eaten) noexcept;

  std::atomic<ULONG> reference_count_{1};
  ITfThreadMgr* thread_manager_{nullptr};
  ITfKeystrokeMgr* keystroke_manager_{nullptr};
  ITfComposition* composition_{nullptr};
  ITfContext* composition_context_{nullptr};
  TfClientId client_id_{TF_CLIENTID_NULL};
  bool key_sink_advised_{false};
  bool secure_mode_{false};
  bool input_enabled_{true};
  bool foreground_{false};
  std::atomic<std::uint64_t> focus_generation_{0};
  ipc::SessionId ipc_session_id_{};
  std::unique_ptr<PipeClient> pipe_client_;
  ActivationMessageWindow command_window_;
  std::mutex external_queue_mutex_;
  std::deque<ipc::Envelope> external_queue_;
#if defined(KEYINA_TSF_TEST_HOOKS)
  std::wstring test_pipe_name_;
#endif
  Engine engine_;
};

}  // namespace keyina::tsf
