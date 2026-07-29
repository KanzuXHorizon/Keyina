#include <windows.h>

#include <msctf.h>
#include <objbase.h>

#include <array>
#include <atomic>
#include <cstddef>
#include <new>
#include <string>

#include <keyina/tsf/identifiers.h>

#include "module_state.h"
#include "text_service.h"

namespace keyina::tsf {
namespace {

std::atomic<long> g_live_objects{0};
std::atomic<long> g_server_locks{0};
HMODULE g_module = nullptr;

constexpr wchar_t kDescription[] = L"Keyina - Tiếng Việt";
constexpr LANGID kVietnameseLanguage =
    MAKELANGID(LANG_VIETNAMESE, SUBLANG_DEFAULT);

class ClassFactory final : public IClassFactory {
 public:
  ClassFactory() noexcept { ModuleObjectCreated(); }

  HRESULT STDMETHODCALLTYPE QueryInterface(REFIID interface_id,
                                           void** object) override {
    if (object == nullptr) {
      return E_POINTER;
    }
    *object = nullptr;
    if (!IsEqualIID(interface_id, IID_IUnknown) &&
        !IsEqualIID(interface_id, IID_IClassFactory)) {
      return E_NOINTERFACE;
    }
    *object = static_cast<IClassFactory*>(this);
    AddRef();
    return S_OK;
  }

  ULONG STDMETHODCALLTYPE AddRef() override { return ++reference_count_; }

  ULONG STDMETHODCALLTYPE Release() override {
    const ULONG remaining = --reference_count_;
    if (remaining == 0) {
      delete this;
    }
    return remaining;
  }

  HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown* outer,
                                           REFIID interface_id,
                                           void** object) override {
    if (object == nullptr) {
      return E_POINTER;
    }
    *object = nullptr;
    if (outer != nullptr) {
      return CLASS_E_NOAGGREGATION;
    }

    auto* service = new (std::nothrow) TextService();
    if (service == nullptr) {
      return E_OUTOFMEMORY;
    }
    const HRESULT result = service->QueryInterface(interface_id, object);
    service->Release();
    return result;
  }

  HRESULT STDMETHODCALLTYPE LockServer(BOOL lock) override {
    if (lock) {
      ModuleLock();
    } else {
      ModuleUnlock();
    }
    return S_OK;
  }

 private:
  ~ClassFactory() { ModuleObjectDestroyed(); }

  std::atomic<ULONG> reference_count_{1};
};

class ComScope {
 public:
  ComScope() noexcept : result_(CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED)) {
    initialized_here_ = SUCCEEDED(result_);
  }

  ~ComScope() {
    if (initialized_here_) {
      CoUninitialize();
    }
  }

  [[nodiscard]] bool usable() const noexcept {
    return SUCCEEDED(result_) || result_ == RPC_E_CHANGED_MODE;
  }

  [[nodiscard]] HRESULT result() const noexcept { return result_; }

 private:
  HRESULT result_;
  bool initialized_here_{false};
};

std::wstring GuidString(REFGUID guid) {
  std::array<wchar_t, 40> buffer{};
  const int length = StringFromGUID2(guid, buffer.data(),
                                     static_cast<int>(buffer.size()));
  return length > 1 ? std::wstring(buffer.data(), length - 1) : std::wstring{};
}

HRESULT ModulePath(std::wstring& path) {
  std::array<wchar_t, 32768> buffer{};
  const DWORD length = GetModuleFileNameW(g_module, buffer.data(),
                                          static_cast<DWORD>(buffer.size()));
  if (length == 0) {
    return HRESULT_FROM_WIN32(GetLastError());
  }
  if (length >= buffer.size()) {
    return HRESULT_FROM_WIN32(ERROR_INSUFFICIENT_BUFFER);
  }
  path.assign(buffer.data(), length);
  return S_OK;
}

HRESULT SetStringValue(HKEY key, const wchar_t* name,
                       const std::wstring& value) {
  const auto bytes = static_cast<DWORD>((value.size() + 1) * sizeof(wchar_t));
  const LONG result = RegSetValueExW(
      key, name, 0, REG_SZ, reinterpret_cast<const BYTE*>(value.c_str()), bytes);
  return result == ERROR_SUCCESS ? S_OK : HRESULT_FROM_WIN32(result);
}

std::wstring ComClassPath(bool include_inproc) {
  std::wstring path = L"Software\\Classes\\CLSID\\";
  path += GuidString(kTextServiceClsid);
  if (include_inproc) {
    path += L"\\InprocServer32";
  }
  return path;
}

HRESULT RegisterComServer() {
  std::wstring module_path;
  HRESULT result = ModulePath(module_path);
  if (FAILED(result)) {
    return result;
  }

  HKEY class_key = nullptr;
  const LONG class_status = RegCreateKeyExW(
      HKEY_CURRENT_USER, ComClassPath(false).c_str(), 0, nullptr,
      REG_OPTION_NON_VOLATILE, KEY_SET_VALUE | KEY_CREATE_SUB_KEY, nullptr,
      &class_key, nullptr);
  if (class_status != ERROR_SUCCESS) {
    return HRESULT_FROM_WIN32(class_status);
  }
  result = SetStringValue(class_key, nullptr, kDescription);
  RegCloseKey(class_key);
  if (FAILED(result)) {
    return result;
  }

  HKEY inproc_key = nullptr;
  const LONG inproc_status = RegCreateKeyExW(
      HKEY_CURRENT_USER, ComClassPath(true).c_str(), 0, nullptr,
      REG_OPTION_NON_VOLATILE, KEY_SET_VALUE, nullptr, &inproc_key, nullptr);
  if (inproc_status != ERROR_SUCCESS) {
    return HRESULT_FROM_WIN32(inproc_status);
  }
  result = SetStringValue(inproc_key, nullptr, module_path);
  if (SUCCEEDED(result)) {
    result = SetStringValue(inproc_key, L"ThreadingModel", L"Apartment");
  }
  RegCloseKey(inproc_key);
  return result;
}

HRESULT UnregisterComServer() {
  const LONG result = RegDeleteTreeW(HKEY_CURRENT_USER,
                                     ComClassPath(false).c_str());
  if (result == ERROR_SUCCESS || result == ERROR_FILE_NOT_FOUND ||
      result == ERROR_PATH_NOT_FOUND) {
    return S_OK;
  }
  return HRESULT_FROM_WIN32(result);
}

HRESULT RegisterTsf() {
  ComScope com;
  if (!com.usable()) {
    return com.result();
  }

  ITfInputProcessorProfileMgr* profiles = nullptr;
  HRESULT result = CoCreateInstance(
      CLSID_TF_InputProcessorProfiles, nullptr, CLSCTX_INPROC_SERVER,
      IID_ITfInputProcessorProfileMgr, reinterpret_cast<void**>(&profiles));
  if (FAILED(result)) {
    return result;
  }

  std::wstring module_path;
  result = ModulePath(module_path);
  if (SUCCEEDED(result)) {
    result = profiles->RegisterProfile(
        kTextServiceClsid, kVietnameseLanguage, kVietnameseProfileGuid,
        kDescription, static_cast<ULONG>(std::size(kDescription) - 1),
        module_path.c_str(), static_cast<ULONG>(module_path.size()), 0, nullptr,
        0, FALSE, 0);
  }
  profiles->Release();
  if (FAILED(result)) {
    return result;
  }

  ITfCategoryMgr* categories = nullptr;
  result = CoCreateInstance(CLSID_TF_CategoryMgr, nullptr,
                            CLSCTX_INPROC_SERVER, IID_ITfCategoryMgr,
                            reinterpret_cast<void**>(&categories));
  if (FAILED(result)) {
    return result;
  }
  result = categories->RegisterCategory(
      kTextServiceClsid, GUID_TFCAT_TIP_KEYBOARD, kTextServiceClsid);
  categories->Release();
  return result;
}

HRESULT UnregisterTsf() {
  ComScope com;
  if (!com.usable()) {
    return com.result();
  }

  HRESULT first_failure = S_OK;
  ITfCategoryMgr* categories = nullptr;
  HRESULT result = CoCreateInstance(CLSID_TF_CategoryMgr, nullptr,
                                    CLSCTX_INPROC_SERVER, IID_ITfCategoryMgr,
                                    reinterpret_cast<void**>(&categories));
  if (SUCCEEDED(result)) {
    result = categories->UnregisterCategory(
        kTextServiceClsid, GUID_TFCAT_TIP_KEYBOARD, kTextServiceClsid);
    if (FAILED(result)) {
      first_failure = result;
    }
    categories->Release();
  } else {
    first_failure = result;
  }

  ITfInputProcessorProfileMgr* profiles = nullptr;
  result = CoCreateInstance(
      CLSID_TF_InputProcessorProfiles, nullptr, CLSCTX_INPROC_SERVER,
      IID_ITfInputProcessorProfileMgr, reinterpret_cast<void**>(&profiles));
  if (SUCCEEDED(result)) {
    result = profiles->UnregisterProfile(
        kTextServiceClsid, kVietnameseLanguage, kVietnameseProfileGuid, 0);
    if (FAILED(result) && SUCCEEDED(first_failure)) {
      first_failure = result;
    }
    profiles->Release();
  } else if (SUCCEEDED(first_failure)) {
    first_failure = result;
  }
  return first_failure;
}

}  // namespace

void ModuleObjectCreated() noexcept { ++g_live_objects; }
void ModuleObjectDestroyed() noexcept { --g_live_objects; }
void ModuleLock() noexcept { ++g_server_locks; }
void ModuleUnlock() noexcept { --g_server_locks; }
bool ModuleCanUnload() noexcept {
  return g_live_objects.load() == 0 && g_server_locks.load() == 0;
}
HMODULE ModuleHandle() noexcept { return g_module; }
void SetModuleHandle(HMODULE module) noexcept { g_module = module; }

}  // namespace keyina::tsf

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID reserved) {
  static_cast<void>(reserved);
  if (reason == DLL_PROCESS_ATTACH) {
    keyina::tsf::SetModuleHandle(module);
    DisableThreadLibraryCalls(module);
  }
  return TRUE;
}

extern "C" STDAPI DllCanUnloadNow() {
  return keyina::tsf::ModuleCanUnload() ? S_OK : S_FALSE;
}

extern "C" STDAPI DllGetClassObject(REFCLSID class_id, REFIID interface_id,
                                    void** object) {
  if (object == nullptr) {
    return E_POINTER;
  }
  *object = nullptr;
  if (!IsEqualCLSID(class_id, keyina::tsf::kTextServiceClsid)) {
    return CLASS_E_CLASSNOTAVAILABLE;
  }

  auto* factory = new (std::nothrow) keyina::tsf::ClassFactory();
  if (factory == nullptr) {
    return E_OUTOFMEMORY;
  }
  const HRESULT result = factory->QueryInterface(interface_id, object);
  factory->Release();
  return result;
}

extern "C" STDAPI DllRegisterServer() {
  HRESULT result = keyina::tsf::RegisterComServer();
  if (FAILED(result)) {
    return result;
  }
  result = keyina::tsf::RegisterTsf();
  if (FAILED(result)) {
    static_cast<void>(keyina::tsf::UnregisterTsf());
    static_cast<void>(keyina::tsf::UnregisterComServer());
  }
  return result;
}

extern "C" STDAPI DllUnregisterServer() {
  const HRESULT tsf_result = keyina::tsf::UnregisterTsf();
  const HRESULT com_result = keyina::tsf::UnregisterComServer();
  return FAILED(tsf_result) ? tsf_result : com_result;
}
