#include "key_edit_session.h"

#include "module_state.h"
#include "text_service.h"

namespace keyina::tsf {

KeyEditSession::KeyEditSession(TextService* service, ITfContext* context,
                               KeyRoute route) noexcept
    : service_(service), context_(context), route_(route) {
  ModuleObjectCreated();
  service_->AddRef();
  context_->AddRef();
}

KeyEditSession::~KeyEditSession() {
  context_->Release();
  service_->Release();
  ModuleObjectDestroyed();
}

HRESULT KeyEditSession::QueryInterface(REFIID interface_id, void** object) {
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

ULONG KeyEditSession::AddRef() { return ++reference_count_; }

ULONG KeyEditSession::Release() {
  const ULONG remaining = --reference_count_;
  if (remaining == 0) {
    delete this;
  }
  return remaining;
}

HRESULT KeyEditSession::DoEditSession(TfEditCookie edit_cookie) {
  return service_->ApplyRoute(context_, edit_cookie, route_);
}

}  // namespace keyina::tsf
