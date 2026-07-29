#pragma once

#include <windows.h>

#include <msctf.h>

#include <atomic>
#include <cstdint>
#include <string>

namespace keyina::tsf {

class TextService;

class ExternalEditSession final : public ITfEditSession {
 public:
  ExternalEditSession(TextService* service,
                      ITfContext* context,
                      std::uint64_t focus_generation,
                      std::wstring expected_suffix,
                      std::wstring insert_text) noexcept;

  ExternalEditSession(const ExternalEditSession&) = delete;
  ExternalEditSession& operator=(const ExternalEditSession&) = delete;

  HRESULT STDMETHODCALLTYPE QueryInterface(REFIID interface_id,
                                           void** object) override;
  ULONG STDMETHODCALLTYPE AddRef() override;
  ULONG STDMETHODCALLTYPE Release() override;
  HRESULT STDMETHODCALLTYPE DoEditSession(TfEditCookie edit_cookie) override;

 private:
  ~ExternalEditSession();

  std::atomic<ULONG> reference_count_{1};
  TextService* service_;
  ITfContext* context_;
  std::uint64_t focus_generation_;
  std::wstring expected_suffix_;
  std::wstring insert_text_;
};

}  // namespace keyina::tsf
