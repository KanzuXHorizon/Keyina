#pragma once

#include <windows.h>

#include <msctf.h>

#include <atomic>

#include <keyina/tsf/key_router.h>

namespace keyina::tsf {

class TextService;

class KeyEditSession final : public ITfEditSession {
 public:
  KeyEditSession(TextService* service, ITfContext* context,
                 KeyRoute route) noexcept;

  KeyEditSession(const KeyEditSession&) = delete;
  KeyEditSession& operator=(const KeyEditSession&) = delete;

  HRESULT STDMETHODCALLTYPE QueryInterface(REFIID interface_id,
                                           void** object) override;
  ULONG STDMETHODCALLTYPE AddRef() override;
  ULONG STDMETHODCALLTYPE Release() override;
  HRESULT STDMETHODCALLTYPE DoEditSession(TfEditCookie edit_cookie) override;

 private:
  ~KeyEditSession();

  std::atomic<ULONG> reference_count_{1};
  TextService* service_;
  ITfContext* context_;
  KeyRoute route_;
};

}  // namespace keyina::tsf
