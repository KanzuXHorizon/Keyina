#include <keyina/windows/win32_input_runtime.h>
#include <keyina/windows/input_injection.h>
#include <keyina/windows/pointer_input.h>
#include <keyina/windows/standard_edit_replacement.h>

#include <psapi.h>
#include <shellapi.h>
#include <tlhelp32.h>

#include <algorithm>
#include <array>
#include <atomic>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <cwchar>
#include <limits>
#include <new>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

namespace keyina::windows {
namespace {

constexpr wchar_t kWindowClassName[] = L"KeyinaNativeInputWindow";
constexpr wchar_t kSnippetOverlayTitle[] = L"Keyina snippets";
constexpr std::size_t kMaximumVisibleSnippetSuggestions = 8;
constexpr UINT kPointerRegistrationMessage = WM_APP + 1;
constexpr UINT kTrayCallbackMessage = WM_APP + 2;
constexpr UINT kRuntimeCommandMessage = WM_APP + 3;
constexpr UINT kSnippetOverlayUpdateMessage = WM_APP + 4;
constexpr UINT kTrayUpdateMessage = WM_APP + 5;
constexpr UINT kDeferredSnippetActionMessage = WM_APP + 6;
constexpr UINT kDeferredClipboardInjectionMessage = WM_APP + 7;
constexpr UINT_PTR kProfileReloadTimerIdentifier = 1;
constexpr UINT_PTR kClipboardRestoreTimerIdentifier = 2;
constexpr UINT kProfileReloadIntervalMilliseconds = 1000;
constexpr UINT kTrayIdentifier = 1;
constexpr wchar_t kCommandCompanionMutexName[] =
    L"Local\\Keyina.CommandCompanion";
constexpr UINT kToggleMenuCommand = 1001;
constexpr UINT kSettingsMenuCommand = 1002;
constexpr UINT kExitMenuCommand = 1003;
constexpr UINT kDictationMenuCommand = 1004;
constexpr UINT kTranslateMenuCommand = 1005;
constexpr std::uint8_t kControlModifier = 1u << 0u;
constexpr std::uint8_t kShiftModifier = 1u << 1u;
constexpr std::uint8_t kAltModifier = 1u << 2u;
constexpr std::uint8_t kWindowsModifier = 1u << 3u;
constexpr std::uint64_t kTenMiB = 10ULL * 1024ULL * 1024ULL;
constexpr DWORD kClipboardPasteSettleMilliseconds = 100;
constexpr std::uint64_t kNanosecondsPerSecond = 1000000000ULL;
constexpr std::size_t kFastInputEventCapacity = 16;
constexpr std::size_t kMaximumKeyboardInputEvents =
    ((kMaxActiveKeys + 1) * 2) + (kMaximumInputInsertUnits * 2);
constexpr std::size_t kMaximumSelectionInputEvents =
    2 + (kMaxActiveKeys * 2) + (kMaximumInputInsertUnits * 2);
constexpr std::size_t kMaximumClipboardInputEvents =
    ((kMaxActiveKeys + 1) * 2) + 6;
constexpr std::uint32_t kPendingToggleVietnameseAction = 1u << 0u;
constexpr std::uint32_t kPendingToggleDictationAction = 1u << 1u;
constexpr SIZE_T kExternalCommandWorkerStackReservation = 256 * 1024;

#if defined(_MSC_VER)
#define KEYINA_NOINLINE __declspec(noinline)
#elif defined(__GNUC__) || defined(__clang__)
#define KEYINA_NOINLINE __attribute__((noinline))
#else
#define KEYINA_NOINLINE
#endif

std::size_t RequiredKeyboardInputEvents(
    const InputDecision& decision) noexcept {
  return (static_cast<std::size_t>(decision.backspace_count) * 2) +
      (static_cast<std::size_t>(decision.insert_units) * 2);
}

std::size_t RequiredSelectionInputEvents(
    const InputDecision& decision) noexcept {
  return (decision.backspace_count == 0 ? 0 : 2) +
      (static_cast<std::size_t>(decision.backspace_count) * 2) +
      (static_cast<std::size_t>(decision.insert_units) * 2);
}

struct InputSubmissionResult {
  bool complete{false};
  UINT inserted{};
};

template <std::size_t Capacity>
InputSubmissionResult SubmitInputSequence(
    std::span<INPUT> sequence) noexcept {
  if (sequence.empty() || sequence.size() >
      static_cast<std::size_t>(std::numeric_limits<UINT>::max())) {
    return {};
  }
  const UINT expected = static_cast<UINT>(sequence.size());
  const UINT inserted = SendInput(expected, sequence.data(), sizeof(INPUT));
  if (inserted == expected) {
    return {true, inserted};
  }

  std::array<INPUT, Capacity> recovery{};
  const std::size_t recovery_count = BuildPartialInputRecoverySequence(
      sequence,
      inserted,
      recovery);
  if (recovery_count != 0) {
    static_cast<void>(SendInput(
        static_cast<UINT>(recovery_count),
        recovery.data(),
        sizeof(INPUT)));
  }
  return {false, inserted};
}

template <std::size_t Capacity>
bool BuildAndSendKeyboardInput(
    const InputDecision& decision,
    std::size_t required) noexcept {
  if (required == 0 || required > Capacity) {
    return required == 0 && decision.backspace_count == 0 &&
        decision.insert_units == 0;
  }
  std::array<INPUT, Capacity> inputs;
  const std::size_t count = BuildKeyboardInputSequence(
      decision,
      std::span<INPUT>(inputs.data(), required));
  if (count != required) {
    return false;
  }
  return SubmitInputSequence<Capacity>(
      std::span<INPUT>(inputs.data(), count)).complete;
}

template <std::size_t Capacity>
bool BuildAndSendSelectionInput(
    const InputDecision& decision,
    std::size_t required) noexcept {
  if (required == 0 || required > Capacity) {
    return required == 0 && decision.backspace_count == 0 &&
        decision.insert_units == 0;
  }
  std::array<INPUT, Capacity> inputs;
  const std::size_t count = BuildSelectionReplacementSequence(
      decision,
      std::span<INPUT>(inputs.data(), required));
  if (count != required) {
    return false;
  }
  return SubmitInputSequence<Capacity>(
      std::span<INPUT>(inputs.data(), count)).complete;
}

KEYINA_NOINLINE bool SendKeyboardInputFallback(
    const InputDecision& decision,
    std::size_t required) noexcept {
  return BuildAndSendKeyboardInput<kMaximumKeyboardInputEvents>(
      decision, required);
}

KEYINA_NOINLINE bool SendSelectionInputFallback(
    const InputDecision& decision,
    std::size_t required) noexcept {
  return BuildAndSendSelectionInput<kMaximumSelectionInputEvents>(
      decision, required);
}

bool SendKeyboardInputDecision(const InputDecision& decision) noexcept {
  const std::size_t required = RequiredKeyboardInputEvents(decision);
  if (required <= kFastInputEventCapacity) {
    return BuildAndSendKeyboardInput<kFastInputEventCapacity>(
        decision, required);
  }
  if (required > kMaximumKeyboardInputEvents) {
    return false;
  }
  return SendKeyboardInputFallback(decision, required);
}

bool SendSelectionInputDecision(const InputDecision& decision) noexcept {
  const std::size_t required = RequiredSelectionInputEvents(decision);
  if (required <= kFastInputEventCapacity) {
    return BuildAndSendSelectionInput<kFastInputEventCapacity>(
        decision, required);
  }
  if (required > kMaximumSelectionInputEvents) {
    return false;
  }
  return SendSelectionInputFallback(decision, required);
}

InputSubmissionResult SubmitLiteralUnicodeCharacter(
    char32_t character) noexcept {
  std::array<INPUT, 4> inputs;
  const std::size_t count = BuildLiteralUnicodeInputSequence(
      character, inputs);
  if (count == 0) {
    return {};
  }
  return SubmitInputSequence<4>(
      std::span<INPUT>(inputs.data(), count));
}

bool SendLiteralUnicodeCharacter(char32_t character) noexcept {
  return SubmitLiteralUnicodeCharacter(character).complete;
}

InputSubmissionResult SubmitVirtualKeyPair(
    std::uint16_t virtual_key) noexcept {
  if (virtual_key == 0) {
    return {};
  }
  std::array<INPUT, 2> inputs{};
  inputs[0].type = INPUT_KEYBOARD;
  inputs[0].ki.wVk = virtual_key;
  inputs[0].ki.dwExtraInfo = kKeyinaInjectionMarker;
  inputs[1] = inputs[0];
  inputs[1].ki.dwFlags = KEYEVENTF_KEYUP;
  return SubmitInputSequence<2>(inputs);
}

#undef KEYINA_NOINLINE

std::uint64_t CounterTicksToNanoseconds(
    std::uint64_t ticks,
    std::uint64_t frequency) noexcept {
  if (frequency == 0) {
    return 0;
  }
  const std::uint64_t seconds = ticks / frequency;
  const std::uint64_t remainder = ticks % frequency;
  constexpr std::uint64_t maximum =
      std::numeric_limits<std::uint64_t>::max();
  if (seconds > maximum / kNanosecondsPerSecond) {
    return maximum;
  }
  const std::uint64_t whole = seconds * kNanosecondsPerSecond;
  const auto fractional = static_cast<std::uint64_t>(
      (static_cast<long double>(remainder) *
       static_cast<long double>(kNanosecondsPerSecond)) /
      static_cast<long double>(frequency));
  return maximum - whole < fractional ? maximum : whole + fractional;
}

class NativeCallbackLatencyScope {
 public:
  NativeCallbackLatencyScope(
      NativeLatencyHistogram* histogram,
      std::uint64_t frequency) noexcept
      : histogram_(histogram), frequency_(frequency) {
    if (histogram_ == nullptr || frequency_ == 0 ||
        QueryPerformanceCounter(&started_) == FALSE) {
      histogram_ = nullptr;
    }
  }

  ~NativeCallbackLatencyScope() {
    if (histogram_ == nullptr) {
      return;
    }
    LARGE_INTEGER finished{};
    if (QueryPerformanceCounter(&finished) == FALSE ||
        finished.QuadPart < started_.QuadPart) {
      return;
    }
    histogram_->RecordNanoseconds(CounterTicksToNanoseconds(
        static_cast<std::uint64_t>(finished.QuadPart - started_.QuadPart),
        frequency_));
  }

  NativeCallbackLatencyScope(const NativeCallbackLatencyScope&) = delete;
  NativeCallbackLatencyScope& operator=(
      const NativeCallbackLatencyScope&) = delete;

 private:
  NativeLatencyHistogram* histogram_{};
  std::uint64_t frequency_{};
  LARGE_INTEGER started_{};
};

bool TryOpenClipboard(HWND owner) noexcept {
  return OpenClipboard(owner) != FALSE;
}

bool ClipboardContainsOnlyRestorableText() noexcept {
  UINT format = 0;
  while ((format = EnumClipboardFormats(format)) != 0) {
    if (format != CF_UNICODETEXT && format != CF_TEXT &&
        format != CF_OEMTEXT && format != CF_LOCALE) {
      return false;
    }
  }
  return true;
}

bool ReadClipboardUnicodeText(std::wstring& text, bool& present) {
  present = IsClipboardFormatAvailable(CF_UNICODETEXT) != FALSE;
  if (!present) {
    text.clear();
    return true;
  }
  const HANDLE handle = GetClipboardData(CF_UNICODETEXT);
  if (handle == nullptr) {
    return false;
  }
  const auto* value = static_cast<const wchar_t*>(GlobalLock(handle));
  if (value == nullptr) {
    return false;
  }
  try {
    text.assign(value);
  } catch (...) {
    GlobalUnlock(handle);
    return false;
  }
  GlobalUnlock(handle);
  return true;
}

bool IsKeyboardMessage(WPARAM message) noexcept {
  return message == WM_KEYDOWN || message == WM_KEYUP ||
         message == WM_SYSKEYDOWN || message == WM_SYSKEYUP;
}

bool IsKeyDownMessage(WPARAM message) noexcept {
  return message == WM_KEYDOWN || message == WM_SYSKEYDOWN;
}

bool IsModifierKey(std::uint16_t key) noexcept {
  return key == VK_SHIFT || key == VK_CONTROL || key == VK_MENU ||
         key == VK_LSHIFT || key == VK_RSHIFT ||
         key == VK_LCONTROL || key == VK_RCONTROL ||
         key == VK_LMENU || key == VK_RMENU ||
         key == VK_LWIN || key == VK_RWIN;
}

bool IsDeferredOrderingVirtualKey(std::uint16_t key) noexcept {
  switch (key) {
    case VK_BACK:
    case VK_TAB:
    case VK_RETURN:
    case VK_ESCAPE:
    case VK_PRIOR:
    case VK_NEXT:
    case VK_END:
    case VK_HOME:
    case VK_LEFT:
    case VK_UP:
    case VK_RIGHT:
    case VK_DOWN:
    case VK_INSERT:
    case VK_DELETE:
      return true;
    default:
      return false;
  }
}

void AppendUtf32ToWide(std::u32string_view value, std::wstring& output) {
  for (const char32_t codepoint : value) {
    if (codepoint <= 0xFFFF && !(codepoint >= 0xD800 && codepoint <= 0xDFFF)) {
      output.push_back(static_cast<wchar_t>(codepoint));
    } else if (codepoint <= 0x10FFFF) {
      const char32_t adjusted = codepoint - 0x10000;
      output.push_back(static_cast<wchar_t>(0xD800 + (adjusted >> 10)));
      output.push_back(static_cast<wchar_t>(0xDC00 + (adjusted & 0x3FF)));
    }
  }
}

std::wstring BuildSnippetOverlayText(
    std::u32string_view token,
    const std::vector<const RuntimeSnippetDefinition*>& suggestions) {
  std::wstring text = L"Gõ tắt ";
  AppendUtf32ToWide(token, text);
  text += L"\r\n";
  for (const auto* definition : suggestions) {
    if (definition == nullptr) {
      continue;
    }
    text += L"\r\n";
    AppendUtf32ToWide(definition->trigger, text);
    text += L"   ";
    switch (definition->command) {
      case RuntimeSnippetCommand::ToggleVietnamese:
        text += L"Bật/tắt tiếng Việt";
        break;
      case RuntimeSnippetCommand::ToggleDictation:
        text += L"Nhập bằng giọng nói";
        break;
      case RuntimeSnippetCommand::ExternalOutput:
        text += L"Đầu ra lệnh";
        break;
      case RuntimeSnippetCommand::None:
        if (!definition->expansion.empty()) {
          const std::size_t preview_length =
              std::min<std::size_t>(definition->expansion.size(), 48);
          text.append(
              reinterpret_cast<const wchar_t*>(definition->expansion.data()),
              preview_length);
          if (preview_length < definition->expansion.size()) {
            text += L"…";
          }
        }
        break;
    }
  }
  text += L"\r\n\r\nGõ tiếp để lọc · Space/Enter để chèn";
  return text;
}

char32_t TranslateCharacter(std::uint16_t virtual_key, bool shift,
                            bool caps_lock) noexcept {
  if (virtual_key >= 'A' && virtual_key <= 'Z') {
    const bool uppercase = shift != caps_lock;
    return uppercase
               ? static_cast<char32_t>(virtual_key)
               : static_cast<char32_t>(virtual_key + (L'a' - L'A'));
  }
  if (virtual_key >= '0' && virtual_key <= '9') {
    constexpr std::array<char32_t, 10> kShiftedDigits{
        U')', U'!', U'@', U'#', U'$', U'%', U'^', U'&', U'*', U'('};
    return shift ? kShiftedDigits[virtual_key - '0']
                 : static_cast<char32_t>(virtual_key);
  }
  switch (virtual_key) {
    case VK_SPACE:
      return U' ';
    case VK_OEM_1:
      return shift ? U':' : U';';
    case VK_OEM_PLUS:
      return shift ? U'+' : U'=';
    case VK_OEM_COMMA:
      return shift ? U'<' : U',';
    case VK_OEM_MINUS:
      return shift ? U'_' : U'-';
    case VK_OEM_PERIOD:
      return shift ? U'>' : U'.';
    case VK_OEM_2:
      return shift ? U'?' : U'/';
    case VK_OEM_3:
      return shift ? U'~' : U'`';
    case VK_OEM_4:
      return shift ? U'{' : U'[';
    case VK_OEM_5:
      return shift ? U'|' : U'\\';
    case VK_OEM_6:
      return shift ? U'}' : U']';
    case VK_OEM_7:
      return shift ? U'"' : U'\'';
    default:
      return U'\0';
  }
}

std::uint64_t FileTimeValue(const FILETIME& value) noexcept {
  return (static_cast<std::uint64_t>(value.dwHighDateTime) << 32u) |
         value.dwLowDateTime;
}

std::uint64_t QueryPrivateWorkingSetBytes() noexcept {
  constexpr SIZE_T kBufferSize = 1024 * 1024;
  void* storage = VirtualAlloc(
      nullptr, kBufferSize, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
  if (storage == nullptr) {
    return 0;
  }

  std::uint64_t private_bytes = 0;
  if (QueryWorkingSet(GetCurrentProcess(), storage, kBufferSize)) {
    const auto* information =
        static_cast<const PSAPI_WORKING_SET_INFORMATION*>(storage);
    SYSTEM_INFO system_info{};
    GetSystemInfo(&system_info);
    const std::uint64_t page_size = system_info.dwPageSize;
    for (ULONG_PTR index = 0;
         index < information->NumberOfEntries; ++index) {
      if (information->WorkingSetInfo[index].Shared == 0) {
        private_bytes += page_size;
      }
    }
  }
  VirtualFree(storage, 0, MEM_RELEASE);
  return private_bytes;
}

std::uint32_t CountCurrentProcessThreads() noexcept {
  const DWORD current_process = GetCurrentProcessId();
  HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, 0);
  if (snapshot == INVALID_HANDLE_VALUE) {
    return 0;
  }

  std::uint32_t count = 0;
  THREADENTRY32 entry{};
  entry.dwSize = sizeof(entry);
  if (Thread32First(snapshot, &entry)) {
    do {
      if (entry.th32OwnerProcessID == current_process) {
        ++count;
      }
      entry.dwSize = sizeof(entry);
    } while (Thread32Next(snapshot, &entry));
  }
  CloseHandle(snapshot);
  return count;
}

HICON LoadIconFromExecutableDirectory(const wchar_t* file_name) noexcept {
  std::array<wchar_t, 32768> module_path{};
  const DWORD length = GetModuleFileNameW(
      nullptr, module_path.data(), static_cast<DWORD>(module_path.size()));
  if (length == 0 || length >= module_path.size()) {
    return CopyIcon(LoadIconW(nullptr, IDI_APPLICATION));
  }

  wchar_t* separator = std::wcsrchr(module_path.data(), L'\\');
  if (separator == nullptr) {
    return CopyIcon(LoadIconW(nullptr, IDI_APPLICATION));
  }
  *separator = L'\0';

  std::array<wchar_t, 32768> candidate{};
  if (swprintf_s(candidate.data(), candidate.size(), L"%ls\\Assets\\%ls",
                 module_path.data(), file_name) > 0) {
    if (auto icon = static_cast<HICON>(LoadImageW(
            nullptr, candidate.data(), IMAGE_ICON, 16, 16,
            LR_LOADFROMFILE | LR_DEFAULTSIZE));
        icon != nullptr) {
      return icon;
    }
  }
  if (swprintf_s(candidate.data(), candidate.size(), L"%ls\\%ls",
                 module_path.data(), file_name) > 0) {
    if (auto icon = static_cast<HICON>(LoadImageW(
            nullptr, candidate.data(), IMAGE_ICON, 16, 16,
            LR_LOADFROMFILE | LR_DEFAULTSIZE));
        icon != nullptr) {
      return icon;
    }
  }
  return CopyIcon(LoadIconW(nullptr, IDI_APPLICATION));
}

bool IsFullscreenWindow(HWND window) noexcept {
  if (window == nullptr || IsIconic(window)) {
    return false;
  }
  RECT window_rect{};
  if (!GetWindowRect(window, &window_rect)) {
    return false;
  }
  const HMONITOR monitor = MonitorFromWindow(window, MONITOR_DEFAULTTONEAREST);
  MONITORINFO monitor_info{};
  monitor_info.cbSize = sizeof(monitor_info);
  if (monitor == nullptr || !GetMonitorInfoW(monitor, &monitor_info)) {
    return false;
  }
  constexpr LONG tolerance = 1;
  return window_rect.left <= monitor_info.rcMonitor.left + tolerance &&
         window_rect.top <= monitor_info.rcMonitor.top + tolerance &&
         window_rect.right >= monitor_info.rcMonitor.right - tolerance &&
         window_rect.bottom >= monitor_info.rcMonitor.bottom - tolerance;
}

bool ResolveRuntimeProfilePath(
    const wchar_t* file_name,
    std::array<wchar_t, 32768>& path) noexcept {
  std::array<wchar_t, 32768> local_app_data{};
  const DWORD length = GetEnvironmentVariableW(
      L"LOCALAPPDATA",
      local_app_data.data(),
      static_cast<DWORD>(local_app_data.size()));
  if (length == 0 || length >= local_app_data.size()) {
    return false;
  }
  return swprintf_s(
             path.data(),
             path.size(),
             L"%ls\\Keyina\\%ls",
             local_app_data.data(),
             file_name) > 0;
}

bool ResolveRuntimeInputProfilePath(
    std::array<wchar_t, 32768>& path) noexcept {
  return ResolveRuntimeProfilePath(L"runtime-input.bin", path);
}

bool ResolveRuntimeSnippetProfilePath(
    std::array<wchar_t, 32768>& path) noexcept {
  return ResolveRuntimeProfilePath(L"runtime-snippets.bin", path);
}

bool TryReadRuntimeInputProfile(
    const wchar_t* path,
    RuntimeInputProfile& profile) noexcept {
  HANDLE file = CreateFileW(
      path,
      GENERIC_READ,
      FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
      nullptr,
      OPEN_EXISTING,
      FILE_ATTRIBUTE_NORMAL,
      nullptr);
  if (file == INVALID_HANDLE_VALUE) {
    return false;
  }

  std::array<std::byte, kRuntimeInputProfileSize> bytes{};
  DWORD read = 0;
  const BOOL success = ReadFile(
      file,
      bytes.data(),
      static_cast<DWORD>(bytes.size()),
      &read,
      nullptr);
  CloseHandle(file);
  if (!success || read != bytes.size()) {
    return false;
  }
  const auto decoded = DecodeRuntimeInputProfile(bytes);
  if (!decoded) {
    return false;
  }
  profile = decoded.profile;
  return true;
}

bool TryReadRuntimeSnippetProfile(
    const wchar_t* path,
    RuntimeSnippetProfile& profile) noexcept {
  HANDLE file = CreateFileW(
      path,
      GENERIC_READ,
      FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
      nullptr,
      OPEN_EXISTING,
      FILE_ATTRIBUTE_NORMAL,
      nullptr);
  if (file == INVALID_HANDLE_VALUE) {
    return false;
  }

  LARGE_INTEGER size{};
  if (!GetFileSizeEx(file, &size) || size.QuadPart <= 0 ||
      size.QuadPart > static_cast<LONGLONG>(
                          kMaximumRuntimeSnippetProfileBytes)) {
    CloseHandle(file);
    return false;
  }

  std::vector<std::byte> bytes(static_cast<std::size_t>(size.QuadPart));
  DWORD read = 0;
  const BOOL success = ReadFile(
      file,
      bytes.data(),
      static_cast<DWORD>(bytes.size()),
      &read,
      nullptr);
  CloseHandle(file);
  if (!success || read != bytes.size()) {
    return false;
  }

  auto decoded = DecodeRuntimeSnippetProfile(bytes);
  if (!decoded) {
    return false;
  }
  profile = std::move(decoded.profile);
  return true;
}

RuntimeSnippetProfile LoadRuntimeSnippetProfileOrDefault() noexcept {
  RuntimeSnippetProfile profile = DefaultRuntimeSnippetProfile();
  std::array<wchar_t, 32768> path{};
  if (ResolveRuntimeSnippetProfilePath(path)) {
    static_cast<void>(TryReadRuntimeSnippetProfile(path.data(), profile));
  }
  return profile;
}

std::uint64_t HashApplicationIdForProcess(DWORD process_id) noexcept {
  HANDLE process = OpenProcess(
      PROCESS_QUERY_LIMITED_INFORMATION,
      FALSE,
      process_id);
  if (process == nullptr) {
    return 0;
  }

  std::array<wchar_t, 32768> path{};
  DWORD length = static_cast<DWORD>(path.size());
  const BOOL queried = QueryFullProcessImageNameW(
      process,
      0,
      path.data(),
      &length);
  CloseHandle(process);
  if (!queried || length == 0 || length >= path.size()) {
    return 0;
  }

  std::wstring_view full_path(path.data(), length);
  const std::size_t separator = full_path.find_last_of(L"\\/");
  const std::wstring_view file_name = separator == std::wstring_view::npos
                                          ? full_path
                                          : full_path.substr(separator + 1);
  if (file_name.empty()) {
    return 0;
  }

  std::array<wchar_t, 32768> upper{};
  if (file_name.size() >= upper.size()) {
    return 0;
  }
  std::copy(file_name.begin(), file_name.end(), upper.begin());
  if (CharUpperBuffW(upper.data(), static_cast<DWORD>(file_name.size())) == 0) {
    return 0;
  }

  std::array<char, 65536> utf8{};
  const int utf8_length = WideCharToMultiByte(
      CP_UTF8,
      WC_ERR_INVALID_CHARS,
      upper.data(),
      static_cast<int>(file_name.size()),
      utf8.data(),
      static_cast<int>(utf8.size()),
      nullptr,
      nullptr);
  if (utf8_length <= 0) {
    return 0;
  }

  std::uint64_t hash = 14695981039346656037ULL;
  for (int index = 0; index < utf8_length; ++index) {
    hash ^= static_cast<std::uint8_t>(utf8[index]);
    hash *= 1099511628211ULL;
  }
  return hash;
}

bool TryGetRuntimeInputProfileWriteTime(
    const wchar_t* path,
    FILETIME& write_time) noexcept {
  WIN32_FILE_ATTRIBUTE_DATA attributes{};
  if (!GetFileAttributesExW(
          path,
          GetFileExInfoStandard,
          &attributes)) {
    return false;
  }
  write_time = attributes.ftLastWriteTime;
  return true;
}

}  // namespace

Win32InputRuntime* Win32InputRuntime::active_runtime_ = nullptr;

Win32InputRuntime::Win32InputRuntime(
    RuntimeInputProfile profile,
    bool enable_tray,
    bool reload_profiles,
    bool profile_callback_latency,
    ULONG_PTR accepted_input_marker,
    bool force_selection_replacement_for_self_test) noexcept
    : profile_(profile),
      committed_vietnamese_enabled_(profile.vietnamese_enabled),
      controller_(profile, LoadRuntimeSnippetProfileOrDefault()),
      enable_tray_(enable_tray),
      reload_profiles_(reload_profiles),
      profile_callback_latency_(profile_callback_latency),
      accepted_input_marker_(accepted_input_marker),
      force_selection_replacement_for_self_test_(
          force_selection_replacement_for_self_test &&
          accepted_input_marker != 0) {}

Win32InputRuntime::~Win32InputRuntime() { Stop(); }

bool Win32InputRuntime::Start() noexcept {
  if (hook_ != nullptr || active_runtime_ != nullptr ||
      external_command_thread_ != nullptr) {
    startup_error_ = ERROR_ALREADY_EXISTS;
    return false;
  }

  if (deferred_clipboard_queue_ == nullptr) {
    deferred_clipboard_queue_.reset(
        new (std::nothrow) DeferredClipboardQueue());
    if (deferred_clipboard_queue_ == nullptr) {
      startup_error_ = ERROR_NOT_ENOUGH_MEMORY;
      return false;
    }
  }

  stopping_ = false;
  if (!ole_initialized_) {
    const HRESULT ole_result = OleInitialize(nullptr);
    ole_initialized_ = SUCCEEDED(ole_result);
  }
  pressed_keys_.Clear();
  owned_text_keys_.Clear();
  hotkey_router_.Reset();
  toggle_chord_active_ = false;
  toggle_chord_contaminated_ = false;
  snippet_overlay_update_posted_ = false;
  tray_update_posted_ = false;
  deferred_clipboard_message_posted_ = false;
  pending_snippet_actions_.store(0, std::memory_order_relaxed);
  deferred_clipboard_queue_->ResetAfterShutdown();
  external_command_queue_.ResetAfterShutdown();
  deferred_clipboard_queue_full_count_ = 0;
  deferred_clipboard_fallback_count_ = 0;
  preapplied_vietnamese_toggle_count_ = 0;
  committed_vietnamese_enabled_ = profile_.vietnamese_enabled;
  deferred_literal_injection_count_ = 0;
  deferred_virtual_key_injection_count_ = 0;
  clipboard_privacy_write_count_ = 0;
  clipboard_privacy_failure_count_ = 0;
  clipboard_privacy_formats_ = RegisterClipboardPrivacyFormats();
  startup_stage_ = NativeRuntimeStartupStage::None;
  startup_error_ = ERROR_SUCCESS;
  ClearCallbackLatency();
  performance_counter_frequency_ = 0;
  if (profile_callback_latency_) {
    LARGE_INTEGER frequency{};
    if (QueryPerformanceFrequency(&frequency) != FALSE &&
        frequency.QuadPart > 0) {
      performance_counter_frequency_ =
          static_cast<std::uint64_t>(frequency.QuadPart);
    } else {
      profile_callback_latency_ = false;
    }
  }
  WNDCLASSEXW window_class{};
  window_class.cbSize = sizeof(window_class);
  window_class.hInstance = GetModuleHandleW(nullptr);
  window_class.lpfnWndProc = &Win32InputRuntime::WindowProcedure;
  window_class.lpszClassName = kWindowClassName;
  if (RegisterClassExW(&window_class) == 0 &&
      GetLastError() != ERROR_CLASS_ALREADY_EXISTS) {
    startup_stage_ = NativeRuntimeStartupStage::RegisterWindowClass;
    startup_error_ = GetLastError();
    return false;
  }

  window_ = CreateWindowExW(
      0, kWindowClassName, L"Keyina native input", 0, 0, 0, 0, 0,
      HWND_MESSAGE, nullptr, window_class.hInstance, this);
  if (window_ == nullptr) {
    startup_stage_ = NativeRuntimeStartupStage::CreateMessageWindow;
    startup_error_ = GetLastError();
    return false;
  }

  active_runtime_ = this;
  caps_lock_ = (GetKeyState(VK_CAPITAL) & 0x0001) != 0;
  hook_ = SetWindowsHookExW(
      WH_KEYBOARD_LL, &Win32InputRuntime::KeyboardProcedure, nullptr, 0);
  if (hook_ == nullptr) {
    startup_stage_ = NativeRuntimeStartupStage::InstallKeyboardHook;
    startup_error_ = GetLastError();
    active_runtime_ = nullptr;
    DestroyWindow(window_);
    window_ = nullptr;
    return false;
  }

  if (enable_tray_) {
    shell_module_ = LoadLibraryW(L"shell32.dll");
    if (shell_module_ != nullptr) {
      shell_notify_icon_ = reinterpret_cast<decltype(shell_notify_icon_)>(
          GetProcAddress(shell_module_, "Shell_NotifyIconW"));
      shell_execute_ = reinterpret_cast<decltype(shell_execute_)>(
          GetProcAddress(shell_module_, "ShellExecuteW"));
    }
    if (shell_notify_icon_ == nullptr || shell_execute_ == nullptr) {
      if (shell_module_ != nullptr) {
        FreeLibrary(shell_module_);
        shell_module_ = nullptr;
      }
      shell_notify_icon_ = nullptr;
      shell_execute_ = nullptr;
      enable_tray_ = false;
    } else {
      active_icon_ = LoadIconFromExecutableDirectory(
          L"keyina-tray-active.ico");
      inactive_icon_ = LoadIconFromExecutableDirectory(
          L"keyina-tray-inactive.ico");
      UpdateTray();
    }
  }
  if (reload_profiles_) {
    ReloadProfileIfChanged();
    profile_timer_ = SetTimer(
        window_,
        kProfileReloadTimerIdentifier,
        kProfileReloadIntervalMilliseconds,
        nullptr);
  }
  static_cast<void>(StartExternalCommandWorker());
  return true;
}

int Win32InputRuntime::Run() noexcept {
  MSG message{};
  while (!stopping_) {
    const BOOL result = GetMessageW(&message, nullptr, 0, 0);
    if (result == 0) {
      return static_cast<int>(message.wParam);
    }
    if (result < 0) {
      return 1;
    }
    TranslateMessage(&message);
    DispatchMessageW(&message);
  }
  return 0;
}

void Win32InputRuntime::Stop() noexcept {
  if (stopping_ && hook_ == nullptr && window_ == nullptr) {
    return;
  }
  stopping_ = true;

  if (profile_timer_ != 0 && window_ != nullptr) {
    KillTimer(window_, profile_timer_);
    profile_timer_ = 0;
  }
  if (clipboard_restore_timer_ != 0 && window_ != nullptr) {
    KillTimer(window_, clipboard_restore_timer_);
    clipboard_restore_timer_ = 0;
  }
  if (hook_ != nullptr) {
    UnhookWindowsHookEx(hook_);
    hook_ = nullptr;
  }
  for (int attempt = 0;
       attempt < 50 && pending_clipboard_sequence_ != 0;
       ++attempt) {
    RestorePendingClipboard();
    if (pending_clipboard_sequence_ != 0) {
      Sleep(2);
    }
  }
  if (pending_clipboard_data_object_ != nullptr) {
    pending_clipboard_data_object_->Release();
    pending_clipboard_data_object_ = nullptr;
  }
  if (ole_initialized_) {
    OleUninitialize();
    ole_initialized_ = false;
  }
  pointer_registration_desired_ = false;
  ApplyPointerRegistration();
  StopExternalCommandWorker();
  if (tray_added_ && shell_notify_icon_ != nullptr) {
    shell_notify_icon_(NIM_DELETE, &tray_data_);
    tray_added_ = false;
  }
  if (active_icon_ != nullptr) {
    DestroyIcon(active_icon_);
    active_icon_ = nullptr;
  }
  if (inactive_icon_ != nullptr) {
    DestroyIcon(inactive_icon_);
    inactive_icon_ = nullptr;
  }
  shell_notify_icon_ = nullptr;
  shell_execute_ = nullptr;
  if (shell_module_ != nullptr) {
    FreeLibrary(shell_module_);
    shell_module_ = nullptr;
  }
  HideSnippetOverlay();
  if (snippet_overlay_window_ != nullptr) {
    DestroyWindow(snippet_overlay_window_);
    snippet_overlay_window_ = nullptr;
  }
  snippet_overlay_visible_ = false;
  if (window_ != nullptr) {
    DestroyWindow(window_);
    window_ = nullptr;
  }
  if (active_runtime_ == this) {
    active_runtime_ = nullptr;
  }
  pressed_keys_.Clear();
  owned_text_keys_.Clear();
  hotkey_router_.Reset();
  toggle_chord_active_ = false;
  toggle_chord_contaminated_ = false;
  snippet_overlay_update_posted_ = false;
  tray_update_posted_ = false;
  deferred_clipboard_message_posted_ = false;
  pending_snippet_actions_.store(0, std::memory_order_relaxed);
  if (preapplied_vietnamese_toggle_count_ != 0) {
    profile_.vietnamese_enabled = committed_vietnamese_enabled_;
    controller_.ApplyProfile(profile_);
    preapplied_vietnamese_toggle_count_ = 0;
  }
  if (deferred_clipboard_queue_ != nullptr) {
    deferred_clipboard_queue_->ResetAfterShutdown();
  }
  external_command_queue_.ResetAfterShutdown();
  controller_.Reset();
}

void Win32InputRuntime::RequestOpenSettings() noexcept {
  OpenManagedSettings();
}

void Win32InputRuntime::PumpMessagesFor(DWORD duration_milliseconds) noexcept {
  const ULONGLONG deadline = GetTickCount64() + duration_milliseconds;
  while (!stopping_ && GetTickCount64() < deadline) {
    const ULONGLONG remaining = deadline - GetTickCount64();
    const DWORD timeout = static_cast<DWORD>(
        std::min<ULONGLONG>(remaining, 50));
    MsgWaitForMultipleObjectsEx(
        0, nullptr, timeout, QS_ALLINPUT, MWMO_INPUTAVAILABLE);

    MSG message{};
    while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) {
      if (message.message == WM_QUIT) {
        stopping_ = true;
        return;
      }
      TranslateMessage(&message);
      DispatchMessageW(&message);
    }
  }
}

bool Win32InputRuntime::KeyStateSet::Get(std::uint16_t key) const noexcept {
  if (key >= 256) {
    return false;
  }
  const std::size_t segment = key >> 6;
  const std::uint64_t mask = std::uint64_t{1} << (key & 63u);
  return (segments_[segment] & mask) != 0;
}

void Win32InputRuntime::KeyStateSet::Set(std::uint16_t key,
                                        bool value) noexcept {
  if (key >= 256) {
    return;
  }
  const std::size_t segment = key >> 6;
  const std::uint64_t mask = std::uint64_t{1} << (key & 63u);
  if (value) {
    segments_[segment] |= mask;
  } else {
    segments_[segment] &= ~mask;
  }
}

void Win32InputRuntime::KeyStateSet::Clear() noexcept { segments_.fill(0); }

LRESULT CALLBACK Win32InputRuntime::WindowProcedure(
    HWND window, UINT message, WPARAM w_param, LPARAM l_param) noexcept {
  if (message == WM_NCCREATE) {
    const auto* create = reinterpret_cast<const CREATESTRUCTW*>(l_param);
    SetWindowLongPtrW(
        window, GWLP_USERDATA,
        reinterpret_cast<LONG_PTR>(create->lpCreateParams));
  }
  auto* runtime = reinterpret_cast<Win32InputRuntime*>(
      GetWindowLongPtrW(window, GWLP_USERDATA));
  if (runtime != nullptr) {
    return runtime->HandleWindowMessage(window, message, w_param, l_param);
  }
  return DefWindowProcW(window, message, w_param, l_param);
}

LRESULT CALLBACK Win32InputRuntime::KeyboardProcedure(
    int code, WPARAM message, LPARAM data) noexcept {
  auto* runtime = active_runtime_;
  if (runtime == nullptr) {
    return CallNextHookEx(nullptr, code, message, data);
  }
  return runtime->HandleKeyboardEvent(code, message, data);
}

DWORD WINAPI Win32InputRuntime::ExternalCommandWorkerProcedure(
    void* context) noexcept {
  auto* runtime = static_cast<Win32InputRuntime*>(context);
  return runtime == nullptr ? 1 : runtime->RunExternalCommandWorker();
}

LRESULT Win32InputRuntime::HandleWindowMessage(
    HWND window, UINT message, WPARAM w_param, LPARAM l_param) noexcept {
  switch (message) {
    case kPointerRegistrationMessage:
      ApplyPointerRegistration();
      return 0;
    case kSnippetOverlayUpdateMessage:
      snippet_overlay_update_posted_ = false;
      UpdateSnippetOverlay();
      return 0;
    case kTrayUpdateMessage:
      tray_update_posted_ = false;
      UpdateTray();
      return 0;
    case kDeferredSnippetActionMessage:
      HandleDeferredSnippetActions();
      return 0;
    case kDeferredClipboardInjectionMessage:
      HandleDeferredClipboardInjections();
      return 0;
    case WM_INPUT: {
      const bool reset = IsPointerResetPacket(
          reinterpret_cast<HRAWINPUT>(l_param));
      const LRESULT result = DefWindowProcW(
          window, message, w_param, l_param);
      if (reset) {
        ++pointer_reset_count_;
        controller_.OnPointerReset();
        pointer_registration_desired_ = false;
        ApplyPointerRegistration();
      }
      return result;
    }
    case WM_TIMER:
      if (w_param == kProfileReloadTimerIdentifier) {
        ReloadProfileIfChanged();
        return 0;
      }
      if (w_param == kClipboardRestoreTimerIdentifier) {
        if (clipboard_restore_timer_ != 0) {
          KillTimer(window_, clipboard_restore_timer_);
          clipboard_restore_timer_ = 0;
        }
        RestorePendingClipboard();
        RequestDeferredClipboardDrain();
        return 0;
      }
      break;
    case kRuntimeCommandMessage:
      static_cast<void>(
          LaunchManagedCommand(static_cast<RuntimeCommand>(w_param)));
      return 0;
    case kTrayCallbackMessage:
      if (l_param == WM_RBUTTONUP || l_param == WM_CONTEXTMENU) {
        ShowTrayMenu();
      } else if (l_param == WM_LBUTTONDBLCLK) {
        OpenManagedSettings();
      }
      return 0;
    case WM_COMMAND:
      switch (LOWORD(w_param)) {
        case kToggleMenuCommand:
          ExecuteSnippetAction(RuntimeSnippetCommand::ToggleVietnamese);
          return 0;
        case kSettingsMenuCommand:
          OpenManagedSettings();
          return 0;
        case kDictationMenuCommand:
          static_cast<void>(QueueManagedCommand(RuntimeCommand::ToggleDictation));
          return 0;
        case kTranslateMenuCommand:
          static_cast<void>(QueueManagedCommand(RuntimeCommand::TranslateSelection));
          return 0;
        case kExitMenuCommand:
          RequestExit();
          return 0;
        default:
          break;
      }
      break;
    case WM_CLOSE:
      RequestExit();
      return 0;
    default:
      break;
  }
  return DefWindowProcW(window, message, w_param, l_param);
}

LRESULT Win32InputRuntime::HandleKeyboardEvent(
    int code, WPARAM message, LPARAM data) noexcept {
  if (code < 0 || data == 0 || !IsKeyboardMessage(message)) {
    return CallNextHookEx(nullptr, code, message, data);
  }

  try {
    const auto& native_event =
        *reinterpret_cast<const KBDLLHOOKSTRUCT*>(data);
    if (native_event.dwExtraInfo == kKeyinaInjectionMarker) {
      return CallNextHookEx(nullptr, code, message, data);
    }
    if (accepted_input_marker_ != 0 &&
        native_event.dwExtraInfo != accepted_input_marker_) {
      return CallNextHookEx(nullptr, code, message, data);
    }

    NativeCallbackLatencyScope callback_latency(
        profile_callback_latency_ ? &callback_latency_histogram_ : nullptr,
        performance_counter_frequency_);
    auto stage_histogram = [this](
        NativeCallbackLatencyStage stage) noexcept
        -> NativeLatencyHistogram* {
      if (!profile_callback_latency_) {
        return nullptr;
      }
      const auto index = static_cast<std::size_t>(stage);
      return index < callback_stage_latency_histograms_.size()
                 ? &callback_stage_latency_histograms_[index]
                 : nullptr;
    };

    ++processed_keyboard_events_;
    const bool key_down = IsKeyDownMessage(message);
    const auto virtual_key = static_cast<std::uint16_t>(native_event.vkCode);
    const bool was_pressed = pressed_keys_.Get(virtual_key);
    PhysicalKeyEvent event{};
    RuntimeHotkeyDecision hotkey_decision{};
    {
      NativeCallbackLatencyScope key_state_latency(
          stage_histogram(NativeCallbackLatencyStage::KeyStateAndHotkey),
          performance_counter_frequency_);
      if (key_down && !was_pressed && virtual_key == VK_CAPITAL) {
        caps_lock_ = !caps_lock_;
      }
      pressed_keys_.Set(virtual_key, key_down);
      if (IsModifierKey(virtual_key)) {
        RefreshModifierState();
      }

      event.virtual_key = virtual_key;
      event.character = key_down
                            ? TranslateCharacter(
                                  virtual_key,
                                  (modifier_state_ & kShiftModifier) != 0,
                                  caps_lock_)
                            : U'\0';
      event.key_down = key_down;
      event.shift = (modifier_state_ & kShiftModifier) != 0;
      event.control = (modifier_state_ & kControlModifier) != 0;
      event.alt = (modifier_state_ & kAltModifier) != 0;
      event.windows = (modifier_state_ & kWindowsModifier) != 0;
      event.key_repeat = key_down && was_pressed;

      ProcessToggleGesture(event);
      const bool companion_state_required = key_down &&
          (event.virtual_key == profile_.hotkeys[4].virtual_key ||
           event.virtual_key == profile_.hotkeys[5].virtual_key);
      hotkey_decision = hotkey_router_.Process(
          event,
          profile_,
          was_pressed,
          companion_state_required && IsCommandCompanionActive());
    }
    if (hotkey_decision.command != RuntimeCommand::None &&
        !QueueManagedCommand(hotkey_decision.command) &&
        hotkey_decision.suppress && key_down) {
      hotkey_router_.CancelSuppression(event.virtual_key);
      return CallNextHookEx(nullptr, code, message, data);
    }
    if (hotkey_decision.suppress) {
      return 1;
    }

    if (!key_down) {
      bool suppress_release = false;
      {
        NativeCallbackLatencyScope key_up_latency(
            stage_histogram(NativeCallbackLatencyStage::KeyUpRelease),
            performance_counter_frequency_);
        const bool owned_release = owned_text_keys_.Get(virtual_key);
        if (owned_release) {
          owned_text_keys_.Set(virtual_key, false);
        }
        const bool controller_release =
            controller_.Process(event, {}).suppress;
        suppress_release = owned_release || controller_release;
      }
      return suppress_release
                 ? 1
                 : CallNextHookEx(nullptr, code, message, data);
    }

    TypingContext context{};
    {
      NativeCallbackLatencyScope context_latency(
          stage_histogram(NativeCallbackLatencyStage::TypingContext),
          performance_counter_frequency_);
      context = CaptureTypingContext();
    }
    if (key_down) {
      if (last_key_down_context_known_ && last_key_down_context_ != context) {
        ++context_change_count_;
      }
      last_key_down_context_ = context;
      last_key_down_context_known_ = true;
      if (context.bypass_typing) {
        ++bypass_context_count_;
      }
    }

    InputDecision decision{};
    {
      NativeCallbackLatencyScope controller_latency(
          stage_histogram(NativeCallbackLatencyStage::ControllerProcess),
          performance_counter_frequency_);
      decision = controller_.Process(event, context);
      if (key_down) {
        RequestSnippetOverlayUpdate();
      }
      RequestPointerRegistration(
          controller_.pointer_observation_required() &&
          !context.bypass_typing);
    }
    const bool clipboard_delivery =
        profile_.clipboard_compatibility_enabled;
    const bool selection_replacement_target =
        !context.bypass_typing && profile_.vietnamese_enabled &&
        RequiresSelectionReplacementTarget(context.focus_window);
    const bool own_text_stream = ShouldOwnTextStream(
        profile_.vietnamese_enabled,
        context.bypass_typing,
        clipboard_delivery,
        selection_replacement_target);
    const bool deferred_barrier_active = clipboard_delivery &&
        (deferred_clipboard_message_posted_ ||
         pending_clipboard_sequence_ != 0 ||
         (deferred_clipboard_queue_ != nullptr &&
          !deferred_clipboard_queue_->empty()));
    const bool decision_inserts_text =
        decision.insert_units != 0 || !decision.extended_insert.empty();

    const bool defer_command =
        decision.snippet_command != RuntimeSnippetCommand::None;
    const bool defer_literal = event.character != U'\0' &&
        !event.control && !event.alt && !event.windows;
    const bool defer_virtual_key = event.character == U'\0' &&
        !event.shift && !event.control && !event.alt && !event.windows &&
        IsDeferredOrderingVirtualKey(virtual_key);
    if (deferred_barrier_active && !decision_inserts_text &&
        (defer_command || defer_literal || defer_virtual_key)) {
      bool queued = false;
      {
        NativeCallbackLatencyScope injection_latency(
            stage_histogram(NativeCallbackLatencyStage::Injection),
            performance_counter_frequency_);
        ++suppressed_edit_count_;
        if (defer_command) {
          queued = QueueDeferredCommandInjection(
              decision,
              event.character,
              virtual_key,
              context);
        } else if (defer_literal) {
          queued = QueueDeferredLiteralInjection(
              event.character,
              virtual_key,
              context);
        } else {
          queued = QueueDeferredVirtualKeyInjection(virtual_key, context);
        }
      }
      if (queued) {
        owned_text_keys_.Set(virtual_key, true);
        if (decision.suppress) {
          controller_.Reset();
          RequestPointerRegistration(false);
          RequestSnippetOverlayUpdate();
        }
        return 1;
      }
      ++failed_injection_count_;
      controller_.Reset();
      RequestPointerRegistration(false);
      deferred_clipboard_queue_->CancelProducer();
      return CallNextHookEx(nullptr, code, message, data);
    }

    if (!decision.suppress && !own_text_stream) {
      return CallNextHookEx(nullptr, code, message, data);
    }

    if (!decision.suppress) {
      if (event.character == U'\0' || event.control || event.alt ||
          event.windows) {
        return CallNextHookEx(nullptr, code, message, data);
      }
      bool injected = false;
      {
        NativeCallbackLatencyScope injection_latency(
            stage_histogram(NativeCallbackLatencyStage::Injection),
            performance_counter_frequency_);
        ++suppressed_edit_count_;
        injected = SendLiteralUnicodeCharacter(event.character);
      }
      if (!injected) {
        ++failed_injection_count_;
        controller_.Reset();
        RequestPointerRegistration(false);
        return CallNextHookEx(nullptr, code, message, data);
      }
      ++successful_injection_count_;
      owned_text_keys_.Set(virtual_key, true);
      return 1;
    }

    const TextDeliveryMode delivery_mode = ChooseTextDeliveryMode(
        clipboard_delivery,
        selection_replacement_target);
    if (delivery_mode == TextDeliveryMode::Clipboard &&
        decision_inserts_text) {
      bool queued = false;
      {
        NativeCallbackLatencyScope injection_latency(
            stage_histogram(NativeCallbackLatencyStage::Injection),
            performance_counter_frequency_);
        ++suppressed_edit_count_;
        queued = QueueDeferredClipboardInjection(
            decision,
            event.character,
            virtual_key,
            context);
      }
      if (!queued) {
        ++failed_injection_count_;
        controller_.Reset();
        RequestPointerRegistration(false);
        deferred_clipboard_queue_->CancelProducer();
        return CallNextHookEx(nullptr, code, message, data);
      }
      return 1;
    }

    if (!PrepareDeferredSnippetCommand(decision)) {
      ++failed_injection_count_;
      controller_.Reset();
      RequestPointerRegistration(false);
      return CallNextHookEx(nullptr, code, message, data);
    }

    bool injected = false;
    {
      NativeCallbackLatencyScope injection_latency(
          stage_histogram(NativeCallbackLatencyStage::Injection),
          performance_counter_frequency_);
      ++suppressed_edit_count_;
      switch (delivery_mode) {
        case TextDeliveryMode::Keyboard:
          injected = Inject(decision);
          break;
        case TextDeliveryMode::SelectionReplacement:
          injected = InjectWithSelectionReplacement(decision);
          break;
        case TextDeliveryMode::Clipboard:
          // Command-only decisions erase their trigger without touching the
          // clipboard. Text-bearing decisions were queued above.
          injected = Inject(decision);
          break;
      }
    }
    if (!injected) {
      ++failed_injection_count_;
      controller_.Reset();
      RequestPointerRegistration(false);
      external_command_queue_.CancelProducer();
      return CallNextHookEx(nullptr, code, message, data);
    }
    ++successful_injection_count_;
    CommitDeferredSnippetCommand(decision.snippet_command);
    return 1;
  } catch (...) {
    controller_.Reset();
    RequestPointerRegistration(false);
    return CallNextHookEx(nullptr, code, message, data);
  }
}

TypingContext Win32InputRuntime::CaptureTypingContext() noexcept {
  ++typing_context_capture_count_;
  GUITHREADINFO info{};
  info.cbSize = sizeof(info);
  if (!GetGUIThreadInfo(0, &info) || info.hwndFocus == nullptr) {
    cached_focus_window_ = nullptr;
    cached_selection_replacement_target_window_ = nullptr;
    cached_selection_replacement_target_ = false;
    return {};
  }

  if (info.hwndFocus != cached_focus_window_) {
    cached_focus_window_ = info.hwndFocus;
    cached_selection_replacement_target_window_ = nullptr;
    cached_selection_replacement_target_ = false;
  }

  const HWND active = info.hwndActive != nullptr
                          ? info.hwndActive
                          : info.hwndFocus;
  if (active != cached_active_window_ || cached_process_id_ == 0) {
    DWORD process_id = 0;
    if (GetWindowThreadProcessId(active, &process_id) == 0) {
      cached_active_window_ = nullptr;
      cached_focus_window_ = nullptr;
      cached_selection_replacement_target_window_ = nullptr;
      cached_process_id_ = 0;
      cached_application_hash_ = 0;
      cached_selection_replacement_target_ = false;
      return {};
    }
    cached_active_window_ = active;
    cached_process_id_ = process_id;
    cached_application_hash_ = HashApplicationIdForProcess(process_id);
  }

  SetLastError(ERROR_SUCCESS);
  const LONG_PTR style = GetWindowLongPtrW(info.hwndFocus, GWL_STYLE);
  const bool style_failed = style == 0 && GetLastError() != ERROR_SUCCESS;
  const bool password =
      style_failed || (style & ES_PASSWORD) != 0;
  return TypingContext{
      cached_process_id_,
      reinterpret_cast<std::uintptr_t>(info.hwndFocus),
      password,
      cached_application_hash_,
  };
}

bool Win32InputRuntime::RequiresSelectionReplacementTarget(
    std::uintptr_t focus_window) noexcept {
  const HWND window = reinterpret_cast<HWND>(focus_window);
  if (window == nullptr) {
    return false;
  }
  if (force_selection_replacement_for_self_test_) {
    return true;
  }
  if (window == cached_selection_replacement_target_window_) {
    return cached_selection_replacement_target_;
  }

  std::array<wchar_t, 128> focus_class{};
  const int class_length = GetClassNameW(
      window,
      focus_class.data(),
      static_cast<int>(focus_class.size()));
  cached_selection_replacement_target_window_ = window;
  cached_selection_replacement_target_ = class_length > 0 &&
      RequiresSelectionReplacementForWindowClass(std::wstring_view(
          focus_class.data(), static_cast<std::size_t>(class_length)));
  return cached_selection_replacement_target_;
}

bool Win32InputRuntime::Inject(
    const InputDecision& decision) noexcept {
  if (decision.extended_insert.empty()) {
    return SendKeyboardInputDecision(decision);
  }

  InputDecision erase{};
  erase.suppress = true;
  erase.backspace_count = decision.backspace_count;
  if (!SendKeyboardInputDecision(erase)) {
    return false;
  }

  std::size_t offset = 0;
  while (offset < decision.extended_insert.size()) {
    InputDecision part{};
    part.suppress = true;
    const std::size_t remaining = decision.extended_insert.size() - offset;
    part.insert_units = static_cast<std::uint16_t>(
        std::min<std::size_t>(remaining, part.insert.size()));
    for (std::size_t index = 0; index < part.insert_units; ++index) {
      part.insert[index] = static_cast<wchar_t>(
          decision.extended_insert[offset + index]);
    }
    if (!SendKeyboardInputDecision(part)) {
      return false;
    }
    offset += part.insert_units;
  }
  return true;
}

bool Win32InputRuntime::InjectWithSelectionReplacement(
    const InputDecision& decision) noexcept {
  if (decision.extended_insert.empty()) {
    return SendSelectionInputDecision(decision);
  }

  std::size_t offset = 0;
  bool first = true;
  while (offset < decision.extended_insert.size()) {
    InputDecision part{};
    part.suppress = true;
    part.backspace_count = first ? decision.backspace_count : 0;
    const std::size_t remaining = decision.extended_insert.size() - offset;
    part.insert_units = static_cast<std::uint16_t>(
        std::min<std::size_t>(remaining, part.insert.size()));
    for (std::size_t index = 0; index < part.insert_units; ++index) {
      part.insert[index] = static_cast<wchar_t>(
          decision.extended_insert[offset + index]);
    }
    if (!SendSelectionInputDecision(part)) {
      return false;
    }
    offset += part.insert_units;
    first = false;
  }
  return true;
}

Win32InputRuntime::TargetInjectionResult
Win32InputRuntime::InjectViaClipboard(
    const InputDecision& decision,
    std::uint32_t target_process_id,
    std::uintptr_t target_focus_window) noexcept {
  if (pending_clipboard_sequence_ != 0) {
    return TargetInjectionResult::FailedBeforeMutation;
  }

  std::wstring replacement;
  try {
    if (!decision.extended_insert.empty()) {
      replacement.assign(
          reinterpret_cast<const wchar_t*>(decision.extended_insert.data()),
          decision.extended_insert.size());
    } else {
      replacement.assign(decision.insert.data(), decision.insert_units);
    }
  } catch (...) {
    return TargetInjectionResult::FailedBeforeMutation;
  }
  if (replacement.empty()) {
    return TargetInjectionResult::FailedBeforeMutation;
  }

  const HWND target = reinterpret_cast<HWND>(target_focus_window);
  const StandardEditReplacementResult edit_result =
      TryReplaceFocusedStandardEdit(
          target,
          target_process_id,
          decision.backspace_count,
          replacement);
  if (edit_result == StandardEditReplacementResult::Replaced) {
    ++standard_edit_replace_count_;
    return TargetInjectionResult::Succeeded;
  }
  if (edit_result != StandardEditReplacementResult::UnsupportedControl) {
    return StandardEditTargetMayBeMutated(edit_result)
        ? TargetInjectionResult::FailedAfterPossibleMutation
        : TargetInjectionResult::FailedBeforeMutation;
  }
  if (!IsDeferredTargetCurrent(target_process_id, target_focus_window)) {
    return TargetInjectionResult::FailedBeforeMutation;
  }

  IDataObject* previous_data_object = nullptr;
  const bool captured_all_formats =
      ole_initialized_ && SUCCEEDED(OleGetClipboard(&previous_data_object)) &&
      previous_data_object != nullptr;
  IDataObject* private_previous_data_object = nullptr;
  if (captured_all_formats) {
    private_previous_data_object = CreatePrivateClipboardDataObject(
        previous_data_object,
        clipboard_privacy_formats_);
    previous_data_object->Release();
    previous_data_object = nullptr;
    if (private_previous_data_object == nullptr) {
      ++clipboard_privacy_failure_count_;
      return TargetInjectionResult::FailedBeforeMutation;
    }
  }
  if (!TryOpenClipboard(window_)) {
    if (private_previous_data_object != nullptr) {
      private_previous_data_object->Release();
    }
    return TargetInjectionResult::FailedBeforeMutation;
  }

  std::wstring previous_text;
  bool previous_text_present = false;
  const bool clipboard_safe =
      ReadClipboardUnicodeText(previous_text, previous_text_present) &&
      (private_previous_data_object != nullptr ||
       ClipboardContainsOnlyRestorableText());
  if (!clipboard_safe) {
    if (private_previous_data_object != nullptr) {
      private_previous_data_object->Release();
    }
    CloseClipboard();
    return TargetInjectionResult::FailedBeforeMutation;
  }

  auto restore_captured_clipboard = [&]() noexcept {
    if (private_previous_data_object != nullptr) {
      CloseClipboard();
      const HRESULT set_result =
          OleSetClipboard(private_previous_data_object);
      if (SUCCEEDED(set_result)) {
        static_cast<void>(OleFlushClipboard());
      }
      private_previous_data_object->Release();
      private_previous_data_object = nullptr;
      return;
    }
    if (previous_text_present) {
      static_cast<void>(SetPrivateClipboardUnicodeText(
          previous_text,
          clipboard_privacy_formats_));
    } else {
      static_cast<void>(EmptyClipboard());
    }
    CloseClipboard();
  };

  if (!SetPrivateClipboardUnicodeText(
          replacement,
          clipboard_privacy_formats_)) {
    ++clipboard_privacy_failure_count_;
    restore_captured_clipboard();
    return TargetInjectionResult::FailedBeforeMutation;
  }
  ++clipboard_privacy_write_count_;
  const DWORD owned_sequence = GetClipboardSequenceNumber();
  CloseClipboard();
  if (owned_sequence == 0) {
    ++clipboard_privacy_failure_count_;
    if (private_previous_data_object != nullptr) {
      const HRESULT set_result =
          OleSetClipboard(private_previous_data_object);
      if (SUCCEEDED(set_result)) {
        static_cast<void>(OleFlushClipboard());
      }
      private_previous_data_object->Release();
    } else if (TryOpenClipboard(window_)) {
      if (previous_text_present) {
        static_cast<void>(SetPrivateClipboardUnicodeText(
            previous_text,
            clipboard_privacy_formats_));
      } else {
        static_cast<void>(EmptyClipboard());
      }
      CloseClipboard();
    }
    return TargetInjectionResult::FailedBeforeMutation;
  }

  pending_clipboard_text_ = std::move(previous_text);
  pending_clipboard_data_object_ = private_previous_data_object;
  pending_clipboard_text_present_ = previous_text_present;
  pending_clipboard_sequence_ = owned_sequence;
  clipboard_restore_timer_ = SetTimer(
      window_,
      kClipboardRestoreTimerIdentifier,
      kClipboardPasteSettleMilliseconds,
      nullptr);
  if (clipboard_restore_timer_ == 0) {
    RestorePendingClipboard();
    return TargetInjectionResult::FailedBeforeMutation;
  }
  if (!IsDeferredTargetCurrent(target_process_id, target_focus_window)) {
    KillTimer(window_, clipboard_restore_timer_);
    clipboard_restore_timer_ = 0;
    RestorePendingClipboard();
    return TargetInjectionResult::FailedBeforeMutation;
  }

  std::array<INPUT, kMaximumClipboardInputEvents> inputs{};
  const std::size_t count = BuildClipboardPasteSequence(decision, inputs);
  if (count == 0) {
    KillTimer(window_, clipboard_restore_timer_);
    clipboard_restore_timer_ = 0;
    RestorePendingClipboard();
    return TargetInjectionResult::FailedBeforeMutation;
  }
  const InputSubmissionResult submission =
      SubmitInputSequence<kMaximumClipboardInputEvents>(
          std::span<INPUT>(inputs.data(), count));
  if (submission.complete) {
    return TargetInjectionResult::Succeeded;
  }
  if (submission.inserted == 0) {
    KillTimer(window_, clipboard_restore_timer_);
    clipboard_restore_timer_ = 0;
    RestorePendingClipboard();
    return TargetInjectionResult::FailedBeforeMutation;
  }
  // Some selection/paste events reached the target. Keep the private clipboard
  // alive until the settle timer fires, because restoring it immediately could
  // make an accepted Ctrl+V paste the previous clipboard contents.
  return TargetInjectionResult::FailedAfterPossibleMutation;
}

void Win32InputRuntime::RestorePendingClipboard() noexcept {
  if (pending_clipboard_sequence_ == 0) {
    return;
  }
  const DWORD current_sequence = GetClipboardSequenceNumber();
  if (!ShouldRestoreClipboard(
          pending_clipboard_sequence_, current_sequence)) {
    pending_clipboard_text_.clear();
    if (pending_clipboard_data_object_ != nullptr) {
      pending_clipboard_data_object_->Release();
      pending_clipboard_data_object_ = nullptr;
    }
    pending_clipboard_text_present_ = false;
    pending_clipboard_sequence_ = 0;
    return;
  }
  if (!TryOpenClipboard(window_)) {
    if (!stopping_ && window_ != nullptr && clipboard_restore_timer_ == 0) {
      clipboard_restore_timer_ = SetTimer(
          window_,
          kClipboardRestoreTimerIdentifier,
          kClipboardPasteSettleMilliseconds,
          nullptr);
    }
    return;
  }
  const bool still_owned = ShouldRestoreClipboard(
      pending_clipboard_sequence_, GetClipboardSequenceNumber());
  bool restored = true;
  if (still_owned) {
    if (pending_clipboard_data_object_ != nullptr) {
      CloseClipboard();
      const HRESULT set_result =
          OleSetClipboard(pending_clipboard_data_object_);
      restored = SUCCEEDED(set_result);
      if (restored && FAILED(OleFlushClipboard())) {
        ++clipboard_privacy_failure_count_;
      }
    } else if (pending_clipboard_text_present_) {
      restored = SetPrivateClipboardUnicodeText(
          pending_clipboard_text_,
          clipboard_privacy_formats_);
      CloseClipboard();
    } else {
      restored = EmptyClipboard() != FALSE;
      CloseClipboard();
    }
  } else {
    CloseClipboard();
  }
  if (!restored) {
    ++clipboard_privacy_failure_count_;
    if (!stopping_ && window_ != nullptr && clipboard_restore_timer_ == 0) {
      clipboard_restore_timer_ = SetTimer(
          window_,
          kClipboardRestoreTimerIdentifier,
          kClipboardPasteSettleMilliseconds,
          nullptr);
    }
    return;
  }
  pending_clipboard_text_.clear();
  if (pending_clipboard_data_object_ != nullptr) {
    pending_clipboard_data_object_->Release();
    pending_clipboard_data_object_ = nullptr;
  }
  pending_clipboard_text_present_ = false;
  pending_clipboard_sequence_ = 0;
}

bool Win32InputRuntime::IsPointerResetPacket(HRAWINPUT input) noexcept {
  std::array<std::byte, 64> storage{};
  UINT size = static_cast<UINT>(storage.size());
  if (GetRawInputData(
          input, RID_INPUT, storage.data(), &size,
          sizeof(RAWINPUTHEADER)) == static_cast<UINT>(-1) ||
      size < sizeof(RAWINPUTHEADER) || size > storage.size()) {
    return false;
  }
  const auto* raw = reinterpret_cast<const RAWINPUT*>(storage.data());
  if (raw->header.dwType != RIM_TYPEMOUSE) {
    return false;
  }
  return IsPointerResetButtonFlags(raw->data.mouse.usButtonFlags);
}

void Win32InputRuntime::RequestPointerRegistration(bool active) noexcept {
  if (pointer_registration_desired_ == active) {
    return;
  }
  pointer_registration_desired_ = active;
  if (window_ != nullptr) {
    PostMessageW(window_, kPointerRegistrationMessage, 0, 0);
  }
}

void Win32InputRuntime::ApplyPointerRegistration() noexcept {
  if (window_ == nullptr ||
      pointer_registered_ == pointer_registration_desired_) {
    return;
  }
  RAWINPUTDEVICE device{};
  device.usUsagePage = 0x01;
  device.usUsage = 0x02;
  device.dwFlags = pointer_registration_desired_
                       ? RIDEV_INPUTSINK
                       : RIDEV_REMOVE;
  device.hwndTarget = pointer_registration_desired_ ? window_ : nullptr;
  if (RegisterRawInputDevices(&device, 1, sizeof(device))) {
    pointer_registered_ = pointer_registration_desired_;
  } else if (pointer_registration_desired_) {
    pointer_registration_desired_ = false;
  }
}

void Win32InputRuntime::ProcessToggleGesture(
    const PhysicalKeyEvent& event) noexcept {
  const std::uint8_t required = profile_.hotkeys[0].modifiers;
  const bool active = required != 0 &&
      (modifier_state_ & required) == required;

  if (event.key_down && !IsModifierKey(event.virtual_key) &&
      toggle_chord_active_) {
    toggle_chord_contaminated_ = true;
  }
  if (toggle_chord_active_ && (modifier_state_ & ~required) != 0) {
    toggle_chord_contaminated_ = true;
  }
  if (!toggle_chord_active_ && active) {
    toggle_chord_active_ = true;
    toggle_chord_contaminated_ =
        (modifier_state_ & ~required) != 0;
    return;
  }
  if (toggle_chord_active_ && !active) {
    if (!toggle_chord_contaminated_) {
      ExecuteSnippetAction(RuntimeSnippetCommand::ToggleVietnamese);
    }
    toggle_chord_active_ = false;
    toggle_chord_contaminated_ = false;
  }
}

void Win32InputRuntime::RequestSnippetOverlayUpdate() noexcept {
  if (snippet_overlay_update_posted_ || window_ == nullptr) {
    return;
  }
  if (PostMessageW(window_, kSnippetOverlayUpdateMessage, 0, 0) != FALSE) {
    snippet_overlay_update_posted_ = true;
  }
}

void Win32InputRuntime::RequestTrayUpdate() noexcept {
  if (tray_update_posted_ || window_ == nullptr) {
    return;
  }
  if (PostMessageW(window_, kTrayUpdateMessage, 0, 0) != FALSE) {
    tray_update_posted_ = true;
  }
}

bool Win32InputRuntime::QueueDeferredClipboardInjection(
    const InputDecision& decision,
    char32_t fallback_character,
    std::uint16_t source_virtual_key,
    const TypingContext& context) noexcept {
  if (deferred_clipboard_queue_ == nullptr) {
    return false;
  }
  deferred_clipboard_queue_->CancelProducer();
  if (window_ == nullptr || context.bypass_typing ||
      context.foreground_process_id == 0 || context.focus_window == 0 ||
      decision.snippet_command == RuntimeSnippetCommand::ExternalOutput) {
    return false;
  }

  const std::size_t text_units = decision.extended_insert.empty()
      ? static_cast<std::size_t>(decision.insert_units)
      : decision.extended_insert.size();
  if (text_units == 0 ||
      text_units > kMaximumRuntimeSnippetExpansionUtf8Bytes) {
    return false;
  }

  auto* item = deferred_clipboard_queue_->TryReserveProducer();
  if (item == nullptr) {
    ++deferred_clipboard_queue_full_count_;
    return false;
  }
  if (decision.extended_insert.empty()) {
    for (std::size_t index = 0; index < text_units; ++index) {
      item->text_storage[index] =
          static_cast<char16_t>(decision.insert[index]);
    }
  } else {
    std::copy(
        decision.extended_insert.begin(),
        decision.extended_insert.end(),
        item->text_storage.begin());
  }
  item->kind = DeferredClipboardWorkKind::Transform;
  item->text_units = text_units;
  item->backspace_count = decision.backspace_count;
  item->source_virtual_key = source_virtual_key;
  item->fallback_character = fallback_character;
  item->target_process_id = context.foreground_process_id;
  item->target_focus_window = context.focus_window;
  item->snippet_command = decision.snippet_command;
  item->snippet_state_preapplied = false;

  const bool needs_message = !deferred_clipboard_message_posted_ &&
      pending_clipboard_sequence_ == 0;
  if (needs_message &&
      PostMessageW(
          window_, kDeferredClipboardInjectionMessage, 0, 0) == FALSE) {
    deferred_clipboard_queue_->CancelProducer();
    return false;
  }
  if (!deferred_clipboard_queue_->CommitProducer()) {
    return false;
  }
  if (needs_message) {
    deferred_clipboard_message_posted_ = true;
  }
  return true;
}

bool Win32InputRuntime::QueueDeferredLiteralInjection(
    char32_t character,
    std::uint16_t source_virtual_key,
    const TypingContext& context) noexcept {
  if (deferred_clipboard_queue_ == nullptr) {
    return false;
  }
  deferred_clipboard_queue_->CancelProducer();
  if (window_ == nullptr || context.bypass_typing || character == U'\0' ||
      character > 0x10FFFF ||
      (character >= 0xD800 && character <= 0xDFFF) ||
      context.foreground_process_id == 0 || context.focus_window == 0) {
    return false;
  }

  auto* item = deferred_clipboard_queue_->TryReserveProducer();
  if (item == nullptr) {
    ++deferred_clipboard_queue_full_count_;
    return false;
  }
  item->kind = DeferredClipboardWorkKind::Literal;
  item->snippet_state_preapplied = false;
  if (character <= 0xFFFF) {
    item->text_storage[0] = static_cast<char16_t>(character);
    item->text_units = 1;
  } else {
    const char32_t adjusted = character - 0x10000;
    item->text_storage[0] = static_cast<char16_t>(
        0xD800 + (adjusted >> 10));
    item->text_storage[1] = static_cast<char16_t>(
        0xDC00 + (adjusted & 0x3FF));
    item->text_units = 2;
  }
  item->backspace_count = 0;
  item->source_virtual_key = source_virtual_key;
  item->fallback_character = character;
  item->target_process_id = context.foreground_process_id;
  item->target_focus_window = context.focus_window;
  item->snippet_command = RuntimeSnippetCommand::None;

  const bool needs_message = !deferred_clipboard_message_posted_ &&
      pending_clipboard_sequence_ == 0;
  if (needs_message &&
      PostMessageW(
          window_, kDeferredClipboardInjectionMessage, 0, 0) == FALSE) {
    deferred_clipboard_queue_->CancelProducer();
    return false;
  }
  if (!deferred_clipboard_queue_->CommitProducer()) {
    return false;
  }
  if (needs_message) {
    deferred_clipboard_message_posted_ = true;
  }
  return true;
}

bool Win32InputRuntime::QueueDeferredVirtualKeyInjection(
    std::uint16_t virtual_key,
    const TypingContext& context) noexcept {
  if (deferred_clipboard_queue_ == nullptr ||
      !IsDeferredOrderingVirtualKey(virtual_key)) {
    return false;
  }
  deferred_clipboard_queue_->CancelProducer();
  if (window_ == nullptr || context.bypass_typing ||
      context.foreground_process_id == 0 || context.focus_window == 0) {
    return false;
  }

  auto* item = deferred_clipboard_queue_->TryReserveProducer();
  if (item == nullptr) {
    ++deferred_clipboard_queue_full_count_;
    return false;
  }
  item->kind = DeferredClipboardWorkKind::VirtualKey;
  item->snippet_state_preapplied = false;
  item->text_units = 0;
  item->backspace_count = 0;
  item->source_virtual_key = virtual_key;
  item->fallback_character = U'\0';
  item->target_process_id = context.foreground_process_id;
  item->target_focus_window = context.focus_window;
  item->snippet_command = RuntimeSnippetCommand::None;

  const bool needs_message = !deferred_clipboard_message_posted_ &&
      pending_clipboard_sequence_ == 0;
  if (needs_message &&
      PostMessageW(
          window_, kDeferredClipboardInjectionMessage, 0, 0) == FALSE) {
    deferred_clipboard_queue_->CancelProducer();
    return false;
  }
  if (!deferred_clipboard_queue_->CommitProducer()) {
    return false;
  }
  if (needs_message) {
    deferred_clipboard_message_posted_ = true;
  }
  return true;
}

bool Win32InputRuntime::QueueDeferredCommandInjection(
    const InputDecision& decision,
    char32_t fallback_character,
    std::uint16_t source_virtual_key,
    const TypingContext& context) noexcept {
  if (deferred_clipboard_queue_ == nullptr ||
      decision.snippet_command == RuntimeSnippetCommand::None ||
      !decision.extended_insert.empty() || decision.insert_units != 0) {
    return false;
  }
  const std::size_t payload_units =
      decision.snippet_command_payload.size();
  if (payload_units > kMaximumRuntimeSnippetExpansionUtf8Bytes ||
      (decision.snippet_command == RuntimeSnippetCommand::ExternalOutput &&
       payload_units == 0)) {
    return false;
  }

  deferred_clipboard_queue_->CancelProducer();
  if (window_ == nullptr || context.bypass_typing ||
      context.foreground_process_id == 0 || context.focus_window == 0) {
    return false;
  }
  auto* item = deferred_clipboard_queue_->TryReserveProducer();
  if (item == nullptr) {
    ++deferred_clipboard_queue_full_count_;
    return false;
  }
  if (payload_units != 0) {
    std::copy(
        decision.snippet_command_payload.begin(),
        decision.snippet_command_payload.end(),
        item->text_storage.begin());
  }
  item->kind = DeferredClipboardWorkKind::Command;
  item->text_units = payload_units;
  item->backspace_count = decision.backspace_count;
  item->source_virtual_key = source_virtual_key;
  item->fallback_character = fallback_character;
  item->target_process_id = context.foreground_process_id;
  item->target_focus_window = context.focus_window;
  item->snippet_command = decision.snippet_command;
  item->snippet_state_preapplied =
      decision.snippet_command == RuntimeSnippetCommand::ToggleVietnamese;

  const bool needs_message = !deferred_clipboard_message_posted_ &&
      pending_clipboard_sequence_ == 0;
  if (needs_message &&
      PostMessageW(
          window_, kDeferredClipboardInjectionMessage, 0, 0) == FALSE) {
    deferred_clipboard_queue_->CancelProducer();
    return false;
  }
  if (item->snippet_state_preapplied &&
      !PreapplySnippetState(item->snippet_command)) {
    deferred_clipboard_queue_->CancelProducer();
    return false;
  }
  if (!deferred_clipboard_queue_->CommitProducer()) {
    if (item->snippet_state_preapplied) {
      RollbackPreappliedSnippetState(item->snippet_command);
      item->snippet_state_preapplied = false;
    }
    deferred_clipboard_queue_->CancelProducer();
    return false;
  }
  if (needs_message) {
    deferred_clipboard_message_posted_ = true;
  }
  return true;
}

void Win32InputRuntime::RequestDeferredClipboardDrain() noexcept {
  if (stopping_ || window_ == nullptr ||
      deferred_clipboard_message_posted_ ||
      pending_clipboard_sequence_ != 0 ||
      deferred_clipboard_queue_ == nullptr ||
      deferred_clipboard_queue_->empty()) {
    return;
  }
  if (PostMessageW(
          window_, kDeferredClipboardInjectionMessage, 0, 0) != FALSE) {
    deferred_clipboard_message_posted_ = true;
  }
}

Win32InputRuntime::TargetInjectionResult
Win32InputRuntime::InjectDeferredFallback(
    char32_t fallback_character,
    std::uint32_t target_process_id,
    std::uintptr_t target_focus_window) noexcept {
  if (fallback_character == U'\0' ||
      !IsDeferredTargetCurrent(
          target_process_id,
          target_focus_window)) {
    return TargetInjectionResult::FailedBeforeMutation;
  }
  const InputSubmissionResult submission =
      SubmitLiteralUnicodeCharacter(fallback_character);
  if (submission.complete) {
    return TargetInjectionResult::Succeeded;
  }
  return submission.inserted == 0
      ? TargetInjectionResult::FailedBeforeMutation
      : TargetInjectionResult::FailedAfterPossibleMutation;
}

Win32InputRuntime::TargetInjectionResult
Win32InputRuntime::InjectDeferredVirtualKey(
    std::uint16_t virtual_key,
    std::uint32_t target_process_id,
    std::uintptr_t target_focus_window) noexcept {
  if (!IsDeferredOrderingVirtualKey(virtual_key) ||
      !IsDeferredTargetCurrent(
          target_process_id,
          target_focus_window)) {
    return TargetInjectionResult::FailedBeforeMutation;
  }
  const InputSubmissionResult submission =
      SubmitVirtualKeyPair(virtual_key);
  if (submission.complete) {
    return TargetInjectionResult::Succeeded;
  }
  return submission.inserted == 0
      ? TargetInjectionResult::FailedBeforeMutation
      : TargetInjectionResult::FailedAfterPossibleMutation;
}

Win32InputRuntime::TargetInjectionResult
Win32InputRuntime::InjectDeferredCommand(
    RuntimeSnippetCommand command,
    bool state_preapplied,
    std::uint16_t backspace_count,
    std::u16string_view payload,
    std::uint32_t target_process_id,
    std::uintptr_t target_focus_window) noexcept {
  if (command == RuntimeSnippetCommand::None ||
      !IsDeferredTargetCurrent(
          target_process_id,
          target_focus_window)) {
    return TargetInjectionResult::FailedBeforeMutation;
  }

  const bool external = command == RuntimeSnippetCommand::ExternalOutput;
  if (external && !ReserveExternalSnippetCommand(
          payload, target_process_id, target_focus_window)) {
    return TargetInjectionResult::FailedBeforeMutation;
  }

  InputDecision erase{};
  erase.suppress = true;
  erase.backspace_count = backspace_count;
  const std::size_t required = RequiredKeyboardInputEvents(erase);
  InputSubmissionResult submission{true, 0};
  if (required != 0) {
    if (required > kMaximumKeyboardInputEvents) {
      if (external) {
        external_command_queue_.CancelProducer();
      }
      return TargetInjectionResult::FailedBeforeMutation;
    }
    std::array<INPUT, kMaximumKeyboardInputEvents> inputs{};
    const std::size_t count = BuildKeyboardInputSequence(
        erase,
        std::span<INPUT>(inputs.data(), required));
    if (count != required) {
      if (external) {
        external_command_queue_.CancelProducer();
      }
      return TargetInjectionResult::FailedBeforeMutation;
    }
    submission = SubmitInputSequence<kMaximumKeyboardInputEvents>(
        std::span<INPUT>(inputs.data(), count));
  }

  if (!submission.complete) {
    if (external) {
      external_command_queue_.CancelProducer();
    }
    return submission.inserted == 0
        ? TargetInjectionResult::FailedBeforeMutation
        : TargetInjectionResult::FailedAfterPossibleMutation;
  }

  if (external) {
    CommitDeferredSnippetCommand(command);
  } else {
    ExecuteSnippetAction(command, state_preapplied);
  }
  return TargetInjectionResult::Succeeded;
}

void Win32InputRuntime::HandleDeferredClipboardInjections() noexcept {
  deferred_clipboard_message_posted_ = false;
  if (pending_clipboard_sequence_ != 0 ||
      deferred_clipboard_queue_ == nullptr) {
    return;
  }
  bool transform_state_valid = true;
  bool raw_fallback_safe = true;

  for (;;) {
    auto* item = deferred_clipboard_queue_->TryPeekConsumer();
    if (item == nullptr) {
      break;
    }

    const bool transform_item =
        item->kind == DeferredClipboardWorkKind::Transform;
    const bool literal_item =
        item->kind == DeferredClipboardWorkKind::Literal;
    const bool command_item =
        item->kind == DeferredClipboardWorkKind::Command;
    auto inject_degraded_item = [&]() noexcept {
      return item->kind == DeferredClipboardWorkKind::VirtualKey
          ? InjectDeferredVirtualKey(
                item->source_virtual_key,
                item->target_process_id,
                item->target_focus_window)
          : InjectDeferredFallback(
                item->fallback_character,
                item->target_process_id,
                item->target_focus_window);
    };

    if (transform_state_valid) {
      TargetInjectionResult result =
          TargetInjectionResult::FailedBeforeMutation;
      if (transform_item) {
        InputDecision decision{};
        decision.suppress = true;
        decision.backspace_count = item->backspace_count;
        decision.extended_insert = std::u16string_view(
            item->text_storage.data(), item->text_units);
        result = InjectViaClipboard(
            decision,
            item->target_process_id,
            item->target_focus_window);
      } else if (command_item) {
        result = InjectDeferredCommand(
            item->snippet_command,
            item->snippet_state_preapplied,
            item->backspace_count,
            std::u16string_view(
                item->text_storage.data(), item->text_units),
            item->target_process_id,
            item->target_focus_window);
      } else {
        result = inject_degraded_item();
      }

      if (result == TargetInjectionResult::Succeeded) {
        ++successful_injection_count_;
        if (transform_item) {
          ExecuteSnippetAction(item->snippet_command);
        } else if (literal_item) {
          ++deferred_literal_injection_count_;
        } else if (!command_item) {
          ++deferred_virtual_key_injection_count_;
        }
      } else {
        ++failed_injection_count_;
        transform_state_valid = false;
        raw_fallback_safe =
            result == TargetInjectionResult::FailedBeforeMutation;
        if (command_item && item->snippet_state_preapplied) {
          if (raw_fallback_safe) {
            RollbackPreappliedSnippetState(item->snippet_command);
            item->snippet_state_preapplied = false;
          } else {
            ExecuteSnippetAction(item->snippet_command, true);
          }
        }
        controller_.Reset();
        RequestPointerRegistration(false);
        RequestSnippetOverlayUpdate();
        if (pressed_keys_.Get(item->source_virtual_key)) {
          owned_text_keys_.Set(item->source_virtual_key, true);
        }
        if ((transform_item || command_item) && raw_fallback_safe) {
          const TargetInjectionResult fallback_result =
              inject_degraded_item();
          if (fallback_result == TargetInjectionResult::Succeeded) {
            ++deferred_clipboard_fallback_count_;
          } else {
            raw_fallback_safe = false;
          }
        } else {
          raw_fallback_safe = false;
        }
      }
    } else {
      ++failed_injection_count_;
      if (command_item && item->snippet_state_preapplied) {
        RollbackPreappliedSnippetState(item->snippet_command);
        item->snippet_state_preapplied = false;
      }
      if (pressed_keys_.Get(item->source_virtual_key)) {
        owned_text_keys_.Set(item->source_virtual_key, true);
      }
      if (raw_fallback_safe) {
        const TargetInjectionResult fallback_result =
            inject_degraded_item();
        if (fallback_result == TargetInjectionResult::Succeeded) {
          ++deferred_clipboard_fallback_count_;
        } else {
          raw_fallback_safe = false;
        }
      }
    }

    if (item->snippet_state_preapplied) {
      if (preapplied_vietnamese_toggle_count_ != 0) {
        --preapplied_vietnamese_toggle_count_;
      }
      item->snippet_state_preapplied = false;
      if (preapplied_vietnamese_toggle_count_ == 0) {
        RequestTrayUpdate();
      }
    }
    item->kind = DeferredClipboardWorkKind::Transform;
    item->text_units = 0;
    item->backspace_count = 0;
    item->source_virtual_key = 0;
    item->fallback_character = U'\0';
    item->target_process_id = 0;
    item->target_focus_window = 0;
    item->snippet_command = RuntimeSnippetCommand::None;
    item->snippet_state_preapplied = false;
    static_cast<void>(deferred_clipboard_queue_->PopConsumer());
    if (pending_clipboard_sequence_ != 0) {
      // Ctrl+V is asynchronous. Do not replace/restore the clipboard for the
      // next transaction until the settle timer confirms the first paste had
      // a chance to consume its private payload.
      return;
    }
  }
}

bool Win32InputRuntime::PrepareDeferredSnippetCommand(
    const InputDecision& decision) noexcept {
  external_command_queue_.CancelProducer();
  switch (decision.snippet_command) {
    case RuntimeSnippetCommand::None:
      return true;
    case RuntimeSnippetCommand::ToggleVietnamese:
    case RuntimeSnippetCommand::ToggleDictation:
      if (window_ == nullptr) {
        return false;
      }
      if (pending_snippet_actions_.load(std::memory_order_acquire) == 0 &&
          PostMessageW(window_, kDeferredSnippetActionMessage, 0, 0) == FALSE) {
        return false;
      }
      return true;
    case RuntimeSnippetCommand::ExternalOutput:
      break;
    default:
      return false;
  }

  return ReserveExternalSnippetCommand(
      decision.snippet_command_payload,
      decision.snippet_target_process_id,
      decision.snippet_target_focus_window);
}

bool Win32InputRuntime::ReserveExternalSnippetCommand(
    std::u16string_view payload,
    std::uint32_t target_process_id,
    std::uintptr_t target_focus_window) noexcept {
  if (external_command_thread_ == nullptr ||
      external_command_event_ == nullptr || payload.empty() ||
      payload.size() > kMaximumRuntimeSnippetExpansionUtf8Bytes ||
      target_process_id == 0 || target_focus_window == 0 ||
      shell_execute_ == nullptr) {
    return false;
  }

  auto* item = external_command_queue_.TryReserveProducer();
  if (item == nullptr) {
    dropped_external_command_count_.fetch_add(1, std::memory_order_relaxed);
    return false;
  }
  std::copy(payload.begin(), payload.end(), item->payload.begin());
  item->payload_units = payload.size();
  item->target_process_id = target_process_id;
  item->target_focus_window = target_focus_window;
  return true;
}

void Win32InputRuntime::CommitDeferredSnippetCommand(
    RuntimeSnippetCommand command) noexcept {
  switch (command) {
    case RuntimeSnippetCommand::ToggleVietnamese:
      pending_snippet_actions_.fetch_xor(
          kPendingToggleVietnameseAction,
          std::memory_order_release);
      break;
    case RuntimeSnippetCommand::ToggleDictation:
      pending_snippet_actions_.fetch_xor(
          kPendingToggleDictationAction,
          std::memory_order_release);
      break;
    case RuntimeSnippetCommand::ExternalOutput:
      if (external_command_queue_.CommitProducer()) {
        static_cast<void>(SetEvent(external_command_event_));
      }
      break;
    case RuntimeSnippetCommand::None:
    default:
      break;
  }
}

void Win32InputRuntime::HandleDeferredSnippetActions() noexcept {
  const std::uint32_t actions =
      pending_snippet_actions_.exchange(0, std::memory_order_acq_rel);
  if ((actions & kPendingToggleVietnameseAction) != 0) {
    ExecuteSnippetAction(RuntimeSnippetCommand::ToggleVietnamese);
  }
  if ((actions & kPendingToggleDictationAction) != 0) {
    ExecuteSnippetAction(RuntimeSnippetCommand::ToggleDictation);
  }
}

bool Win32InputRuntime::PreapplySnippetState(
    RuntimeSnippetCommand command) noexcept {
  if (command != RuntimeSnippetCommand::ToggleVietnamese) {
    return false;
  }
  profile_.vietnamese_enabled = !profile_.vietnamese_enabled;
  controller_.ApplyProfile(profile_);
  ++preapplied_vietnamese_toggle_count_;
  RequestPointerRegistration(false);
  RequestSnippetOverlayUpdate();
  return true;
}

void Win32InputRuntime::RollbackPreappliedSnippetState(
    RuntimeSnippetCommand command) noexcept {
  if (command != RuntimeSnippetCommand::ToggleVietnamese ||
      preapplied_vietnamese_toggle_count_ == 0) {
    return;
  }
  profile_.vietnamese_enabled = !profile_.vietnamese_enabled;
  controller_.ApplyProfile(profile_);
  --preapplied_vietnamese_toggle_count_;
  RequestPointerRegistration(false);
  RequestSnippetOverlayUpdate();
  if (preapplied_vietnamese_toggle_count_ == 0) {
    RequestTrayUpdate();
  }
}

void Win32InputRuntime::ExecuteSnippetAction(
    RuntimeSnippetCommand command,
    bool state_preapplied) noexcept {
  switch (command) {
    case RuntimeSnippetCommand::ToggleVietnamese:
      if (state_preapplied) {
        committed_vietnamese_enabled_ = !committed_vietnamese_enabled_;
      } else {
        profile_.vietnamese_enabled = !profile_.vietnamese_enabled;
        controller_.ApplyProfile(profile_);
        committed_vietnamese_enabled_ = profile_.vietnamese_enabled;
        RequestPointerRegistration(false);
        RequestSnippetOverlayUpdate();
        RequestTrayUpdate();
      }
      static_cast<void>(QueueManagedCommand(
          committed_vietnamese_enabled_
              ? RuntimeCommand::SetVietnameseEnabled
              : RuntimeCommand::SetVietnameseDisabled));
      break;
    case RuntimeSnippetCommand::ToggleDictation:
      static_cast<void>(QueueManagedCommand(RuntimeCommand::ToggleDictation));
      break;
    case RuntimeSnippetCommand::None:
    case RuntimeSnippetCommand::ExternalOutput:
    default:
      break;
  }
}

bool Win32InputRuntime::StartExternalCommandWorker() noexcept {
  if (external_command_thread_ != nullptr) {
    return true;
  }
  external_command_event_ = CreateEventW(nullptr, FALSE, FALSE, nullptr);
  external_command_stop_event_ = CreateEventW(nullptr, TRUE, FALSE, nullptr);
  if (external_command_event_ == nullptr ||
      external_command_stop_event_ == nullptr) {
    StopExternalCommandWorker();
    return false;
  }
  external_command_thread_ = CreateThread(
      nullptr,
      kExternalCommandWorkerStackReservation,
      &Win32InputRuntime::ExternalCommandWorkerProcedure,
      this,
      STACK_SIZE_PARAM_IS_A_RESERVATION,
      nullptr);
  if (external_command_thread_ == nullptr) {
    StopExternalCommandWorker();
    return false;
  }
  return true;
}

void Win32InputRuntime::StopExternalCommandWorker() noexcept {
  if (external_command_stop_event_ != nullptr) {
    static_cast<void>(SetEvent(external_command_stop_event_));
  }
  if (external_command_thread_ != nullptr) {
    static_cast<void>(WaitForSingleObject(external_command_thread_, INFINITE));
    CloseHandle(external_command_thread_);
    external_command_thread_ = nullptr;
  }
  if (external_command_event_ != nullptr) {
    CloseHandle(external_command_event_);
    external_command_event_ = nullptr;
  }
  if (external_command_stop_event_ != nullptr) {
    CloseHandle(external_command_stop_event_);
    external_command_stop_event_ = nullptr;
  }
  external_command_queue_.ResetAfterShutdown();
}

DWORD Win32InputRuntime::RunExternalCommandWorker() noexcept {
  const std::array<HANDLE, 2> events = {
      external_command_stop_event_,
      external_command_event_,
  };
  for (;;) {
    const DWORD wait = WaitForMultipleObjects(
        static_cast<DWORD>(events.size()),
        events.data(),
        FALSE,
        INFINITE);
    if (wait == WAIT_OBJECT_0) {
      return 0;
    }
    if (wait != WAIT_OBJECT_0 + 1) {
      return 1;
    }

    for (;;) {
      auto* item = external_command_queue_.TryPeekConsumer();
      if (item == nullptr) {
        break;
      }
      if (IsDeferredTargetCurrent(
              item->target_process_id,
              item->target_focus_window)) {
        static_cast<void>(LaunchExternalSnippetCommand(
            std::u16string_view(item->payload.data(), item->payload_units),
            item->target_process_id,
            item->target_focus_window));
      }
      item->payload_units = 0;
      item->target_process_id = 0;
      item->target_focus_window = 0;
      static_cast<void>(external_command_queue_.PopConsumer());
    }
  }
}

bool Win32InputRuntime::IsDeferredTargetCurrent(
    std::uint32_t target_process_id,
    std::uintptr_t target_focus_window) const noexcept {
  const HWND expected_focus =
      reinterpret_cast<HWND>(target_focus_window);
  if (target_process_id == 0 || expected_focus == nullptr ||
      IsWindow(expected_focus) == FALSE) {
    return false;
  }
  GUITHREADINFO info{};
  info.cbSize = sizeof(info);
  if (GetGUIThreadInfo(0, &info) == FALSE ||
      info.hwndFocus != expected_focus) {
    return false;
  }
  DWORD process_id = 0;
  return GetWindowThreadProcessId(expected_focus, &process_id) != 0 &&
      process_id == target_process_id;
}

bool Win32InputRuntime::LaunchExternalSnippetCommand(
    std::u16string_view payload,
    std::uint32_t target_process_id,
    std::uintptr_t target_focus_window) noexcept {
  try {
    if (payload.empty() || payload.size() > 16 * 1024 ||
        target_process_id == 0 || target_focus_window == 0 ||
        shell_execute_ == nullptr ||
        !IsDeferredTargetCurrent(
            target_process_id,
            target_focus_window)) {
      return false;
    }

    const int payload_bytes = WideCharToMultiByte(
        CP_UTF8,
        WC_ERR_INVALID_CHARS,
        reinterpret_cast<LPCWCH>(payload.data()),
        static_cast<int>(payload.size()),
        nullptr,
        0,
        nullptr,
        nullptr);
    if (payload_bytes <= 0 || payload_bytes > 32 * 1024) {
      return false;
    }
    std::vector<char> utf8(static_cast<std::size_t>(payload_bytes));
    if (WideCharToMultiByte(
            CP_UTF8,
            WC_ERR_INVALID_CHARS,
            reinterpret_cast<LPCWCH>(payload.data()),
            static_cast<int>(payload.size()),
            utf8.data(),
            payload_bytes,
            nullptr,
            nullptr) != payload_bytes) {
      return false;
    }

    std::array<wchar_t, 32768> local_app_data{};
    const DWORD local_length = GetEnvironmentVariableW(
        L"LOCALAPPDATA",
        local_app_data.data(),
        static_cast<DWORD>(local_app_data.size()));
    if (local_length == 0 || local_length >= local_app_data.size()) {
      return false;
    }
    std::wstring keyina_directory(local_app_data.data(), local_length);
    keyina_directory.append(L"\\Keyina");
    if (CreateDirectoryW(keyina_directory.c_str(), nullptr) == FALSE &&
        GetLastError() != ERROR_ALREADY_EXISTS) {
      return false;
    }
    std::wstring commands_directory = keyina_directory + L"\\commands";
    if (CreateDirectoryW(commands_directory.c_str(), nullptr) == FALSE &&
        GetLastError() != ERROR_ALREADY_EXISTS) {
      return false;
    }

    static std::atomic_uint64_t request_counter{0};
    const auto request_id = request_counter.fetch_add(1) + 1;
    std::array<wchar_t, 32768> request_path{};
    if (swprintf_s(
            request_path.data(),
            request_path.size(),
            L"%ls\\snippet-%lu-%llu-%llu.bin",
            commands_directory.c_str(),
            static_cast<unsigned long>(GetCurrentProcessId()),
            static_cast<unsigned long long>(GetTickCount64()),
            static_cast<unsigned long long>(request_id)) <= 0) {
      return false;
    }

    HANDLE file = CreateFileW(
        request_path.data(),
        GENERIC_WRITE,
        0,
        nullptr,
        CREATE_NEW,
        FILE_ATTRIBUTE_TEMPORARY | FILE_FLAG_WRITE_THROUGH,
        nullptr);
    if (file == INVALID_HANDLE_VALUE) {
      return false;
    }

    std::array<std::byte, 20> header{};
    header[0] = static_cast<std::byte>('K');
    header[1] = static_cast<std::byte>('Y');
    header[2] = static_cast<std::byte>('S');
    header[3] = static_cast<std::byte>('C');
    header[4] = static_cast<std::byte>(1);
    header[5] = static_cast<std::byte>(20);
    const auto write32 = [&header](std::size_t offset, std::uint32_t value) {
      for (std::size_t index = 0; index < 4; ++index) {
        header[offset + index] = static_cast<std::byte>(
            (value >> (index * 8u)) & 0xFFu);
      }
    };
    const auto write64 = [&header](std::size_t offset, std::uint64_t value) {
      for (std::size_t index = 0; index < 8; ++index) {
        header[offset + index] = static_cast<std::byte>(
            (value >> (index * 8u)) & 0xFFu);
      }
    };
    write32(8, target_process_id);
    write64(12, static_cast<std::uint64_t>(target_focus_window));

    DWORD written = 0;
    const bool header_written = WriteFile(
        file,
        header.data(),
        static_cast<DWORD>(header.size()),
        &written,
        nullptr) != FALSE && written == header.size();
    const bool payload_written = header_written && WriteFile(
        file,
        utf8.data(),
        static_cast<DWORD>(utf8.size()),
        &written,
        nullptr) != FALSE && written == utf8.size();
    const bool flushed = payload_written && FlushFileBuffers(file) != FALSE;
    CloseHandle(file);
    if (!flushed) {
      DeleteFileW(request_path.data());
      return false;
    }

    std::array<wchar_t, 32768> executable{};
    const DWORD executable_length = GetModuleFileNameW(
        nullptr, executable.data(), static_cast<DWORD>(executable.size()));
    if (executable_length == 0 || executable_length >= executable.size()) {
      DeleteFileW(request_path.data());
      return false;
    }
    wchar_t* separator = std::wcsrchr(executable.data(), L'\\');
    if (separator == nullptr) {
      DeleteFileW(request_path.data());
      return false;
    }
    *separator = L'\0';

    std::array<wchar_t, 32768> companion{};
    if (swprintf_s(
            companion.data(),
            companion.size(),
            L"%ls\\Keyina.Host.exe",
            executable.data()) <= 0) {
      DeleteFileW(request_path.data());
      return false;
    }
    std::wstring arguments = L"--snippet-command-file=\"";
    arguments.append(request_path.data());
    arguments.push_back(L'\"');
    if (!IsDeferredTargetCurrent(
            target_process_id,
            target_focus_window)) {
      DeleteFileW(request_path.data());
      return false;
    }
    const HINSTANCE result = shell_execute_(
        window_,
        L"open",
        companion.data(),
        arguments.c_str(),
        executable.data(),
        SW_SHOWNOACTIVATE);
    if (reinterpret_cast<INT_PTR>(result) <= 32) {
      DeleteFileW(request_path.data());
      return false;
    }
    return true;
  } catch (...) {
    return false;
  }
}

bool Win32InputRuntime::QueueManagedCommand(
    RuntimeCommand command) noexcept {
  return command != RuntimeCommand::None && window_ != nullptr &&
         PostMessageW(
             window_,
             kRuntimeCommandMessage,
             static_cast<WPARAM>(command),
             0) != FALSE;
}

bool Win32InputRuntime::LaunchManagedCommand(
    RuntimeCommand command) noexcept {
  const wchar_t* argument = RuntimeCommandArgument(command);
  if (argument == nullptr || shell_execute_ == nullptr) {
    return false;
  }

  std::array<wchar_t, 32768> executable{};
  const DWORD length = GetModuleFileNameW(
      nullptr, executable.data(), static_cast<DWORD>(executable.size()));
  if (length == 0 || length >= executable.size()) {
    return false;
  }
  wchar_t* separator = std::wcsrchr(executable.data(), L'\\');
  if (separator == nullptr) {
    return false;
  }
  *separator = L'\0';

  std::array<wchar_t, 32768> companion{};
  if (swprintf_s(
          companion.data(),
          companion.size(),
          L"%ls\\Keyina.Host.exe",
          executable.data()) <= 0) {
    return false;
  }

  const HINSTANCE result = shell_execute_(
      window_,
      L"open",
      companion.data(),
      argument,
      executable.data(),
      SW_SHOWNOACTIVATE);
  return reinterpret_cast<INT_PTR>(result) > 32;
}

bool Win32InputRuntime::IsCommandCompanionActive() const noexcept {
  HANDLE mutex = OpenMutexW(
      SYNCHRONIZE,
      FALSE,
      kCommandCompanionMutexName);
  if (mutex == nullptr) {
    return false;
  }
  CloseHandle(mutex);
  return true;
}

void Win32InputRuntime::ReloadProfileIfChanged() noexcept {
  if (preapplied_vietnamese_toggle_count_ != 0) {
    return;
  }
  bool input_profile_changed = false;
  std::array<wchar_t, 32768> input_path{};
  FILETIME input_write_time{};
  if (ResolveRuntimeInputProfilePath(input_path) &&
      TryGetRuntimeInputProfileWriteTime(
          input_path.data(), input_write_time) &&
      (!profile_write_time_known_ ||
       CompareFileTime(&profile_write_time_, &input_write_time) != 0)) {
    RuntimeInputProfile profile{};
    if (TryReadRuntimeInputProfile(input_path.data(), profile)) {
      profile_write_time_ = input_write_time;
      profile_write_time_known_ = true;
      profile_ = profile;
      committed_vietnamese_enabled_ = profile_.vietnamese_enabled;
      controller_.ApplyProfile(profile_);
      input_profile_changed = true;
    }
  }

  std::array<wchar_t, 32768> snippet_path{};
  FILETIME snippet_write_time{};
  if (ResolveRuntimeSnippetProfilePath(snippet_path) &&
      TryGetRuntimeInputProfileWriteTime(
          snippet_path.data(), snippet_write_time) &&
      (!snippet_profile_write_time_known_ ||
       CompareFileTime(
           &snippet_profile_write_time_, &snippet_write_time) != 0)) {
    RuntimeSnippetProfile snippets{};
    if (TryReadRuntimeSnippetProfile(snippet_path.data(), snippets)) {
      snippet_profile_write_time_ = snippet_write_time;
      snippet_profile_write_time_known_ = true;
      controller_.ApplySnippets(std::move(snippets));
      RequestPointerRegistration(false);
    }
  }

  if (!input_profile_changed) {
    return;
  }
  owned_text_keys_.Clear();
  hotkey_router_.Reset();
  toggle_chord_active_ = false;
  toggle_chord_contaminated_ = false;
  RequestPointerRegistration(false);
  UpdateTray();
}

void Win32InputRuntime::RefreshModifierState() noexcept {
  std::uint8_t state = 0;
  if (pressed_keys_.Get(VK_SHIFT) ||
      pressed_keys_.Get(VK_LSHIFT) ||
      pressed_keys_.Get(VK_RSHIFT)) {
    state |= kShiftModifier;
  }
  if (pressed_keys_.Get(VK_CONTROL) ||
      pressed_keys_.Get(VK_LCONTROL) ||
      pressed_keys_.Get(VK_RCONTROL)) {
    state |= kControlModifier;
  }
  if (pressed_keys_.Get(VK_MENU) ||
      pressed_keys_.Get(VK_LMENU) ||
      pressed_keys_.Get(VK_RMENU)) {
    state |= kAltModifier;
  }
  if (pressed_keys_.Get(VK_LWIN) || pressed_keys_.Get(VK_RWIN)) {
    state |= kWindowsModifier;
  }
  modifier_state_ = state;
}

void Win32InputRuntime::UpdateSnippetOverlay() noexcept {
  const std::u32string_view token = controller_.snippet_token();
  if (!IsRuntimeSnippetSuggestionPrefix(token)) {
    HideSnippetOverlay();
    return;
  }

  const auto suggestions = controller_.snippet_suggestions(
      kMaximumVisibleSnippetSuggestions);
  if (suggestions.empty()) {
    HideSnippetOverlay();
    return;
  }

  const std::wstring text = BuildSnippetOverlayText(token, suggestions);
  if (snippet_overlay_window_ == nullptr) {
    snippet_overlay_window_ = CreateWindowExW(
        WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE |
            WS_EX_TRANSPARENT,
        L"STATIC",
        kSnippetOverlayTitle,
        WS_POPUP | WS_BORDER | SS_LEFT,
        0,
        0,
        420,
        240,
        nullptr,
        nullptr,
        GetModuleHandleW(nullptr),
        nullptr);
    if (snippet_overlay_window_ == nullptr) {
      return;
    }
    SendMessageW(
        snippet_overlay_window_,
        WM_SETFONT,
        reinterpret_cast<WPARAM>(GetStockObject(DEFAULT_GUI_FONT)),
        TRUE);
  }

  SetWindowTextW(snippet_overlay_window_, text.c_str());
  RECT work_area{};
  if (!SystemParametersInfoW(SPI_GETWORKAREA, 0, &work_area, 0)) {
    work_area = RECT{0, 0, GetSystemMetrics(SM_CXSCREEN),
                     GetSystemMetrics(SM_CYSCREEN)};
  }
  const int width = 440;
  const int height = std::min<int>(
      110 + static_cast<int>(suggestions.size()) * 30,
      360);
  const int x = std::max(work_area.left + 12, work_area.right - width - 20);
  const int y = std::max(work_area.top + 12, work_area.bottom - height - 20);
  if (SetWindowPos(
          snippet_overlay_window_,
          HWND_TOPMOST,
          x,
          y,
          width,
          height,
          SWP_NOACTIVATE | SWP_SHOWWINDOW) != FALSE) {
    snippet_overlay_visible_ = true;
  }
}

void Win32InputRuntime::HideSnippetOverlay() noexcept {
  if (!snippet_overlay_visible_ || snippet_overlay_window_ == nullptr) {
    return;
  }
  ShowWindow(snippet_overlay_window_, SW_HIDE);
  snippet_overlay_visible_ = false;
}

void Win32InputRuntime::UpdateTray() noexcept {
  if (!enable_tray_ || window_ == nullptr ||
      shell_notify_icon_ == nullptr) {
    return;
  }
  tray_data_ = {};
  tray_data_.cbSize = sizeof(tray_data_);
  tray_data_.hWnd = window_;
  tray_data_.uID = kTrayIdentifier;
  tray_data_.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
  tray_data_.uCallbackMessage = kTrayCallbackMessage;
  tray_data_.hIcon = profile_.vietnamese_enabled
                         ? active_icon_
                         : inactive_icon_;
  wcscpy_s(
      tray_data_.szTip,
      profile_.vietnamese_enabled
          ? L"Keyina — Tiếng Việt đang bật"
          : L"Keyina — Tiếng Việt đang tắt");
  if (!tray_added_) {
    tray_added_ = shell_notify_icon_(NIM_ADD, &tray_data_) != FALSE;
  } else {
    shell_notify_icon_(NIM_MODIFY, &tray_data_);
  }
}

void Win32InputRuntime::ShowTrayMenu() noexcept {
  HMENU menu = CreatePopupMenu();
  if (menu == nullptr) {
    return;
  }
  AppendMenuW(
      menu, MF_STRING | MF_DISABLED | MF_GRAYED, 0,
      profile_.vietnamese_enabled
          ? L"Keyina · Bộ gõ đang bật"
          : L"Keyina · Bộ gõ đang tắt");
  AppendMenuW(menu, MF_SEPARATOR, 0, nullptr);
  AppendMenuW(
      menu, MF_STRING | (profile_.vietnamese_enabled ? MF_CHECKED : 0),
      kToggleMenuCommand,
      profile_.vietnamese_enabled
          ? L"Tắt bộ gõ tiếng Việt"
          : L"Bật bộ gõ tiếng Việt");
  AppendMenuW(menu, MF_STRING, kDictationMenuCommand,
              L"Nhập bằng giọng nói");
  AppendMenuW(menu, MF_STRING, kTranslateMenuCommand,
              L"Dịch vùng chọn");
  AppendMenuW(menu, MF_SEPARATOR, 0, nullptr);
  AppendMenuW(menu, MF_STRING, kSettingsMenuCommand, L"Mở cài đặt");
  AppendMenuW(menu, MF_SEPARATOR, 0, nullptr);
  AppendMenuW(menu, MF_STRING, kExitMenuCommand, L"Thoát Keyina");

  POINT point{};
  if (GetCursorPos(&point)) {
    SetForegroundWindow(window_);
    TrackPopupMenu(
        menu, TPM_RIGHTBUTTON | TPM_BOTTOMALIGN | TPM_LEFTALIGN,
        point.x, point.y, 0, window_, nullptr);
    PostMessageW(window_, WM_NULL, 0, 0);
  }
  DestroyMenu(menu);
}

void Win32InputRuntime::OpenManagedSettings() noexcept {
  if (shell_execute_ == nullptr) {
    return;
  }
  std::array<wchar_t, 32768> executable{};
  const DWORD length = GetModuleFileNameW(
      nullptr, executable.data(), static_cast<DWORD>(executable.size()));
  if (length == 0 || length >= executable.size()) {
    return;
  }
  wchar_t* separator = std::wcsrchr(executable.data(), L'\\');
  if (separator == nullptr) {
    return;
  }
  *separator = L'\0';
  std::array<wchar_t, 32768> companion{};
  if (swprintf_s(companion.data(), companion.size(), L"%ls\\Keyina.Host.exe",
                 executable.data()) <= 0) {
    return;
  }
  shell_execute_(
      window_, L"open", companion.data(), L"--companion-settings",
      executable.data(), SW_SHOWNORMAL);
}

void Win32InputRuntime::RequestExit() noexcept {
  stopping_ = true;
  PostQuitMessage(0);
}

RuntimeInputProfile LoadRuntimeInputProfileOrDefault() noexcept {
  RuntimeInputProfile profile = DefaultRuntimeInputProfile();
  std::array<wchar_t, 32768> path{};
  if (ResolveRuntimeInputProfilePath(path)) {
    static_cast<void>(TryReadRuntimeInputProfile(path.data(), profile));
  }
  return profile;
}

NativeResidentResourceSnapshot MeasureNativeResidentResources(
    Win32InputRuntime& runtime,
    DWORD duration_milliseconds,
    std::uint32_t baseline_thread_count) noexcept {
  NativeResidentResourceSnapshot snapshot{};
  FILETIME creation{}, exit{}, kernel_before{}, user_before{};
  FILETIME kernel_after{}, user_after{};
  GetProcessTimes(
      GetCurrentProcess(), &creation, &exit, &kernel_before, &user_before);
  const std::uint64_t processed_before =
      runtime.processed_keyboard_events();

  runtime.PumpMessagesFor(duration_milliseconds);

  GetProcessTimes(
      GetCurrentProcess(), &creation, &exit, &kernel_after, &user_after);
  PROCESS_MEMORY_COUNTERS_EX counters{};
  counters.cb = sizeof(counters);
  GetProcessMemoryInfo(
      GetCurrentProcess(),
      reinterpret_cast<PROCESS_MEMORY_COUNTERS*>(&counters),
      sizeof(counters));
  DWORD handle_count = 0;
  GetProcessHandleCount(GetCurrentProcess(), &handle_count);

  const std::uint64_t cpu_100ns =
      (FileTimeValue(kernel_after) - FileTimeValue(kernel_before)) +
      (FileTimeValue(user_after) - FileTimeValue(user_before));
  const DWORD processor_count =
      std::max<DWORD>(GetActiveProcessorCount(ALL_PROCESSOR_GROUPS), 1);
  const double available_100ns =
      static_cast<double>(duration_milliseconds) * 10000.0 *
      static_cast<double>(processor_count);

  snapshot.working_set_bytes = counters.WorkingSetSize;
  snapshot.private_working_set_bytes = QueryPrivateWorkingSetBytes();
  snapshot.private_memory_bytes = counters.PrivateUsage;
  snapshot.thread_count = CountCurrentProcessThreads();
  snapshot.thread_count_delta =
      snapshot.thread_count > baseline_thread_count
          ? snapshot.thread_count - baseline_thread_count
          : 0;
  snapshot.handle_count = handle_count;
  snapshot.cpu_percent = available_100ns == 0.0
                             ? 0.0
                             : static_cast<double>(cpu_100ns) * 100.0 /
                                   available_100ns;
  const std::uint64_t processed_after =
      runtime.processed_keyboard_events();
  snapshot.processed_keyboard_events =
      processed_after >= processed_before
          ? processed_after - processed_before
          : 0;
  snapshot.hook_running = runtime.hook_running();
  snapshot.contaminated_by_input =
      snapshot.processed_keyboard_events != 0;
  // Physical desktop input can legitimately arrive while this global-hook
  // probe is running. Keep that fact in the snapshot so benchmark callers
  // can reject contaminated samples, but do not turn unrelated user input
  // into a product resource-budget failure.
  snapshot.budget_pass = snapshot.hook_running &&
      snapshot.private_working_set_bytes <= kTenMiB &&
      snapshot.private_memory_bytes <= kTenMiB &&
      snapshot.thread_count_delta == 0;
  return snapshot;
}

}  // namespace keyina::windows
