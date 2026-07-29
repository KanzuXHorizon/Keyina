#pragma once

#include <windows.h>

#include <msctf.h>

#include <atomic>

#include <keyina/engine.h>

namespace keyina::tsf {

class TextService final : public ITfTextInputProcessorEx,
                          public ITfKeyEventSink {
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

 private:
  ~TextService();
  void ReleaseActivationState() noexcept;
  static HRESULT PassThrough(BOOL* eaten) noexcept;

  std::atomic<ULONG> reference_count_{1};
  ITfThreadMgr* thread_manager_{nullptr};
  ITfKeystrokeMgr* keystroke_manager_{nullptr};
  TfClientId client_id_{TF_CLIENTID_NULL};
  bool key_sink_advised_{false};
  bool secure_mode_{false};
  Engine engine_;
};

}  // namespace keyina::tsf
