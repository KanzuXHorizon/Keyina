#include <windows.h>

#include <msctf.h>
#include <objbase.h>

#include <array>
#include <atomic>
#include <chrono>
#include <functional>
#include <iostream>
#include <mutex>
#include <string>
#include <string_view>
#include <thread>
#include <vector>

#include <keyina/ipc_protocol.h>
#include <keyina/tsf/identifiers.h>

#include "test_text_store.h"

namespace {

using DllCanUnloadNowFunction = HRESULT(STDAPICALLTYPE*)();
using DllGetClassObjectFunction = HRESULT(STDAPICALLTYPE*)(REFCLSID, REFIID,
                                                          void**);

int Fail(std::wstring_view message, HRESULT result = S_OK) {
  std::wcerr << message;
  if (FAILED(result)) {
    std::wcerr << L" HRESULT=0x" << std::hex << std::uppercase
               << static_cast<unsigned long>(result);
  }
  std::wcerr << L'\n';
  return 1;
}

bool SetTestKeyboardState(bool shift) {
  BYTE state[256]{};
  if (shift) {
    state[VK_SHIFT] = 0x80;
    state[VK_LSHIFT] = 0x80;
  }
  return SetKeyboardState(state) != FALSE;
}

bool SendKey(ITfKeyEventSink* sink, ITfContext* context, WPARAM virtual_key,
             bool shift = false) {
  if (!SetTestKeyboardState(shift)) {
    return false;
  }
  BOOL test_eaten = FALSE;
  if (FAILED(sink->OnTestKeyDown(context, virtual_key, 0, &test_eaten)) ||
      !test_eaten) {
    return false;
  }
  BOOL eaten = FALSE;
  return SUCCEEDED(sink->OnKeyDown(context, virtual_key, 0, &eaten)) && eaten;
}

bool SendRaw(ITfKeyEventSink* sink, ITfContext* context,
             std::string_view raw) {
  for (const char character : raw) {
    if (character >= 'a' && character <= 'z') {
      if (!SendKey(sink, context,
                   static_cast<WPARAM>('A' + (character - 'a')))) {
        return false;
      }
      continue;
    }
    if (character == '_') {
      if (!SendKey(sink, context, VK_OEM_MINUS, true)) {
        return false;
      }
      continue;
    }
    return false;
  }
  return true;
}

bool HasActiveComposition(ITfContext* context, bool expected) {
  ITfContextComposition* compositions = nullptr;
  HRESULT result = context->QueryInterface(
      IID_ITfContextComposition, reinterpret_cast<void**>(&compositions));
  if (FAILED(result)) {
    return false;
  }

  IEnumITfCompositionView* enumerator = nullptr;
  result = compositions->EnumCompositions(&enumerator);
  compositions->Release();
  if (FAILED(result) || enumerator == nullptr) {
    return false;
  }

  ITfCompositionView* view = nullptr;
  ULONG fetched = 0;
  result = enumerator->Next(1, &view, &fetched);
  enumerator->Release();
  if (view != nullptr) {
    view->Release();
  }
  if (FAILED(result) && result != S_FALSE) {
    return false;
  }
  return (fetched == 1) == expected;
}

bool ReadExact(HANDLE pipe, std::span<std::uint8_t> destination) {
  std::size_t offset = 0;
  while (offset < destination.size()) {
    DWORD read = 0;
    if (!ReadFile(pipe, destination.data() + offset,
                  static_cast<DWORD>(destination.size() - offset),
                  &read, nullptr) || read == 0) {
      return false;
    }
    offset += read;
  }
  return true;
}

bool WriteExact(HANDLE pipe, std::span<const std::uint8_t> source) {
  std::size_t offset = 0;
  while (offset < source.size()) {
    DWORD written = 0;
    if (!WriteFile(pipe, source.data() + offset,
                   static_cast<DWORD>(source.size() - offset),
                   &written, nullptr) || written == 0) {
      return false;
    }
    offset += written;
  }
  return true;
}

std::optional<keyina::ipc::Envelope> ReadEnvelope(HANDLE pipe) {
  std::array<std::uint8_t, keyina::ipc::kHeaderSize> header{};
  if (!ReadExact(pipe, header)) {
    return std::nullopt;
  }
  const std::uint32_t payload_length =
      static_cast<std::uint32_t>(header[10]) |
      (static_cast<std::uint32_t>(header[11]) << 8U) |
      (static_cast<std::uint32_t>(header[12]) << 16U) |
      (static_cast<std::uint32_t>(header[13]) << 24U);
  if (payload_length > keyina::ipc::kMaximumPayloadBytes) {
    return std::nullopt;
  }
  std::vector<std::uint8_t> frame(header.begin(), header.end());
  frame.resize(keyina::ipc::kHeaderSize + payload_length);
  if (payload_length != 0 &&
      !ReadExact(pipe, std::span<std::uint8_t>(frame).subspan(
                           keyina::ipc::kHeaderSize))) {
    return std::nullopt;
  }
  const auto decoded = keyina::ipc::Decode(frame);
  return decoded.status == keyina::ipc::DecodeStatus::Success
             ? decoded.envelope
             : std::nullopt;
}

std::wstring UniquePipeName() {
  GUID guid{};
  if (FAILED(CoCreateGuid(&guid))) {
    return {};
  }
  std::array<wchar_t, 40> text{};
  if (StringFromGUID2(guid, text.data(), static_cast<int>(text.size())) <= 1) {
    return {};
  }
  return L"Keyina.Tests.Tsf." + std::wstring(text.data());
}

bool PumpMessagesUntil(const std::function<bool()>& condition,
                       std::chrono::milliseconds timeout) {
  const auto deadline = std::chrono::steady_clock::now() + timeout;
  while (std::chrono::steady_clock::now() < deadline) {
    MSG message{};
    while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) {
      TranslateMessage(&message);
      DispatchMessageW(&message);
    }
    if (condition()) {
      return true;
    }
    Sleep(5);
  }
  return condition();
}

class PipeHostFixture final {
 public:
  PipeHostFixture() : pipe_name_(UniquePipeName()) {}

  ~PipeHostFixture() { Stop(); }

  [[nodiscard]] const std::wstring& pipe_name() const noexcept {
    return pipe_name_;
  }

  [[nodiscard]] bool Start() {
    if (pipe_name_.empty() || worker_.joinable()) {
      return false;
    }
    worker_ = std::thread([this] { Run(); });
    return true;
  }

  void RequestFinal(std::string text, std::uint64_t expected_generation) {
    {
      std::lock_guard lock(text_mutex_);
      final_text_ = std::move(text);
    }
    expected_generation_.store(expected_generation, std::memory_order_release);
    send_requested_.store(true, std::memory_order_release);
  }

  [[nodiscard]] bool sent() const noexcept {
    return sent_.load(std::memory_order_acquire);
  }

  void Stop() noexcept {
    if (!worker_.joinable()) {
      return;
    }
    stop_.store(true, std::memory_order_release);
    const std::wstring full_pipe = L"\\\\.\\pipe\\" + pipe_name_;
    HANDLE wake = CreateFileW(full_pipe.c_str(), GENERIC_READ | GENERIC_WRITE,
                              0, nullptr, OPEN_EXISTING, 0, nullptr);
    if (wake != INVALID_HANDLE_VALUE) {
      CloseHandle(wake);
    }
    worker_.join();
  }

 private:
  void Run() noexcept {
    const std::wstring full_pipe = L"\\\\.\\pipe\\" + pipe_name_;
    while (!stop_.load(std::memory_order_acquire) && !sent()) {
      HANDLE server = CreateNamedPipeW(
          full_pipe.c_str(), PIPE_ACCESS_DUPLEX,
          PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT,
          1, static_cast<DWORD>(keyina::ipc::kMaximumFrameBytes),
          static_cast<DWORD>(keyina::ipc::kMaximumFrameBytes), 0, nullptr);
      if (server == INVALID_HANDLE_VALUE) {
        return;
      }

      const BOOL connected = ConnectNamedPipe(server, nullptr);
      if (!connected && GetLastError() != ERROR_PIPE_CONNECTED) {
        CloseHandle(server);
        if (stop_.load(std::memory_order_acquire)) {
          return;
        }
        continue;
      }

      const auto hello = ReadEnvelope(server);
      if (!hello.has_value() ||
          hello->message_type != keyina::ipc::MessageType::Hello) {
        DisconnectNamedPipe(server);
        CloseHandle(server);
        continue;
      }

      if (send_requested_.load(std::memory_order_acquire) &&
          hello->focus_generation ==
              expected_generation_.load(std::memory_order_acquire)) {
        std::string text;
        {
          std::lock_guard lock(text_mutex_);
          text = final_text_;
        }
        keyina::ipc::Envelope final{
            .message_type = keyina::ipc::MessageType::FinalTranscript,
            .flags = 0,
            .session_id = hello->session_id,
            .focus_generation = hello->focus_generation,
            .payload = std::move(text),
        };
        const auto frame = keyina::ipc::Encode(final);
        if (!frame.empty() && WriteExact(server, frame)) {
          FlushFileBuffers(server);
          sent_.store(true, std::memory_order_release);
        }
        DisconnectNamedPipe(server);
        CloseHandle(server);
        return;
      }

      std::array<std::uint8_t, 1> wait_for_disconnect{};
      static_cast<void>(ReadExact(server, wait_for_disconnect));
      DisconnectNamedPipe(server);
      CloseHandle(server);
    }
  }

  std::wstring pipe_name_;
  std::atomic<bool> stop_{false};
  std::atomic<bool> send_requested_{false};
  std::atomic<bool> sent_{false};
  std::atomic<std::uint64_t> expected_generation_{0};
  std::mutex text_mutex_;
  std::string final_text_;
  std::thread worker_;
};

}  // namespace

int wmain(int argc, wchar_t** argv) {
  if (argc != 2) {
    return Fail(L"expected the Keyina TSF DLL path");
  }

  const HRESULT initialize_result =
      CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
  if (FAILED(initialize_result)) {
    return Fail(L"CoInitializeEx failed", initialize_result);
  }

  int exit_code = 1;
  HMODULE module = LoadLibraryW(argv[1]);
  ITfThreadMgr* thread_manager = nullptr;
  ITfDocumentMgr* document_manager = nullptr;
  ITfContext* context = nullptr;
  TestTextStore* store = nullptr;
  IClassFactory* factory = nullptr;
  ITfTextInputProcessorEx* service = nullptr;
  ITfKeyEventSink* key_sink = nullptr;
  keyina::tsf::IKeyinaTsfTestControl* test_control = nullptr;
  TfClientId client_id = TF_CLIENTID_NULL;
  bool service_active = false;
  bool thread_manager_active = false;
  PipeHostFixture pipe_host;

  do {
    if (module == nullptr) {
      Fail(L"LoadLibraryW failed");
      break;
    }
    const auto can_unload = reinterpret_cast<DllCanUnloadNowFunction>(
        GetProcAddress(module, "DllCanUnloadNow"));
    const auto get_class_object = reinterpret_cast<DllGetClassObjectFunction>(
        GetProcAddress(module, "DllGetClassObject"));
    if (can_unload == nullptr || get_class_object == nullptr) {
      Fail(L"required COM exports are missing");
      break;
    }

    HRESULT result = CoCreateInstance(
        CLSID_TF_ThreadMgr, nullptr, CLSCTX_INPROC_SERVER, IID_ITfThreadMgr,
        reinterpret_cast<void**>(&thread_manager));
    if (FAILED(result)) {
      Fail(L"could not create ITfThreadMgr", result);
      break;
    }
    result = thread_manager->Activate(&client_id);
    if (FAILED(result)) {
      Fail(L"ITfThreadMgr::Activate failed", result);
      break;
    }
    thread_manager_active = true;

    result = thread_manager->CreateDocumentMgr(&document_manager);
    if (FAILED(result)) {
      Fail(L"CreateDocumentMgr failed", result);
      break;
    }

    store = new TestTextStore();
    TfEditCookie text_store_cookie = 0;
    result = document_manager->CreateContext(
        client_id, 0, static_cast<ITextStoreACP*>(store), &context,
        &text_store_cookie);
    if (FAILED(result)) {
      Fail(L"CreateContext failed", result);
      break;
    }
    result = document_manager->Push(context);
    if (FAILED(result)) {
      Fail(L"ITfDocumentMgr::Push failed", result);
      break;
    }
    result = thread_manager->SetFocus(document_manager);
    if (FAILED(result)) {
      Fail(L"ITfThreadMgr::SetFocus failed", result);
      break;
    }

    result = get_class_object(
        keyina::tsf::kTextServiceClsid, IID_IClassFactory,
        reinterpret_cast<void**>(&factory));
    if (FAILED(result)) {
      Fail(L"DllGetClassObject failed", result);
      break;
    }
    result = factory->CreateInstance(
        nullptr, IID_ITfTextInputProcessorEx,
        reinterpret_cast<void**>(&service));
    if (FAILED(result)) {
      Fail(L"could not create Keyina text service", result);
      break;
    }
    result = service->QueryInterface(IID_ITfKeyEventSink,
                                     reinterpret_cast<void**>(&key_sink));
    if (FAILED(result)) {
      Fail(L"Keyina does not expose ITfKeyEventSink", result);
      break;
    }
    result = service->QueryInterface(
        __uuidof(keyina::tsf::IKeyinaTsfTestControl),
        reinterpret_cast<void**>(&test_control));
    if (FAILED(result)) {
      Fail(L"Keyina test DLL does not expose external text control", result);
      break;
    }
    if (!pipe_host.Start()) {
      Fail(L"could not start the local pipe host fixture");
      break;
    }
    BSTR pipe_name = SysAllocStringLen(
        pipe_host.pipe_name().data(),
        static_cast<UINT>(pipe_host.pipe_name().size()));
    result = test_control->SetPipeNameForTests(pipe_name);
    SysFreeString(pipe_name);
    if (FAILED(result)) {
      Fail(L"could not set the test pipe endpoint", result);
      break;
    }
    result = service->ActivateEx(
        thread_manager, client_id,
        keyina::tsf::kManualKeyDispatchForTests);
    if (FAILED(result)) {
      Fail(L"Keyina activation failed", result);
      break;
    }
    service_active = true;
    result = key_sink->OnSetFocus(TRUE);
    if (FAILED(result)) {
      Fail(L"Keyina focus activation failed", result);
      break;
    }

    ULONGLONG generation_before_test = 0;
    ULONGLONG generation_after_test = 0;
    result = test_control->GetFocusGeneration(&generation_before_test);
    BOOL unsupported_eaten = TRUE;
    if (FAILED(result) ||
        FAILED(key_sink->OnTestKeyDown(context, VK_F7, 0, &unsupported_eaten)) ||
        unsupported_eaten ||
        FAILED(test_control->GetFocusGeneration(&generation_after_test)) ||
        generation_before_test != generation_after_test) {
      Fail(L"OnTestKeyDown mutated focus generation");
      break;
    }

    if (!SendRaw(key_sink, context, "tieengs") ||
        store->Text() != L"tiếng" || !HasActiveComposition(context, true)) {
      Fail(L"typing tieengs did not produce an active tiếng composition");
      break;
    }
    if (!SendKey(key_sink, context, VK_SPACE) ||
        store->Text() != L"tiếng " || !HasActiveComposition(context, false)) {
      Fail(L"space did not commit the first composition");
      break;
    }
    if (!SendRaw(key_sink, context, "dduowngf") ||
        store->Text() != L"tiếng đường" ||
        !HasActiveComposition(context, true)) {
      Fail(L"typing dduowngf did not produce đường");
      break;
    }
    if (!SendKey(key_sink, context, VK_BACK) ||
        store->Text() != L"tiếng đương") {
      Fail(L"Backspace did not rebuild the previous composition");
      break;
    }
    if (!SendKey(key_sink, context, VK_SPACE) ||
        !SendRaw(key_sink, context, "as_") ||
        store->Text() != L"tiếng đương as_") {
      Fail(L"Context Guard did not restore raw technical-token keys");
      break;
    }
    if (!SendKey(key_sink, context, VK_SPACE) ||
        !HasActiveComposition(context, false)) {
      Fail(L"technical token boundary did not end composition");
      break;
    }

    const std::wstring before_selection{store->Text()};
    store->SelectForTest(0, static_cast<LONG>(store->Text().size()));
    BOOL selection_test_eaten = FALSE;
    BOOL selection_eaten = TRUE;
    result = key_sink->OnTestKeyDown(context, 'T', 0, &selection_test_eaten);
    if (FAILED(result) || !selection_test_eaten ||
        FAILED(key_sink->OnKeyDown(context, 'T', 0, &selection_eaten)) ||
        selection_eaten || store->Text() != before_selection) {
      Fail(L"typing over a non-empty selection did not fail open safely");
      break;
    }
    store->SelectForTest(
        static_cast<LONG>(store->Text().size()),
        static_cast<LONG>(store->Text().size()));

    ULONGLONG focus_generation = 0;
    result = test_control->GetFocusGeneration(&focus_generation);
    if (FAILED(result) || focus_generation == 0) {
      Fail(L"Keyina did not expose a valid focus generation", result);
      break;
    }

    BSTR empty_suffix = SysAllocStringLen(nullptr, 0);
    BSTR final_text = SysAllocString(L"xin chào");
    result = test_control->ApplyExternalText(
        focus_generation, empty_suffix, final_text);
    SysFreeString(empty_suffix);
    SysFreeString(final_text);
    if (result != S_OK || store->Text() != L"tiếng đương as_ xin chào") {
      Fail(L"final transcript was not inserted atomically", result);
      break;
    }

    BSTR stale_suffix = SysAllocStringLen(nullptr, 0);
    BSTR stale_text = SysAllocString(L" lỗi");
    result = test_control->ApplyExternalText(
        focus_generation, stale_suffix, stale_text);
    SysFreeString(stale_suffix);
    SysFreeString(stale_text);
    if (result != S_FALSE || store->Text() != L"tiếng đương as_ xin chào") {
      Fail(L"stale focus generation was not rejected", result);
      break;
    }

    if (!SendRaw(key_sink, context, "abc")) {
      Fail(L"could not prepare snippet trigger text");
      break;
    }
    ULONGLONG snippet_generation = 0;
    result = test_control->GetFocusGeneration(&snippet_generation);
    if (FAILED(result) || snippet_generation <= focus_generation) {
      Fail(L"typing did not advance focus generation", result);
      break;
    }

    BSTR expected_trigger = SysAllocString(L"abc");
    BSTR snippet_text = SysAllocString(L"XYZ");
    result = test_control->ApplyExternalText(
        snippet_generation, expected_trigger, snippet_text);
    SysFreeString(expected_trigger);
    SysFreeString(snippet_text);
    if (result != S_OK ||
        store->Text() != L"tiếng đương as_ xin chàoXYZ" ||
        HasActiveComposition(context, true)) {
      Fail(L"snippet trigger was not replaced atomically", result);
      break;
    }

    ULONGLONG pipe_generation = 0;
    result = test_control->GetFocusGeneration(&pipe_generation);
    if (FAILED(result)) {
      Fail(L"could not read generation before pipe delivery", result);
      break;
    }
    pipe_host.RequestFinal(" pipe-final", pipe_generation + 1);
    result = key_sink->OnSetFocus(TRUE);
    if (FAILED(result) || !PumpMessagesUntil(
            [&] {
              return pipe_host.sent() &&
                     store->Text() == L"tiếng đương as_ xin chàoXYZ pipe-final";
            },
            std::chrono::seconds(5))) {
      Fail(L"named-pipe final transcript did not reach the focused TSF context",
           result);
      break;
    }

    result = service->Deactivate();
    service_active = false;
    if (FAILED(result)) {
      Fail(L"normal deactivation failed", result);
      break;
    }
    result = service->ActivateEx(thread_manager, client_id,
                                 TF_TMAE_SECUREMODE);
    if (FAILED(result)) {
      Fail(L"secure-mode activation failed", result);
      break;
    }
    service_active = true;

    const std::wstring before_secure{store->Text()};
    ULONGLONG secure_generation = 0;
    result = test_control->GetFocusGeneration(&secure_generation);
    if (FAILED(result)) {
      Fail(L"could not read secure focus generation", result);
      break;
    }
    BSTR secure_suffix = SysAllocStringLen(nullptr, 0);
    BSTR secure_text = SysAllocString(L"blocked");
    result = test_control->ApplyExternalText(
        secure_generation, secure_suffix, secure_text);
    SysFreeString(secure_suffix);
    SysFreeString(secure_text);
    if (result != E_ACCESSDENIED || store->Text() != before_secure) {
      Fail(L"secure mode allowed external text insertion", result);
      break;
    }

    BOOL secure_test_eaten = TRUE;
    result = key_sink->OnTestKeyDown(context, 'A', 0, &secure_test_eaten);
    if (FAILED(result) || secure_test_eaten ||
        store->Text() != before_secure) {
      Fail(L"secure mode did not pass the key through safely", result);
      break;
    }

    result = service->Deactivate();
    service_active = false;
    if (FAILED(result)) {
      Fail(L"secure-mode deactivation failed", result);
      break;
    }

    test_control->Release();
    test_control = nullptr;
    key_sink->Release();
    key_sink = nullptr;
    service->Release();
    service = nullptr;
    factory->Release();
    factory = nullptr;
    if (can_unload() != S_OK) {
      Fail(L"Keyina DLL retained COM objects after service release");
      break;
    }

    exit_code = 0;
  } while (false);

  if (service_active && service != nullptr) {
    static_cast<void>(service->Deactivate());
  }
  if (test_control != nullptr) test_control->Release();
  if (key_sink != nullptr) key_sink->Release();
  if (service != nullptr) service->Release();
  if (factory != nullptr) factory->Release();
  if (document_manager != nullptr) {
    static_cast<void>(document_manager->Pop(TF_POPF_ALL));
  }
  if (context != nullptr) context->Release();
  if (document_manager != nullptr) document_manager->Release();
  if (store != nullptr) store->Release();
  if (thread_manager_active && thread_manager != nullptr) {
    static_cast<void>(thread_manager->Deactivate());
  }
  if (thread_manager != nullptr) thread_manager->Release();
  if (module != nullptr) FreeLibrary(module);
  CoUninitialize();
  return exit_code;
}
