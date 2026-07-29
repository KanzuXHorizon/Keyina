#include "external_edit_session.h"

#include <utility>

#include "module_state.h"
#include "text_service.h"

namespace keyina::tsf {

ExternalEditSession::ExternalEditSession(
    TextService* service,
    ITfContext* context,
    std::uint64_t focus_generation,
    std::wstring expected_suffix,
    std::wstring insert_text) noexcept
    : service_(service),
      context_(context),
      focus_generation_(focus_generation),
      expected_suffix_(std::move(expected_suffix)),
      insert_text_(std::move(insert_text)) {
  ModuleObjectCreated();
  service_->AddRef();
  context_->AddRef();
}

ExternalEditSession::~ExternalEditSession() {
  context_->Release();
  service_->Release();
  ModuleObjectDestroyed();
}

HRESULT ExternalEditSession::QueryInterface(REFIID interface_id,
                                            void** object) {
  if (object == nullptr) {
    return E_POINTER;
  }
  *object = nullptr;
  if (!IsEqualIID(interface_id, IID_IUnknown) &&
      !IsEqualIID(interface_id, IID_ITfEditSession)) {
    return E_NOINTERFACE;
  }
  *object = static_cast<ITfEditSession*>(this);
  AddRef();
  return S_OK;
}

ULONG ExternalEditSession::AddRef() { return ++reference_count_; }

ULONG ExternalEditSession::Release() {
  const ULONG remaining = --reference_count_;
  if (remaining == 0) {
    delete this;
  }
  return remaining;
}

HRESULT ExternalEditSession::DoEditSession(TfEditCookie edit_cookie) {
  return service_->ApplyExternalTextInSession(
      context_,
      edit_cookie,
      focus_generation_,
      expected_suffix_,
      insert_text_);
}

}  // namespace keyina::tsf
