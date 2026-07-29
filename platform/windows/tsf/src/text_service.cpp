#include "text_service.h"

#include <new>

#include "module_state.h"

namespace keyina::tsf {

TextService::TextService() noexcept { ModuleObjectCreated(); }

TextService::~TextService() {
  ReleaseActivationState();
  ModuleObjectDestroyed();
}

HRESULT TextService::QueryInterface(REFIID interface_id, void** object) {
  if (object == nullptr) {
    return E_POINTER;
  }
  *object = nullptr;

  if (IsEqualIID(interface_id, IID_IUnknown) ||
      IsEqualIID(interface_id, IID_ITfTextInputProcessor) ||
      IsEqualIID(interface_id, IID_ITfTextInputProcessorEx)) {
    *object = static_cast<ITfTextInputProcessorEx*>(this);
  } else if (IsEqualIID(interface_id, IID_ITfKeyEventSink)) {
    *object = static_cast<ITfKeyEventSink*>(this);
  } else {
    return E_NOINTERFACE;
  }

  AddRef();
  return S_OK;
}

ULONG TextService::AddRef() { return ++reference_count_; }

ULONG TextService::Release() {
  const ULONG remaining = --reference_count_;
  if (remaining == 0) {
    delete this;
  }
  return remaining;
}

HRESULT TextService::Activate(ITfThreadMgr* thread_manager,
                              TfClientId client_id) {
  return ActivateEx(thread_manager, client_id, 0);
}

HRESULT TextService::ActivateEx(ITfThreadMgr* thread_manager,
                                TfClientId client_id, DWORD flags) {
  if (thread_manager == nullptr || client_id == TF_CLIENTID_NULL) {
    return E_INVALIDARG;
  }

  ReleaseActivationState();
  thread_manager_ = thread_manager;
  thread_manager_->AddRef();
  client_id_ = client_id;
  secure_mode_ = (flags & TF_TMAE_SECUREMODE) != 0;

  if (secure_mode_) {
    return S_OK;
  }

  HRESULT result = thread_manager_->QueryInterface(
      IID_ITfKeystrokeMgr, reinterpret_cast<void**>(&keystroke_manager_));
  if (FAILED(result)) {
    ReleaseActivationState();
    return result;
  }

  result = keystroke_manager_->AdviseKeyEventSink(client_id_, this, TRUE);
  if (FAILED(result)) {
    ReleaseActivationState();
    return result;
  }
  key_sink_advised_ = true;
  return S_OK;
}

HRESULT TextService::Deactivate() {
  HRESULT result = S_OK;
  if (key_sink_advised_ && keystroke_manager_ != nullptr &&
      client_id_ != TF_CLIENTID_NULL) {
    result = keystroke_manager_->UnadviseKeyEventSink(client_id_);
  }
  key_sink_advised_ = false;
  if (keystroke_manager_ != nullptr) {
    keystroke_manager_->Release();
    keystroke_manager_ = nullptr;
  }
  if (thread_manager_ != nullptr) {
    thread_manager_->Release();
    thread_manager_ = nullptr;
  }
  client_id_ = TF_CLIENTID_NULL;
  secure_mode_ = false;
  engine_.Reset();
  return result;
}

HRESULT TextService::OnSetFocus(BOOL foreground) {
  if (!foreground) {
    engine_.Reset();
  }
  return S_OK;
}

HRESULT TextService::OnTestKeyDown(ITfContext* context, WPARAM virtual_key,
                                   LPARAM key_data, BOOL* eaten) {
  static_cast<void>(context);
  static_cast<void>(virtual_key);
  static_cast<void>(key_data);
  return PassThrough(eaten);
}

HRESULT TextService::OnTestKeyUp(ITfContext* context, WPARAM virtual_key,
                                 LPARAM key_data, BOOL* eaten) {
  static_cast<void>(context);
  static_cast<void>(virtual_key);
  static_cast<void>(key_data);
  return PassThrough(eaten);
}

HRESULT TextService::OnKeyDown(ITfContext* context, WPARAM virtual_key,
                               LPARAM key_data, BOOL* eaten) {
  static_cast<void>(context);
  static_cast<void>(virtual_key);
  static_cast<void>(key_data);
  return PassThrough(eaten);
}

HRESULT TextService::OnKeyUp(ITfContext* context, WPARAM virtual_key,
                             LPARAM key_data, BOOL* eaten) {
  static_cast<void>(context);
  static_cast<void>(virtual_key);
  static_cast<void>(key_data);
  return PassThrough(eaten);
}

HRESULT TextService::OnPreservedKey(ITfContext* context, REFGUID command,
                                    BOOL* eaten) {
  static_cast<void>(context);
  static_cast<void>(command);
  return PassThrough(eaten);
}

void TextService::ReleaseActivationState() noexcept {
  static_cast<void>(Deactivate());
}

HRESULT TextService::PassThrough(BOOL* eaten) noexcept {
  if (eaten == nullptr) {
    return E_POINTER;
  }
  *eaten = FALSE;
  return S_OK;
}

}  // namespace keyina::tsf
