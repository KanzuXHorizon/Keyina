#include "text_service.h"

#include <algorithm>
#include <array>
#include <cstring>
#include <limits>
#include <new>
#include <string>
#include <utility>
#include <vector>

#include <keyina/tsf/edit_translator.h>
#include <keyina/tsf/identifiers.h>

#include "external_edit_session.h"
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
  } else if (IsEqualIID(interface_id, IID_ITfCompositionSink)) {
    *object = static_cast<ITfCompositionSink*>(this);
#if defined(KEYINA_TSF_TEST_HOOKS)
  } else if (IsEqualIID(
                 interface_id,
                 __uuidof(IKeyinaTsfTestControl))) {
    *object = static_cast<IKeyinaTsfTestControl*>(this);
#endif
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
  foreground_ = false;
  input_enabled_ = true;
  focus_generation_.fetch_add(1, std::memory_order_relaxed);
  bool manual_key_dispatch = false;
#if defined(KEYINA_TSF_TEST_HOOKS)
  manual_key_dispatch = (flags & kManualKeyDispatchForTests) != 0;
#endif

  if (secure_mode_) {
    return S_OK;
  }

  static_cast<void>(StartIpc());
  if (manual_key_dispatch) {
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

  foreground_ = false;
  focus_generation_.fetch_add(1, std::memory_order_relaxed);
  UpdateIpcFocus(false);
  StopIpc();
  AbandonComposition();
  engine_.Reset();

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
  input_enabled_ = true;
  return result;
}

HRESULT TextService::OnSetFocus(BOOL foreground) {
  foreground_ = foreground != FALSE;
  focus_generation_.fetch_add(1, std::memory_order_relaxed);
  UpdateIpcFocus(foreground_);
  if (!foreground_) {
    bool applied = false;
    if (composition_context_ != nullptr) {
      static_cast<void>(RequestRoute(
          composition_context_, {KeyRouteKind::Reset, U'\0'}, &applied));
    }
    if (!applied) {
      engine_.Reset();
      AbandonComposition();
    }
  }
  return S_OK;
}

HRESULT TextService::OnTestKeyDown(ITfContext* context, WPARAM virtual_key,
                                   LPARAM key_data, BOOL* eaten) {
  static_cast<void>(key_data);
  if (eaten == nullptr) {
    return E_POINTER;
  }
  if (context == nullptr || secure_mode_ || !input_enabled_) {
    *eaten = FALSE;
    return S_OK;
  }

  const KeyRoute route = RouteKey(BuildRoutingInput(virtual_key));
  *eaten = route.kind == KeyRouteKind::PassThrough ? FALSE : TRUE;
  return S_OK;
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
  static_cast<void>(key_data);
  if (eaten == nullptr) {
    return E_POINTER;
  }
  *eaten = FALSE;
  if (context == nullptr || secure_mode_) {
    return S_OK;
  }

  focus_generation_.fetch_add(1, std::memory_order_relaxed);
  UpdateIpcFocus(foreground_);
  if (!input_enabled_) {
    return S_OK;
  }

  const KeyRoute route = RouteKey(BuildRoutingInput(virtual_key));
  if (route.kind == KeyRouteKind::PassThrough) {
    return S_OK;
  }

  bool applied = false;
  static_cast<void>(RequestRoute(context, route, &applied));
  if (route.kind != KeyRouteKind::Reset && applied) {
    *eaten = TRUE;
  }
  return S_OK;
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

HRESULT TextService::OnCompositionTerminated(
    TfEditCookie edit_cookie, ITfComposition* composition) {
  static_cast<void>(edit_cookie);
  if (composition_ != nullptr && composition_ == composition) {
    composition_->Release();
    composition_ = nullptr;
    if (composition_context_ != nullptr) {
      composition_context_->Release();
      composition_context_ = nullptr;
    }
  }
  engine_.Reset();
  return S_OK;
}

#if defined(KEYINA_TSF_TEST_HOOKS)
HRESULT TextService::GetFocusGeneration(ULONGLONG* generation) {
  if (generation == nullptr) {
    return E_POINTER;
  }
  *generation = focus_generation_.load(std::memory_order_relaxed);
  return S_OK;
}

HRESULT TextService::ApplyExternalText(
    ULONGLONG focus_generation,
    BSTR expected_suffix,
    BSTR insert_text) {
  if (secure_mode_) {
    return E_ACCESSDENIED;
  }
  if (insert_text == nullptr || SysStringLen(insert_text) == 0) {
    return E_INVALIDARG;
  }

  const UINT suffix_length =
      expected_suffix == nullptr ? 0U : SysStringLen(expected_suffix);
  const UINT insert_length = SysStringLen(insert_text);
  if (suffix_length > 256U || insert_length > 32'768U) {
    return E_INVALIDARG;
  }

  return RequestExternalText(
      focus_generation,
      std::wstring(expected_suffix == nullptr ? L"" : expected_suffix,
                   suffix_length),
      std::wstring(insert_text, insert_length),
      nullptr);
}

HRESULT TextService::SetPipeNameForTests(BSTR pipe_name) {
  if (client_id_ != TF_CLIENTID_NULL) {
    return E_UNEXPECTED;
  }
  if (pipe_name == nullptr || SysStringLen(pipe_name) == 0 ||
      SysStringLen(pipe_name) > 240) {
    return E_INVALIDARG;
  }
  std::wstring value(pipe_name, SysStringLen(pipe_name));
  if (value.find_first_of(L"\\/:") != std::wstring::npos) {
    return E_INVALIDARG;
  }
  test_pipe_name_ = std::move(value);
  return S_OK;
}
#endif

bool TextService::StartIpc() noexcept {
  if (secure_mode_ || pipe_client_ != nullptr) {
    return false;
  }
  if (!command_window_.Create([this] { DrainExternalEnvelopes(); })) {
    return false;
  }

  ipc_session_id_ = CreateSessionId();
  bool has_session_data = false;
  for (const std::uint8_t value : ipc_session_id_.bytes) {
    has_session_data = has_session_data || value != 0;
  }
  if (!has_session_data) {
    command_window_.Destroy();
    return false;
  }

  std::wstring pipe_name = DefaultPipeName();
#if defined(KEYINA_TSF_TEST_HOOKS)
  if (!test_pipe_name_.empty()) {
    pipe_name = test_pipe_name_;
  }
#endif
  if (pipe_name.empty()) {
    command_window_.Destroy();
    return false;
  }

  try {
    pipe_client_ = std::make_unique<PipeClient>(
        std::move(pipe_name), ipc_session_id_,
        [this](ipc::Envelope envelope) {
          QueueExternalEnvelope(std::move(envelope));
        });
  } catch (...) {
    command_window_.Destroy();
    return false;
  }
  if (!pipe_client_->Start()) {
    pipe_client_.reset();
    command_window_.Destroy();
    return false;
  }
  UpdateIpcFocus(foreground_);
  return true;
}

void TextService::StopIpc() noexcept {
  if (pipe_client_ != nullptr) {
    pipe_client_->SetFocused(false,
                             focus_generation_.load(std::memory_order_relaxed));
    pipe_client_->Stop();
    pipe_client_.reset();
  }
  {
    std::lock_guard lock(external_queue_mutex_);
    external_queue_.clear();
  }
  command_window_.Destroy();
}

void TextService::UpdateIpcFocus(bool focused) noexcept {
  if (pipe_client_ != nullptr) {
    pipe_client_->SetFocused(
        focused,
        focus_generation_.load(std::memory_order_relaxed));
  }
}

void TextService::QueueExternalEnvelope(ipc::Envelope envelope) noexcept {
  try {
    {
      std::lock_guard lock(external_queue_mutex_);
      constexpr std::size_t kMaximumQueuedEnvelopes = 32;
      if (external_queue_.size() >= kMaximumQueuedEnvelopes) {
        return;
      }
      external_queue_.push_back(std::move(envelope));
    }
    static_cast<void>(command_window_.Post());
  } catch (...) {
    // IPC commands are optional and must never affect ordinary typing.
  }
}

void TextService::DrainExternalEnvelopes() noexcept {
  while (true) {
    ipc::Envelope envelope;
    {
      std::lock_guard lock(external_queue_mutex_);
      if (external_queue_.empty()) {
        return;
      }
      envelope = std::move(external_queue_.front());
      external_queue_.pop_front();
    }
    ApplyExternalEnvelope(envelope);
  }
}

void TextService::ApplyExternalEnvelope(
    const ipc::Envelope& envelope) noexcept {
  if (secure_mode_ || envelope.session_id != ipc_session_id_ ||
      envelope.focus_generation !=
          focus_generation_.load(std::memory_order_relaxed)) {
    return;
  }

  if (envelope.message_type == ipc::MessageType::ToggleInput) {
    if (envelope.payload == "enabled") {
      input_enabled_ = true;
    } else if (envelope.payload == "disabled") {
      input_enabled_ = false;
      engine_.Reset();
      AbandonComposition();
    }
    return;
  }

  std::wstring expected_suffix;
  std::wstring insert_text;
  if (envelope.message_type == ipc::MessageType::FinalTranscript) {
    if (envelope.payload.find('\0') != std::string::npos ||
        !Utf8ToWide(envelope.payload, insert_text)) {
      return;
    }
  } else if (envelope.message_type == ipc::MessageType::SnippetExpansion) {
    const std::size_t separator = envelope.payload.find('\0');
    if (separator == std::string::npos ||
        envelope.payload.find('\0', separator + 1) != std::string::npos ||
        !Utf8ToWide(std::string_view(envelope.payload).substr(0, separator),
                    expected_suffix) ||
        !Utf8ToWide(std::string_view(envelope.payload).substr(separator + 1),
                    insert_text)) {
      return;
    }
  } else {
    return;
  }

  if (insert_text.empty() || expected_suffix.size() > 256U ||
      insert_text.size() > 32'768U) {
    return;
  }
  static_cast<void>(RequestExternalText(
      envelope.focus_generation,
      std::move(expected_suffix),
      std::move(insert_text),
      nullptr));
}

bool TextService::Utf8ToWide(
    std::string_view value,
    std::wstring& output) noexcept {
  output.clear();
  if (value.empty()) {
    return true;
  }
  if (value.size() > static_cast<std::size_t>((std::numeric_limits<int>::max)())) {
    return false;
  }
  const int required = MultiByteToWideChar(
      CP_UTF8, MB_ERR_INVALID_CHARS,
      value.data(), static_cast<int>(value.size()), nullptr, 0);
  if (required <= 0) {
    return false;
  }
  try {
    output.resize(static_cast<std::size_t>(required));
  } catch (...) {
    return false;
  }
  return MultiByteToWideChar(
             CP_UTF8, MB_ERR_INVALID_CHARS,
             value.data(), static_cast<int>(value.size()),
             output.data(), required) == required;
}

ipc::SessionId TextService::CreateSessionId() noexcept {
  ipc::SessionId session_id{};
  GUID guid{};
  if (SUCCEEDED(CoCreateGuid(&guid))) {
    static_assert(sizeof(guid) == session_id.bytes.size());
    std::memcpy(session_id.bytes.data(), &guid, sizeof(guid));
  }
  return session_id;
}

std::wstring TextService::DefaultPipeName() noexcept {
  DWORD session_id = 0;
  if (!ProcessIdToSessionId(GetCurrentProcessId(), &session_id)) {
    return {};
  }
  try {
    return L"Keyina.Host.v1.s" + std::to_wstring(session_id);
  } catch (...) {
    return {};
  }
}

KeyRoutingInput TextService::BuildRoutingInput(WPARAM virtual_key) const
    noexcept {
  return KeyRoutingInput{
      .virtual_key = static_cast<std::uint32_t>(virtual_key),
      .shift = (GetKeyState(VK_SHIFT) & 0x8000) != 0,
      .caps_lock = (GetKeyState(VK_CAPITAL) & 0x0001) != 0,
      .control = (GetKeyState(VK_CONTROL) & 0x8000) != 0,
      .alt = (GetKeyState(VK_MENU) & 0x8000) != 0,
      .windows = (GetKeyState(VK_LWIN) & 0x8000) != 0 ||
                 (GetKeyState(VK_RWIN) & 0x8000) != 0,
      .active_composition = !engine_.RawKeys().empty(),
  };
}

HRESULT TextService::RequestExternalText(
    std::uint64_t focus_generation,
    std::wstring expected_suffix,
    std::wstring insert_text,
    bool* applied) noexcept {
  if (applied != nullptr) {
    *applied = false;
  }
  if (secure_mode_) {
    return E_ACCESSDENIED;
  }
  if (client_id_ == TF_CLIENTID_NULL || insert_text.empty()) {
    return E_INVALIDARG;
  }
  if (focus_generation !=
      focus_generation_.load(std::memory_order_relaxed)) {
    return S_FALSE;
  }

  ITfContext* context = nullptr;
  HRESULT result = GetFocusedContext(&context);
  if (FAILED(result) || context == nullptr) {
    return FAILED(result) ? result : S_FALSE;
  }

  auto* session = new (std::nothrow) ExternalEditSession(
      this,
      context,
      focus_generation,
      std::move(expected_suffix),
      std::move(insert_text));
  if (session == nullptr) {
    context->Release();
    return E_OUTOFMEMORY;
  }

  HRESULT session_result = E_FAIL;
  result = context->RequestEditSession(
      client_id_,
      session,
      TF_ES_SYNC | TF_ES_READWRITE,
      &session_result);
  context->Release();
  session->Release();

  if (FAILED(result)) {
    return result;
  }
  if (session_result == S_OK && applied != nullptr) {
    *applied = true;
  }
  return session_result;
}

HRESULT TextService::ApplyExternalTextInSession(
    ITfContext* context,
    TfEditCookie edit_cookie,
    std::uint64_t focus_generation,
    std::wstring_view expected_suffix,
    std::wstring_view insert_text) {
  if (context == nullptr) {
    return E_POINTER;
  }
  if (secure_mode_) {
    return E_ACCESSDENIED;
  }
  if (focus_generation !=
      focus_generation_.load(std::memory_order_relaxed)) {
    return S_FALSE;
  }
  if (insert_text.empty() ||
      insert_text.size() >
          static_cast<std::size_t>(std::numeric_limits<LONG>::max()) ||
      expected_suffix.size() >
          static_cast<std::size_t>(std::numeric_limits<LONG>::max())) {
    return E_INVALIDARG;
  }

  TF_SELECTION selection{};
  ULONG fetched = 0;
  HRESULT result = context->GetSelection(
      edit_cookie, TF_DEFAULT_SELECTION, 1, &selection, &fetched);
  if (FAILED(result) || fetched != 1 || selection.range == nullptr) {
    return FAILED(result) ? result : E_FAIL;
  }

  BOOL empty = FALSE;
  result = selection.range->IsEmpty(edit_cookie, &empty);
  if (FAILED(result) || !empty) {
    selection.range->Release();
    return FAILED(result) ? result : S_FALSE;
  }

  if (!expected_suffix.empty()) {
    const LONG requested = -static_cast<LONG>(expected_suffix.size());
    LONG shifted = 0;
    result = selection.range->ShiftStart(
        edit_cookie, requested, &shifted, nullptr);
    if (FAILED(result) || shifted != requested) {
      selection.range->Release();
      return FAILED(result) ? result : S_FALSE;
    }

    std::vector<WCHAR> observed(expected_suffix.size());
    ULONG observed_length = 0;
    result = selection.range->GetText(
        edit_cookie,
        0,
        observed.data(),
        static_cast<ULONG>(observed.size()),
        &observed_length);
    if (FAILED(result) ||
        observed_length != expected_suffix.size() ||
        !std::equal(observed.begin(), observed.end(),
                    expected_suffix.begin(), expected_suffix.end())) {
      selection.range->Release();
      return FAILED(result) ? result : S_FALSE;
    }
  }

  result = EndComposition(edit_cookie);
  engine_.Reset();
  if (SUCCEEDED(result)) {
    result = selection.range->SetText(
        edit_cookie,
        0,
        insert_text.data(),
        static_cast<LONG>(insert_text.size()));
  }
  if (SUCCEEDED(result)) {
    result = selection.range->Collapse(edit_cookie, TF_ANCHOR_END);
  }
  if (SUCCEEDED(result)) {
    selection.style.ase = TF_AE_NONE;
    selection.style.fInterimChar = FALSE;
    result = context->SetSelection(edit_cookie, 1, &selection);
  }
  selection.range->Release();

  if (SUCCEEDED(result)) {
    focus_generation_.fetch_add(1, std::memory_order_relaxed);
    UpdateIpcFocus(foreground_);
  }
  return result;
}

HRESULT TextService::GetFocusedContext(ITfContext** context) const noexcept {
  if (context == nullptr) {
    return E_POINTER;
  }
  *context = nullptr;
  if (thread_manager_ == nullptr) {
    return E_UNEXPECTED;
  }

  ITfDocumentMgr* document = nullptr;
  HRESULT result = thread_manager_->GetFocus(&document);
  if (FAILED(result) || document == nullptr) {
    return FAILED(result) ? result : S_FALSE;
  }
  result = document->GetTop(context);
  document->Release();
  return result;
}

HRESULT TextService::RequestRoute(ITfContext* context, KeyRoute route,
                                  bool* applied) noexcept {
  if (context == nullptr || applied == nullptr ||
      client_id_ == TF_CLIENTID_NULL) {
    return E_INVALIDARG;
  }
  *applied = false;

  auto* session = new (std::nothrow) KeyEditSession(this, context, route);
  if (session == nullptr) {
    return E_OUTOFMEMORY;
  }

  HRESULT session_result = E_FAIL;
  const HRESULT request_result = context->RequestEditSession(
      client_id_, session, TF_ES_SYNC | TF_ES_READWRITE, &session_result);
  session->Release();

  if (SUCCEEDED(request_result) && SUCCEEDED(session_result)) {
    *applied = true;
    return S_OK;
  }

  engine_.Reset();
  AbandonComposition();
  return FAILED(request_result) ? request_result : session_result;
}

HRESULT TextService::ApplyRoute(ITfContext* context,
                                TfEditCookie edit_cookie,
                                const KeyRoute& route) {
  if (context == nullptr) {
    return E_POINTER;
  }
  if (composition_context_ != nullptr && composition_context_ != context) {
    engine_.Reset();
    AbandonComposition();
  }

  if (route.kind == KeyRouteKind::Reset) {
    const HRESULT result = EndComposition(edit_cookie);
    engine_.Reset();
    return result;
  }

  if (route.kind == KeyRouteKind::Boundary) {
    HRESULT result = EndComposition(edit_cookie);
    engine_.Reset();
    if (FAILED(result)) {
      return result;
    }
    return InsertBoundary(context, edit_cookie, route.character);
  }

  if (route.kind != KeyRouteKind::Character &&
      route.kind != KeyRouteKind::Backspace) {
    return S_FALSE;
  }

  const std::u32string previous_visible{engine_.VisibleText()};
  const KeyEvent event = route.kind == KeyRouteKind::Backspace
                             ? KeyEvent{KeyKind::Backspace}
                             : KeyEvent{KeyKind::Character, route.character};
  const TextEdit edit = engine_.Process(event);
  if (!edit.consumed) {
    return S_FALSE;
  }

  if (edit.commit_before) {
    const HRESULT result = EndComposition(edit_cookie);
    if (FAILED(result)) {
      engine_.Reset();
      return result;
    }
  }

  HRESULT result = ApplyEngineEdit(context, edit_cookie, edit,
                                   edit.commit_before
                                       ? std::u32string_view{}
                                       : std::u32string_view{previous_visible});
  if (FAILED(result)) {
    engine_.Reset();
    return result;
  }

  if (engine_.VisibleText().empty()) {
    result = EndComposition(edit_cookie);
  }
  return result;
}

HRESULT TextService::EnsureComposition(ITfContext* context,
                                       TfEditCookie edit_cookie) {
  if (composition_ != nullptr && composition_context_ == context) {
    return S_OK;
  }
  if (composition_ != nullptr || composition_context_ != nullptr) {
    AbandonComposition();
  }

  TF_SELECTION selection{};
  ULONG fetched = 0;
  HRESULT result = context->GetSelection(
      edit_cookie, TF_DEFAULT_SELECTION, 1, &selection, &fetched);
  if (FAILED(result) || fetched != 1 || selection.range == nullptr) {
    return FAILED(result) ? result : E_FAIL;
  }

  BOOL empty = FALSE;
  result = selection.range->IsEmpty(edit_cookie, &empty);
  if (FAILED(result) || !empty) {
    selection.range->Release();
    return FAILED(result) ? result : S_FALSE;
  }

  ITfContextComposition* context_composition = nullptr;
  result = context->QueryInterface(
      IID_ITfContextComposition,
      reinterpret_cast<void**>(&context_composition));
  if (SUCCEEDED(result)) {
    result = context_composition->StartComposition(
        edit_cookie, selection.range, this, &composition_);
  }
  if (context_composition != nullptr) {
    context_composition->Release();
  }
  selection.range->Release();

  if (SUCCEEDED(result) && composition_ != nullptr) {
    composition_context_ = context;
    composition_context_->AddRef();
    return S_OK;
  }
  composition_ = nullptr;
  return FAILED(result) ? result : E_FAIL;
}

HRESULT TextService::ApplyEngineEdit(
    ITfContext* context, TfEditCookie edit_cookie, const TextEdit& edit,
    std::u32string_view previous_visible) {
  const auto translated = TranslateEdit(edit, previous_visible);
  if (!translated.has_value() ||
      translated->erase_utf16_units >
          static_cast<std::size_t>(std::numeric_limits<LONG>::max()) ||
      translated->insert.size() >
          static_cast<std::size_t>(std::numeric_limits<LONG>::max())) {
    return E_INVALIDARG;
  }

  HRESULT result = EnsureComposition(context, edit_cookie);
  if (FAILED(result)) {
    return result;
  }

  ITfRange* range = nullptr;
  result = composition_->GetRange(&range);
  if (FAILED(result) || range == nullptr) {
    return FAILED(result) ? result : E_FAIL;
  }

  result = range->Collapse(edit_cookie, TF_ANCHOR_END);
  if (SUCCEEDED(result) && translated->erase_utf16_units != 0) {
    const LONG requested =
        -static_cast<LONG>(translated->erase_utf16_units);
    LONG shifted = 0;
    result = range->ShiftStart(edit_cookie, requested, &shifted, nullptr);
    if (SUCCEEDED(result) && shifted != requested) {
      result = E_FAIL;
    }
  }
  if (SUCCEEDED(result)) {
    result = range->SetText(
        edit_cookie, 0,
        translated->insert.empty() ? nullptr : translated->insert.data(),
        static_cast<LONG>(translated->insert.size()));
  }
  range->Release();

  if (SUCCEEDED(result)) {
    result = SetCaretAtCompositionEnd(context, edit_cookie);
  }
  return result;
}

HRESULT TextService::InsertBoundary(ITfContext* context,
                                    TfEditCookie edit_cookie,
                                    char32_t character) {
  const TextEdit source{0, std::u32string{character}, true, false};
  const auto translated = TranslateEdit(source, {});
  if (!translated.has_value() ||
      translated->insert.size() >
          static_cast<std::size_t>(std::numeric_limits<LONG>::max())) {
    return E_INVALIDARG;
  }

  TF_SELECTION selection{};
  ULONG fetched = 0;
  HRESULT result = context->GetSelection(
      edit_cookie, TF_DEFAULT_SELECTION, 1, &selection, &fetched);
  if (FAILED(result) || fetched != 1 || selection.range == nullptr) {
    return FAILED(result) ? result : E_FAIL;
  }

  result = selection.range->SetText(
      edit_cookie, 0,
      translated->insert.empty() ? nullptr : translated->insert.data(),
      static_cast<LONG>(translated->insert.size()));
  if (SUCCEEDED(result)) {
    result = selection.range->Collapse(edit_cookie, TF_ANCHOR_END);
  }
  if (SUCCEEDED(result)) {
    selection.style.ase = TF_AE_NONE;
    selection.style.fInterimChar = FALSE;
    result = context->SetSelection(edit_cookie, 1, &selection);
  }
  selection.range->Release();
  return result;
}

HRESULT TextService::EndComposition(TfEditCookie edit_cookie) noexcept {
  if (composition_ == nullptr) {
    if (composition_context_ != nullptr) {
      composition_context_->Release();
      composition_context_ = nullptr;
    }
    return S_OK;
  }

  ITfComposition* ending = composition_;
  composition_ = nullptr;
  const HRESULT result = ending->EndComposition(edit_cookie);
  ending->Release();
  if (composition_context_ != nullptr) {
    composition_context_->Release();
    composition_context_ = nullptr;
  }
  return result;
}

HRESULT TextService::SetCaretAtCompositionEnd(ITfContext* context,
                                              TfEditCookie edit_cookie) {
  if (composition_ == nullptr) {
    return E_UNEXPECTED;
  }

  ITfRange* caret = nullptr;
  HRESULT result = composition_->GetRange(&caret);
  if (FAILED(result) || caret == nullptr) {
    return FAILED(result) ? result : E_FAIL;
  }
  result = caret->Collapse(edit_cookie, TF_ANCHOR_END);
  if (SUCCEEDED(result)) {
    TF_SELECTION selection{};
    selection.range = caret;
    selection.style.ase = TF_AE_NONE;
    selection.style.fInterimChar = FALSE;
    result = context->SetSelection(edit_cookie, 1, &selection);
  }
  caret->Release();
  return result;
}

void TextService::AbandonComposition() noexcept {
  if (composition_ != nullptr) {
    composition_->Release();
    composition_ = nullptr;
  }
  if (composition_context_ != nullptr) {
    composition_context_->Release();
    composition_context_ = nullptr;
  }
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
