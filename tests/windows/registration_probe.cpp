#include <windows.h>

#include <iomanip>
#include <iostream>
#include <string_view>

namespace {

using RegistrationFunction = HRESULT(STDAPICALLTYPE*)();

}  // namespace

int wmain(int argc, wchar_t** argv) {
  if (argc != 3) {
    std::wcerr << L"usage: keyina_tsf_registration_probe <dll> "
                  L"--register|--unregister\n";
    return 2;
  }

  const std::wstring_view action{argv[2]};
  const char* export_name = nullptr;
  if (action == L"--register") {
    export_name = "DllRegisterServer";
  } else if (action == L"--unregister") {
    export_name = "DllUnregisterServer";
  } else {
    std::wcerr << L"unknown action\n";
    return 2;
  }

  HMODULE module = LoadLibraryW(argv[1]);
  if (module == nullptr) {
    std::wcerr << L"LoadLibraryW failed: " << GetLastError() << L'\n';
    return 3;
  }

  const auto function = reinterpret_cast<RegistrationFunction>(
      GetProcAddress(module, export_name));
  if (function == nullptr) {
    std::wcerr << L"registration export missing\n";
    FreeLibrary(module);
    return 4;
  }

  const HRESULT result = function();
  std::wcout << L"HRESULT=0x" << std::hex << std::uppercase
             << static_cast<unsigned long>(result) << L'\n';
  FreeLibrary(module);
  return SUCCEEDED(result) ? 0 : 1;
}
