#include <windows.h>

#include <msctf.h>
#include <objbase.h>

#include <iostream>
#include <string_view>

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
  TfClientId client_id = TF_CLIENTID_NULL;
  bool service_active = false;
  bool thread_manager_active = false;

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
    result = service->ActivateEx(
        thread_manager, client_id,
        keyina::tsf::kManualKeyDispatchForTests);
    if (FAILED(result)) {
      Fail(L"Keyina activation failed", result);
      break;
    }
    service_active = true;

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
