#include <keyina/windows/win32_input_runtime.h>
#include <keyina/windows/input_injection.h>
#include <keyina/windows/pointer_input.h>

#include <psapi.h>
#include <shellapi.h>
#include <tlhelp32.h>

#include <algorithm>
#include <array>
#include <cstddef>
#include <cstdint>
#include <cwchar>

namespace keyina::windows {
namespace {

constexpr wchar_t kWindowClassName[] = L"KeyinaNativeInputWindow";
constexpr UINT kPointerRegistrationMessage = WM_APP + 1;
constexpr UINT kTrayCallbackMessage = WM_APP + 2;
constexpr UINT kRuntimeCommandMessage = WM_APP + 3;
constexpr UINT_PTR kProfileReloadTimerIdentifier = 1;
constexpr UINT kProfileReloadIntervalMilliseconds = 1000;
constexpr UINT kTrayIdentifier = 1;
constexpr wchar_t kCommandCompanionMutexName[] =
    L"Local\\Keyina.CommandCompanion";
constexpr UINT kToggleMenuCommand = 1001;
constexpr UINT kSettingsMenuCommand = 1002;
constexpr UINT kExitMenuCommand = 1003;
constexpr std::uint8_t kControlModifier = 1u << 0u;
constexpr std::uint8_t kShiftModifier = 1u << 1u;
constexpr std::uint8_t kAltModifier = 1u << 2u;
constexpr std::uint8_t kWindowsModifier = 1u << 3u;
constexpr std::uint64_t kTenMiB = 10ULL * 1024ULL * 1024ULL;

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

bool ResolveRuntimeInputProfilePath(
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
             L"%ls\\Keyina\\runtime-input.bin",
             local_app_data.data()) > 0;
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

Win32InputRuntime::Win32InputRuntime(RuntimeInputProfile profile,
                                     bool enable_tray) noexcept
    : profile_(profile), controller_(profile), enable_tray_(enable_tray) {}

Win32InputRuntime::~Win32InputRuntime() { Stop(); }

bool Win32InputRuntime::Start() noexcept {
  stopping_ = false;
  pressed_keys_.Clear();
  hotkey_router_.Reset();
  toggle_chord_active_ = false;
  toggle_chord_contaminated_ = false;
  startup_stage_ = NativeRuntimeStartupStage::None;
  startup_error_ = ERROR_SUCCESS;
  if (hook_ != nullptr || active_runtime_ != nullptr) {
    startup_error_ = ERROR_ALREADY_EXISTS;
    return false;
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
  ReloadProfileIfChanged();
  profile_timer_ = SetTimer(
      window_,
      kProfileReloadTimerIdentifier,
      kProfileReloadIntervalMilliseconds,
      nullptr);
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
  pointer_registration_desired_ = false;
  ApplyPointerRegistration();
  if (hook_ != nullptr) {
    UnhookWindowsHookEx(hook_);
    hook_ = nullptr;
  }
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
  if (window_ != nullptr) {
    DestroyWindow(window_);
    window_ = nullptr;
  }
  if (active_runtime_ == this) {
    active_runtime_ = nullptr;
  }
  pressed_keys_.Clear();
  hotkey_router_.Reset();
  toggle_chord_active_ = false;
  toggle_chord_contaminated_ = false;
  controller_.Reset();
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

LRESULT Win32InputRuntime::HandleWindowMessage(
    HWND window, UINT message, WPARAM w_param, LPARAM l_param) noexcept {
  switch (message) {
    case kPointerRegistrationMessage:
      ApplyPointerRegistration();
      return 0;
    case WM_INPUT: {
      const bool reset = IsPointerResetPacket(
          reinterpret_cast<HRAWINPUT>(l_param));
      const LRESULT result = DefWindowProcW(
          window, message, w_param, l_param);
      if (reset) {
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
          profile_.vietnamese_enabled = !profile_.vietnamese_enabled;
          controller_.ApplyProfile(profile_);
          RequestPointerRegistration(false);
          UpdateTray();
          static_cast<void>(QueueManagedCommand(
              profile_.vietnamese_enabled
                  ? RuntimeCommand::SetVietnameseEnabled
                  : RuntimeCommand::SetVietnameseDisabled));
          return 0;
        case kSettingsMenuCommand:
          OpenManagedSettings();
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

    ++processed_keyboard_events_;
    const bool key_down = IsKeyDownMessage(message);
    const auto virtual_key = static_cast<std::uint16_t>(native_event.vkCode);
    const bool was_pressed = pressed_keys_.Get(virtual_key);
    if (key_down && !was_pressed && virtual_key == VK_CAPITAL) {
      caps_lock_ = !caps_lock_;
    }
    pressed_keys_.Set(virtual_key, key_down);
    if (IsModifierKey(virtual_key)) {
      RefreshModifierState();
    }

    PhysicalKeyEvent event{};
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

    ProcessToggleGesture(event);
    const bool companion_state_required = key_down &&
        (event.virtual_key == profile_.hotkeys[4].virtual_key ||
         event.virtual_key == profile_.hotkeys[5].virtual_key);
    const auto hotkey_decision = hotkey_router_.Process(
        event,
        profile_,
        was_pressed,
        companion_state_required && IsCommandCompanionActive());
    if (hotkey_decision.command != RuntimeCommand::None &&
        !QueueManagedCommand(hotkey_decision.command) &&
        hotkey_decision.suppress && key_down) {
      hotkey_router_.CancelSuppression(event.virtual_key);
      return CallNextHookEx(nullptr, code, message, data);
    }
    if (hotkey_decision.suppress) {
      return 1;
    }

    const TypingContext context = CaptureTypingContext();
    const InputDecision decision = controller_.Process(event, context);
    RequestPointerRegistration(
        controller_.pointer_observation_required() &&
        !context.bypass_typing);
    if (!decision.suppress) {
      return CallNextHookEx(nullptr, code, message, data);
    }
    if (key_down && !Inject(decision)) {
      controller_.Reset();
      RequestPointerRegistration(false);
      return CallNextHookEx(nullptr, code, message, data);
    }
    return 1;
  } catch (...) {
    controller_.Reset();
    RequestPointerRegistration(false);
    return CallNextHookEx(nullptr, code, message, data);
  }
}

TypingContext Win32InputRuntime::CaptureTypingContext() noexcept {
  GUITHREADINFO info{};
  info.cbSize = sizeof(info);
  if (!GetGUIThreadInfo(0, &info) || info.hwndFocus == nullptr) {
    return {};
  }

  const HWND active = info.hwndActive != nullptr
                          ? info.hwndActive
                          : info.hwndFocus;
  if (active != cached_active_window_ || cached_process_id_ == 0) {
    DWORD process_id = 0;
    if (GetWindowThreadProcessId(active, &process_id) == 0) {
      cached_active_window_ = nullptr;
      cached_process_id_ = 0;
      return {};
    }
    cached_active_window_ = active;
    cached_process_id_ = process_id;
  }

  SetLastError(ERROR_SUCCESS);
  const LONG_PTR style = GetWindowLongPtrW(info.hwndFocus, GWL_STYLE);
  const bool style_failed = style == 0 && GetLastError() != ERROR_SUCCESS;
  const bool password =
      style_failed || (style & ES_PASSWORD) != 0;
  const bool fullscreen = IsFullscreenWindow(active);
  return TypingContext{
      cached_process_id_,
      reinterpret_cast<std::uintptr_t>(info.hwndFocus),
      password || fullscreen,
  };
}

bool Win32InputRuntime::Inject(const InputDecision& decision) noexcept {
  constexpr std::size_t kMaximumInputEvents =
      ((kMaxActiveKeys + 1) * 2) + (kMaximumInputInsertUnits * 2);
  std::array<INPUT, kMaximumInputEvents> inputs;
  const std::size_t count = BuildKeyboardInputSequence(decision, inputs);
  if (count == 0) {
    return decision.backspace_count == 0 && decision.insert_units == 0;
  }
  const UINT expected = static_cast<UINT>(count);
  return SendInput(expected, inputs.data(), sizeof(INPUT)) == expected;
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
      profile_.vietnamese_enabled = !profile_.vietnamese_enabled;
      controller_.ApplyProfile(profile_);
      RequestPointerRegistration(false);
      UpdateTray();
      static_cast<void>(QueueManagedCommand(
          profile_.vietnamese_enabled
              ? RuntimeCommand::SetVietnameseEnabled
              : RuntimeCommand::SetVietnameseDisabled));
    }
    toggle_chord_active_ = false;
    toggle_chord_contaminated_ = false;
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
  std::array<wchar_t, 32768> path{};
  FILETIME write_time{};
  if (!ResolveRuntimeInputProfilePath(path) ||
      !TryGetRuntimeInputProfileWriteTime(path.data(), write_time)) {
    return;
  }
  if (profile_write_time_known_ &&
      CompareFileTime(&profile_write_time_, &write_time) == 0) {
    return;
  }

  RuntimeInputProfile profile{};
  if (!TryReadRuntimeInputProfile(path.data(), profile)) {
    return;
  }

  profile_write_time_ = write_time;
  profile_write_time_known_ = true;
  profile_ = profile;
  controller_.ApplyProfile(profile_);
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
      menu, MF_STRING | (profile_.vietnamese_enabled ? MF_CHECKED : 0),
      kToggleMenuCommand, L"Bật tiếng Việt");
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

RuntimeInputProfile DefaultRuntimeInputProfile() noexcept {
  RuntimeInputProfile profile{};
  profile.vietnamese_enabled = true;
  profile.hotkeys = {
      RuntimeHotkeyBinding{
          RuntimeHotkeyGesture::ModifierGesture, 0x03, 0x00},
      RuntimeHotkeyBinding{
          RuntimeHotkeyGesture::Hold, 0x05, 0x20},
      RuntimeHotkeyBinding{
          RuntimeHotkeyGesture::Press, 0x05, 0x56},
      RuntimeHotkeyBinding{
          RuntimeHotkeyGesture::Press, 0x05, 0x54},
      RuntimeHotkeyBinding{
          RuntimeHotkeyGesture::Press, 0x05, 0x5A},
      RuntimeHotkeyBinding{
          RuntimeHotkeyGesture::Press, 0x00, 0x1B},
  };
  return profile;
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
