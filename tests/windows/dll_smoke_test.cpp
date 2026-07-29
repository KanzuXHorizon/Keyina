#include <windows.h>

#include <msctf.h>
#include <objbase.h>

#include <iostream>

#include <keyina/tsf/identifiers.h>

namespace {

using DllCanUnloadNowFunction = HRESULT(STDAPICALLTYPE*)();
using DllGetClassObjectFunction = HRESULT(STDAPICALLTYPE*)(REFCLSID, REFIID,
                                                          void**);

int Fail(const char* message) {
  std::cerr << message << '\n';
  return 1;
}

}  // namespace

int wmain(int argc, wchar_t** argv) {
  if (argc != 2) {
    return Fail("expected the Keyina TSF DLL path");
  }

  HMODULE module = LoadLibraryW(argv[1]);
  if (module == nullptr) {
    return Fail("LoadLibraryW failed");
  }

  const auto can_unload = reinterpret_cast<DllCanUnloadNowFunction>(
      GetProcAddress(module, "DllCanUnloadNow"));
  const auto get_class_object = reinterpret_cast<DllGetClassObjectFunction>(
      GetProcAddress(module, "DllGetClassObject"));
  const auto register_server = GetProcAddress(module, "DllRegisterServer");
  const auto unregister_server = GetProcAddress(module, "DllUnregisterServer");

  if (can_unload == nullptr || get_class_object == nullptr ||
      register_server == nullptr || unregister_server == nullptr) {
    FreeLibrary(module);
    return Fail("required COM exports are missing");
  }
  if (can_unload() != S_OK) {
    FreeLibrary(module);
    return Fail("fresh DLL reported outstanding objects");
  }

  IClassFactory* factory = nullptr;
  const HRESULT factory_result = get_class_object(
      keyina::tsf::kTextServiceClsid, IID_IClassFactory,
      reinterpret_cast<void**>(&factory));
  if (FAILED(factory_result) || factory == nullptr) {
    FreeLibrary(module);
    return Fail("DllGetClassObject did not return IClassFactory");
  }
  if (can_unload() != S_FALSE) {
    factory->Release();
    FreeLibrary(module);
    return Fail("live class factory was not reflected by DllCanUnloadNow");
  }

  ITfTextInputProcessorEx* service = nullptr;
  const HRESULT create_result = factory->CreateInstance(
      nullptr, IID_ITfTextInputProcessorEx,
      reinterpret_cast<void**>(&service));
  if (FAILED(create_result) || service == nullptr) {
    factory->Release();
    FreeLibrary(module);
    return Fail("class factory could not create ITfTextInputProcessorEx");
  }

  service->Release();
  factory->Release();
  if (can_unload() != S_OK) {
    FreeLibrary(module);
    return Fail("released COM objects kept the DLL locked");
  }

  if (!FreeLibrary(module)) {
    return Fail("FreeLibrary failed");
  }
  return 0;
}
