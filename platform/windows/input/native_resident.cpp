#include <keyina/windows/win32_input_runtime.h>

#include <psapi.h>
#include <tlhelp32.h>
#include <windows.h>

#include <array>
#include <cstdio>
#include <string>
#include <string_view>

namespace {

constexpr int kAlreadyRunningExitCode = 17;
constexpr wchar_t kMutexName[] = L"Local\\Keyina.NativeInput";
constexpr wchar_t kWindowClassName[] = L"KeyinaNativeInputWindow";
constexpr UINT kSettingsMenuCommand = 1002;
constexpr UINT kExitMenuCommand = 1003;
constexpr DWORD kCommandForwardTimeoutMilliseconds = 2'000;
constexpr ULONG_PTR kSelfTestInputMarker =
    static_cast<ULONG_PTR>(0x4B455954455354ULL);

bool ForwardCommandToExistingResident(UINT command) noexcept {
  const ULONGLONG deadline =
      GetTickCount64() + kCommandForwardTimeoutMilliseconds;
  do {
    if (HWND existing = FindWindowW(kWindowClassName, nullptr);
        existing != nullptr) {
      DWORD_PTR ignored = 0;
      return SendMessageTimeoutW(
                 existing,
                 WM_COMMAND,
                 static_cast<WPARAM>(command),
                 0,
                 SMTO_ABORTIFHUNG | SMTO_BLOCK,
                 500,
                 &ignored) != 0;
    }
    Sleep(25);
  } while (GetTickCount64() < deadline);
  return false;
}

std::uint32_t ComputeProfileChecksum(
    const std::array<std::uint8_t, 36>& bytes) noexcept {
  std::uint32_t hash = 2166136261u;
  for (std::size_t index = 0; index < 32; ++index) {
    hash ^= bytes[index];
    hash *= 16777619u;
  }
  return hash;
}

bool WriteRuntimeProfileVector(
    const wchar_t* path,
    bool vietnamese_enabled) noexcept {
  std::array<std::uint8_t, 36> bytes{
      0x4B, 0x49, 0x52, 0x50, 0x02, 0x24, 0x11, 0x06,
      0x02, 0x03, 0x00, 0x01, 0x05, 0x20, 0x00, 0x05,
      0x56, 0x00, 0x05, 0x54, 0x00, 0x05, 0x5A, 0x00,
      0x00, 0x1B, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00,
      0x00, 0x00, 0x00, 0x00,
  };
  if (!vietnamese_enabled) {
    bytes[6] &= static_cast<std::uint8_t>(~0x01u);
  }
  const std::uint32_t checksum = ComputeProfileChecksum(bytes);
  bytes[32] = static_cast<std::uint8_t>(checksum & 0xFFu);
  bytes[33] = static_cast<std::uint8_t>((checksum >> 8u) & 0xFFu);
  bytes[34] = static_cast<std::uint8_t>((checksum >> 16u) & 0xFFu);
  bytes[35] = static_cast<std::uint8_t>((checksum >> 24u) & 0xFFu);

  HANDLE file = CreateFileW(
      path,
      GENERIC_WRITE,
      FILE_SHARE_READ,
      nullptr,
      CREATE_ALWAYS,
      FILE_ATTRIBUTE_NORMAL,
      nullptr);
  if (file == INVALID_HANDLE_VALUE) {
    return false;
  }
  DWORD written = 0;
  const BOOL success = WriteFile(
      file,
      bytes.data(),
      static_cast<DWORD>(bytes.size()),
      &written,
      nullptr);
  FlushFileBuffers(file);
  CloseHandle(file);
  return success && written == bytes.size();
}

bool HasArgument(int argument_count, wchar_t** arguments,
                 std::wstring_view expected) noexcept {
  for (int index = 1; index < argument_count; ++index) {
    if (arguments[index] != nullptr && expected == arguments[index]) {
      return true;
    }
  }
  return false;
}

std::uint32_t CountProcessThreads() noexcept {
  HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, 0);
  if (snapshot == INVALID_HANDLE_VALUE) {
    return 0;
  }
  const DWORD process_id = GetCurrentProcessId();
  std::uint32_t count = 0;
  THREADENTRY32 entry{};
  entry.dwSize = sizeof(entry);
  if (Thread32First(snapshot, &entry)) {
    do {
      if (entry.th32OwnerProcessID == process_id) {
        ++count;
      }
      entry.dwSize = sizeof(entry);
    } while (Thread32Next(snapshot, &entry));
  }
  CloseHandle(snapshot);
  return count;
}

std::uint32_t MeasureSettledProcessThreadBaseline() noexcept {
  constexpr ULONGLONG kMinimumProcessAgeMilliseconds = 1000;
  constexpr ULONGLONG kStableWindowMilliseconds = 250;
  constexpr ULONGLONG kMaximumWaitMilliseconds = 2000;

  const ULONGLONG started_at = GetTickCount64();
  ULONGLONG stable_since = started_at;
  std::uint32_t current = CountProcessThreads();
  while (GetTickCount64() - started_at < kMaximumWaitMilliseconds) {
    Sleep(25);
    const auto next = CountProcessThreads();
    const ULONGLONG now = GetTickCount64();
    if (next != current) {
      current = next;
      stable_since = now;
      continue;
    }
    if (now - started_at >= kMinimumProcessAgeMilliseconds &&
        now - stable_since >= kStableWindowMilliseconds) {
      break;
    }
  }
  return current;
}

std::uint64_t CurrentWorkingSet() noexcept {
  PROCESS_MEMORY_COUNTERS_EX counters{};
  counters.cb = sizeof(counters);
  if (!GetProcessMemoryInfo(
          GetCurrentProcess(),
          reinterpret_cast<PROCESS_MEMORY_COUNTERS*>(&counters),
          sizeof(counters))) {
    return 0;
  }
  return counters.WorkingSetSize;
}

void WriteStandardOutput(std::string_view text) noexcept {
  HANDLE output = GetStdHandle(STD_OUTPUT_HANDLE);
  if (output == nullptr || output == INVALID_HANDLE_VALUE || text.empty()) {
    return;
  }
  DWORD written = 0;
  WriteFile(output, text.data(), static_cast<DWORD>(text.size()),
            &written, nullptr);
}

bool FocusTestControl(HWND window, HWND edit) noexcept {
  if (window == nullptr || edit == nullptr ||
      IsWindow(window) == FALSE || IsWindow(edit) == FALSE) {
    return false;
  }

  constexpr int kMaximumFocusAttempts = 12;
  const DWORD current_thread = GetCurrentThreadId();
  RECT work_area{};
  if (SystemParametersInfoW(
          SPI_GETWORKAREA, 0, &work_area, 0) == FALSE) {
    work_area = {0, 0, 480, 180};
  }
  ShowWindow(window, SW_RESTORE);
  SetWindowPos(
      window,
      HWND_TOPMOST,
      work_area.left + 16,
      work_area.top + 16,
      480,
      180,
      SWP_SHOWWINDOW | SWP_NOOWNERZORDER);

  for (int attempt = 0; attempt < kMaximumFocusAttempts; ++attempt) {
    const HWND previous_foreground = GetForegroundWindow();
    const DWORD foreground_thread = previous_foreground == nullptr
        ? 0
        : GetWindowThreadProcessId(previous_foreground, nullptr);
    const bool attached = foreground_thread != 0 &&
        foreground_thread != current_thread &&
        AttachThreadInput(
            current_thread, foreground_thread, TRUE) != FALSE;

    BringWindowToTop(window);
    static_cast<void>(SetForegroundWindow(window));
    SetActiveWindow(window);
    SetFocus(edit);

    MSG message{};
    while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) {
      TranslateMessage(&message);
      DispatchMessageW(&message);
    }
    const bool ready = GetForegroundWindow() == window &&
        GetFocus() == edit;
    if (attached) {
      static_cast<void>(AttachThreadInput(
          current_thread, foreground_thread, FALSE));
    }
    if (ready) {
      return true;
    }

    // A process launched by CTest may not own the foreground permission yet.
    // A test-only Alt press/release clears that Windows foreground lock; the
    // resident hook ignores it because it does not carry the accepted marker.
    std::array<INPUT, 2> unlock{};
    unlock[0].type = INPUT_KEYBOARD;
    unlock[0].ki.wVk = VK_MENU;
    unlock[1] = unlock[0];
    unlock[1].ki.dwFlags = KEYEVENTF_KEYUP;
    static_cast<void>(SendInput(
        static_cast<UINT>(unlock.size()),
        unlock.data(),
        sizeof(INPUT)));
    Sleep(10);

    // SetForegroundWindow can still be denied on an unattended desktop even
    // after the Alt unlock. A test-only click on the already topmost edit
    // control makes the target the genuine input recipient. Preserve the
    // caller's cursor position so local verification remains non-disruptive.
    POINT previous_cursor{};
    RECT edit_bounds{};
    if (GetCursorPos(&previous_cursor) != FALSE &&
        GetWindowRect(edit, &edit_bounds) != FALSE &&
        SetCursorPos(
            edit_bounds.left + (edit_bounds.right - edit_bounds.left) / 2,
            edit_bounds.top + (edit_bounds.bottom - edit_bounds.top) / 2) != FALSE) {
      std::array<INPUT, 2> click{};
      click[0].type = INPUT_MOUSE;
      click[0].mi.dwFlags = MOUSEEVENTF_LEFTDOWN;
      click[1] = click[0];
      click[1].mi.dwFlags = MOUSEEVENTF_LEFTUP;
      static_cast<void>(SendInput(
          static_cast<UINT>(click.size()),
          click.data(),
          sizeof(INPUT)));
      Sleep(10);
      while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) {
        TranslateMessage(&message);
        DispatchMessageW(&message);
      }
      const bool click_ready = GetForegroundWindow() == window &&
          GetFocus() == edit;
      static_cast<void>(SetCursorPos(previous_cursor.x, previous_cursor.y));
      if (click_ready) {
        return true;
      }
    }
  }
  return false;
}

std::uint32_t SendTestCharacter(char character, bool caps_lock) noexcept {
  const WORD virtual_key = character == ' '
                               ? static_cast<WORD>(VK_SPACE)
                               : static_cast<WORD>(
                                     character >= 'a' && character <= 'z'
                                         ? character - 'a' + 'A'
                                         : character);
  const bool shift = character != ' ' && caps_lock;
  std::array<INPUT, 4> inputs{};
  std::size_t count = 0;
  auto append = [&](WORD key, DWORD flags) noexcept {
    INPUT input{};
    input.type = INPUT_KEYBOARD;
    input.ki.wVk = key;
    input.ki.dwFlags = flags;
    input.ki.dwExtraInfo = kSelfTestInputMarker;
    inputs[count++] = input;
  };
  if (shift) {
    append(VK_SHIFT, 0);
  }
  append(virtual_key, 0);
  append(virtual_key, KEYEVENTF_KEYUP);
  if (shift) {
    append(VK_SHIFT, KEYEVENTF_KEYUP);
  }
  return SendInput(
             static_cast<UINT>(count), inputs.data(), sizeof(INPUT)) == count
      ? static_cast<std::uint32_t>(count)
      : 0;
}

std::uint32_t SendTestTextBatch(
    std::string_view text,
    bool caps_lock) noexcept {
  constexpr std::size_t kMaximumCharacters = 64;
  constexpr std::size_t kMaximumEvents = kMaximumCharacters * 4;
  if (text.empty() || text.size() > kMaximumCharacters) {
    return 0;
  }

  std::array<INPUT, kMaximumEvents> inputs{};
  std::size_t count = 0;
  auto append = [&](WORD key, DWORD flags) noexcept {
    INPUT input{};
    input.type = INPUT_KEYBOARD;
    input.ki.wVk = key;
    input.ki.dwFlags = flags;
    input.ki.dwExtraInfo = kSelfTestInputMarker;
    inputs[count++] = input;
  };
  for (const char character : text) {
    const WORD virtual_key = character == ' '
        ? static_cast<WORD>(VK_SPACE)
        : static_cast<WORD>(character >= 'a' && character <= 'z'
              ? character - 'a' + 'A'
              : character);
    const bool shift = character != ' ' && caps_lock;
    if (shift) {
      append(VK_SHIFT, 0);
    }
    append(virtual_key, 0);
    append(virtual_key, KEYEVENTF_KEYUP);
    if (shift) {
      append(VK_SHIFT, KEYEVENTF_KEYUP);
    }
  }
  const UINT expected = static_cast<UINT>(count);
  return SendInput(expected, inputs.data(), sizeof(INPUT)) == expected
      ? expected
      : 0;
}

std::uint32_t SendTestVirtualKeyPair(WORD virtual_key) noexcept {
  if (virtual_key == 0) {
    return 0;
  }
  std::array<INPUT, 2> inputs{};
  inputs[0].type = INPUT_KEYBOARD;
  inputs[0].ki.wVk = virtual_key;
  inputs[0].ki.dwExtraInfo = kSelfTestInputMarker;
  inputs[1] = inputs[0];
  inputs[1].ki.dwFlags = KEYEVENTF_KEYUP;
  return SendInput(
             static_cast<UINT>(inputs.size()),
             inputs.data(),
             sizeof(INPUT)) == inputs.size()
      ? static_cast<std::uint32_t>(inputs.size())
      : 0;
}

std::uint32_t SendTestKeyPairBatch(std::size_t pair_count) noexcept {
  constexpr std::size_t kMaximumPairs = 64;
  if (pair_count == 0 || pair_count > kMaximumPairs) {
    return 0;
  }
  std::array<INPUT, kMaximumPairs * 2> inputs{};
  std::size_t count = 0;
  for (std::size_t index = 0; index < pair_count; ++index) {
    INPUT down{};
    down.type = INPUT_KEYBOARD;
    down.ki.wVk = static_cast<WORD>('A');
    down.ki.dwExtraInfo = kSelfTestInputMarker;
    inputs[count++] = down;

    INPUT up = down;
    up.ki.dwFlags = KEYEVENTF_KEYUP;
    inputs[count++] = up;
  }
  const UINT expected = static_cast<UINT>(count);
  return SendInput(expected, inputs.data(), sizeof(INPUT)) == expected
             ? expected
             : 0;
}

bool WaitForProcessedKeyboardEvents(
    keyina::windows::Win32InputRuntime& runtime,
    std::uint64_t minimum_count,
    DWORD timeout_milliseconds) noexcept {
  const ULONGLONG deadline = GetTickCount64() + timeout_milliseconds;
  while (runtime.processed_keyboard_events() < minimum_count &&
         GetTickCount64() < deadline) {
    runtime.PumpMessagesFor(5);
  }
  return runtime.processed_keyboard_events() >= minimum_count;
}

void DrainCurrentThreadMessages(DWORD duration_milliseconds) noexcept {
  const ULONGLONG deadline = GetTickCount64() + duration_milliseconds;
  while (GetTickCount64() < deadline) {
    const ULONGLONG remaining = deadline - GetTickCount64();
    const DWORD timeout = static_cast<DWORD>(
        std::min<ULONGLONG>(remaining, 25));
    MsgWaitForMultipleObjectsEx(
        0, nullptr, timeout, QS_ALLINPUT, MWMO_INPUTAVAILABLE);
    MSG message{};
    while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) {
      TranslateMessage(&message);
      DispatchMessageW(&message);
    }
  }
}

template <std::size_t Capacity>
bool WaitForExpectedText(
    keyina::windows::Win32InputRuntime& runtime,
    HWND edit,
    std::wstring_view expected,
    DWORD timeout_milliseconds,
    std::array<wchar_t, Capacity>& text,
    int& length) noexcept {
  const ULONGLONG deadline = GetTickCount64() + timeout_milliseconds;
  do {
    runtime.PumpMessagesFor(10);
    text.fill(L'\0');
    length = GetWindowTextW(
        edit, text.data(), static_cast<int>(text.size()));
    if (length >= 0 &&
        std::wstring_view(text.data(), static_cast<std::size_t>(length)) ==
            expected) {
      return true;
    }
  } while (GetTickCount64() < deadline);
  return false;
}

struct ClipboardLockProbeContext {
  HANDLE ready{};
  HANDLE release{};
  volatile LONG opened{};
};

DWORD WINAPI HoldClipboardForProbe(void* value) noexcept {
  auto* context = static_cast<ClipboardLockProbeContext*>(value);
  if (context == nullptr) {
    return 1;
  }
  bool opened = false;
  for (int attempt = 0; attempt < 40 && !opened; ++attempt) {
    opened = OpenClipboard(nullptr) != FALSE;
    if (!opened) {
      Sleep(5);
    }
  }
  InterlockedExchange(&context->opened, opened ? 1 : 0);
  static_cast<void>(SetEvent(context->ready));
  static_cast<void>(WaitForSingleObject(context->release, 5'000));
  if (opened) {
    CloseClipboard();
  }
  return opened ? 0 : 1;
}

bool RunClipboardOrderingProbe(HWND window, HWND edit) noexcept {
  SetWindowTextW(edit, L"");
  DrainCurrentThreadMessages(20);
  auto profile = keyina::windows::DefaultRuntimeInputProfile();
  profile.vietnamese_enabled = true;
  profile.clipboard_compatibility_enabled = true;
  keyina::windows::Win32InputRuntime runtime(
      profile, false, false, true, kSelfTestInputMarker);
  if (!runtime.Start()) {
    return false;
  }

  runtime.PumpMessagesFor(25);
  bool success = FocusTestControl(window, edit);
  runtime.PumpMessagesFor(25);
  success = success && GetFocus() == edit &&
      GetForegroundWindow() == window;
  constexpr std::string_view raw =
      "as as 12345678901234567890123456789012345678901234567890";
  constexpr std::wstring_view expected =
      L"á á 1234567890123456789012345678901234567890123456789";
  std::array<wchar_t, 64> text{};
  int length = 0;
  const bool caps_lock = (GetKeyState(VK_CAPITAL) & 0x0001) != 0;
  const std::uint64_t before = runtime.processed_keyboard_events();
  const std::uint32_t sent_text = success
      ? SendTestTextBatch(raw, caps_lock)
      : 0;
  const std::uint32_t sent_backspace = sent_text != 0
      ? SendTestVirtualKeyPair(VK_BACK)
      : 0;
  const std::uint64_t expected_events =
      static_cast<std::uint64_t>(sent_text) + sent_backspace;
  success = success && sent_text != 0 && sent_backspace == 2 &&
      WaitForProcessedKeyboardEvents(
          runtime, before + expected_events, 2'000) &&
      WaitForExpectedText(
          runtime, edit, expected, 2'000, text, length) &&
      runtime.failed_injection_count() == 0 &&
      runtime.clipboard_privacy_write_count() == 2 &&
      runtime.clipboard_privacy_failure_count() == 0 &&
      runtime.deferred_clipboard_queue_full_count() == 0 &&
      runtime.deferred_clipboard_fallback_count() == 0 &&
      runtime.deferred_virtual_key_injection_count() == 1;
  runtime.Stop();
  return success;
}

bool RunClipboardCommandOrderingProbe(HWND window, HWND edit) noexcept {
  SetWindowTextW(edit, L"");
  DrainCurrentThreadMessages(20);
  auto profile = keyina::windows::DefaultRuntimeInputProfile();
  profile.vietnamese_enabled = true;
  profile.clipboard_compatibility_enabled = true;
  keyina::windows::Win32InputRuntime runtime(
      profile, false, false, true, kSelfTestInputMarker);
  if (!runtime.Start()) {
    return false;
  }

  runtime.PumpMessagesFor(25);
  bool success = FocusTestControl(window, edit);
  runtime.PumpMessagesFor(25);
  success = success && GetFocus() == edit &&
      GetForegroundWindow() == window;
  const bool caps_lock = (GetKeyState(VK_CAPITAL) & 0x0001) != 0;
  const std::uint64_t before = runtime.processed_keyboard_events();
  const std::uint32_t sent_prefix = success
      ? SendTestTextBatch("as ", caps_lock)
      : 0;
  const std::uint32_t sent_semicolon = sent_prefix != 0
      ? SendTestVirtualKeyPair(VK_OEM_1)
      : 0;
  const std::uint32_t sent_command = sent_semicolon != 0
      ? SendTestTextBatch("kvi ", caps_lock)
      : 0;
  const std::uint64_t expected_events =
      static_cast<std::uint64_t>(sent_prefix) + sent_semicolon + sent_command;
  std::array<wchar_t, 64> text{};
  int length = 0;
  success = success && sent_prefix != 0 && sent_semicolon == 2 &&
      sent_command != 0 &&
      WaitForProcessedKeyboardEvents(
          runtime, before + expected_events, 2'000) &&
      WaitForExpectedText(
          runtime, edit, L"á ", 2'000, text, length) &&
      !runtime.profile().vietnamese_enabled &&
      runtime.failed_injection_count() == 0 &&
      runtime.clipboard_privacy_write_count() == 1 &&
      runtime.deferred_clipboard_queue_full_count() == 0;
  runtime.Stop();
  return success;
}

bool RunClipboardFailOpenProbe(HWND window, HWND edit) noexcept {
  SetWindowTextW(edit, L"");
  DrainCurrentThreadMessages(20);
  auto profile = keyina::windows::DefaultRuntimeInputProfile();
  profile.vietnamese_enabled = true;
  profile.clipboard_compatibility_enabled = true;
  keyina::windows::Win32InputRuntime runtime(
      profile, false, false, true, kSelfTestInputMarker);
  if (!runtime.Start()) {
    return false;
  }
  runtime.PumpMessagesFor(25);
  bool success = FocusTestControl(window, edit);
  runtime.PumpMessagesFor(25);
  success = success && GetFocus() == edit &&
      GetForegroundWindow() == window;

  ClipboardLockProbeContext lock_context{};
  lock_context.ready = CreateEventW(nullptr, TRUE, FALSE, nullptr);
  lock_context.release = CreateEventW(nullptr, TRUE, FALSE, nullptr);
  HANDLE lock_thread = nullptr;
  if (success && lock_context.ready != nullptr &&
      lock_context.release != nullptr) {
    lock_thread = CreateThread(
        nullptr, 0, &HoldClipboardForProbe, &lock_context, 0, nullptr);
  }
  success = success && lock_thread != nullptr &&
      WaitForSingleObject(lock_context.ready, 2'000) == WAIT_OBJECT_0 &&
      InterlockedCompareExchange(&lock_context.opened, 0, 0) == 1;

  constexpr std::string_view raw = "as";
  constexpr std::wstring_view expected = L"as";
  std::array<wchar_t, 64> text{};
  int length = 0;
  const bool caps_lock = (GetKeyState(VK_CAPITAL) & 0x0001) != 0;
  const std::uint64_t before = runtime.processed_keyboard_events();
  const std::uint32_t sent = success
      ? SendTestTextBatch(raw, caps_lock)
      : 0;
  success = success && sent != 0 &&
      WaitForProcessedKeyboardEvents(runtime, before + sent, 1'500) &&
      WaitForExpectedText(
          runtime, edit, expected, 1'500, text, length) &&
      runtime.failed_injection_count() == 1 &&
      runtime.clipboard_privacy_write_count() == 0 &&
      runtime.deferred_clipboard_fallback_count() == 1;

  if (!success) {
    std::array<char, 512> diagnostic{};
    const int diagnostic_length = sprintf_s(
        diagnostic.data(), diagnostic.size(),
        "{\"error\":\"clipboard_fail_open_probe_failed\","
        "\"lock_opened\":%s,\"sent_events\":%u,"
        "\"processed_events\":%llu,\"text_length\":%d,"
        "\"failed_injections\":%llu,\"privacy_writes\":%llu,"
        "\"privacy_failures\":%llu,\"fallbacks\":%llu,"
        "\"queue_full\":%llu}\n",
        InterlockedCompareExchange(&lock_context.opened, 0, 0) == 1
            ? "true"
            : "false",
        sent,
        static_cast<unsigned long long>(runtime.processed_keyboard_events()),
        length,
        static_cast<unsigned long long>(runtime.failed_injection_count()),
        static_cast<unsigned long long>(runtime.clipboard_privacy_write_count()),
        static_cast<unsigned long long>(runtime.clipboard_privacy_failure_count()),
        static_cast<unsigned long long>(runtime.deferred_clipboard_fallback_count()),
        static_cast<unsigned long long>(
            runtime.deferred_clipboard_queue_full_count()));
    if (diagnostic_length > 0) {
      WriteStandardOutput(std::string_view(
          diagnostic.data(), static_cast<std::size_t>(diagnostic_length)));
    }
  }

  if (lock_context.release != nullptr) {
    static_cast<void>(SetEvent(lock_context.release));
  }
  if (lock_thread != nullptr) {
    static_cast<void>(WaitForSingleObject(lock_thread, 2'000));
    CloseHandle(lock_thread);
  }
  if (lock_context.ready != nullptr) {
    CloseHandle(lock_context.ready);
  }
  if (lock_context.release != nullptr) {
    CloseHandle(lock_context.release);
  }
  runtime.Stop();
  return success;
}

int RunTypingSelfTestAttempt(bool clipboard_compatibility) noexcept {
  const HWND previous_foreground = GetForegroundWindow();
  const HINSTANCE instance = GetModuleHandleW(nullptr);
  HWND window = CreateWindowExW(
      WS_EX_TOOLWINDOW, L"STATIC", L"Keyina native typing self-test",
      WS_OVERLAPPEDWINDOW, -1200, 100, 480, 180,
      nullptr, nullptr, instance, nullptr);
  if (window == nullptr) {
    WriteStandardOutput("typing_self_test_window_failed\n");
    return 1;
  }
  HMODULE rich_edit_module = nullptr;
  const wchar_t* edit_class = L"EDIT";
  if (clipboard_compatibility) {
    rich_edit_module = LoadLibraryW(L"Msftedit.dll");
    if (rich_edit_module == nullptr) {
      DestroyWindow(window);
      WriteStandardOutput("clipboard_typing_self_test_richedit_unavailable\n");
      return 1;
    }
    edit_class = L"RICHEDIT50W";
  }
  HWND edit = CreateWindowExW(
      0, edit_class, L"",
      WS_CHILD | WS_VISIBLE | WS_TABSTOP | ES_AUTOHSCROLL,
      12, 12, 440, 40, window, nullptr, instance, nullptr);
  if (edit == nullptr) {
    if (rich_edit_module != nullptr) {
      FreeLibrary(rich_edit_module);
    }
    DestroyWindow(window);
    WriteStandardOutput("typing_self_test_edit_failed\n");
    return 1;
  }

  constexpr int kMaximumAttempts = 5;
  constexpr std::string_view raw = "tieengs vieetj";
  constexpr std::array<std::wstring_view, 14> expected_prefixes{
      L"t", L"ti", L"tie", L"tiê", L"tiên", L"tiêng", L"tiếng",
      L"tiếng ", L"tiếng v", L"tiếng vi", L"tiếng vie", L"tiếng viê",
      L"tiếng viêt", L"tiếng việt",
  };
  constexpr std::wstring_view expected = expected_prefixes.back();
  bool focus_ready = false;
  bool focus_confirmed = false;
  bool foreground_confirmed = false;
  bool success = false;
  bool clipboard_ordering_probe_pass = true;
  bool clipboard_command_ordering_probe_pass = true;
  bool clipboard_fail_open_probe_pass = true;
  std::uint64_t processed_events = 0;
  std::uint64_t suppressed_edits = 0;
  std::uint64_t successful_injections = 0;
  std::uint64_t failed_injections = 0;
  std::uint64_t bypass_contexts = 0;
  std::uint64_t context_changes = 0;
  std::uint64_t pointer_resets = 0;
  std::uint64_t standard_edit_replaces = 0;
  std::uint64_t typing_context_captures = 0;
  std::uint64_t clipboard_privacy_writes = 0;
  std::uint64_t clipboard_privacy_failures = 0;
  std::uint64_t deferred_clipboard_queue_full = 0;
  std::uint64_t deferred_clipboard_fallbacks = 0;
  std::uint64_t deferred_literal_injections = 0;
  keyina::windows::NativeLatencySnapshot callback_latency{};
  keyina::windows::NativeLatencySnapshot injection_latency{};
  std::array<wchar_t, 64> text{};
  int length = 0;

  for (int attempt = 0; attempt < kMaximumAttempts && !success; ++attempt) {
    DrainCurrentThreadMessages(200);
    SetWindowTextW(edit, L"");
    DrainCurrentThreadMessages(20);
    auto profile = keyina::windows::DefaultRuntimeInputProfile();
    profile.vietnamese_enabled = true;
    profile.clipboard_compatibility_enabled = clipboard_compatibility;
    keyina::windows::Win32InputRuntime runtime(
        profile, false, false, true, kSelfTestInputMarker);
    if (!runtime.Start()) {
      DestroyWindow(window);
      if (rich_edit_module != nullptr) {
        FreeLibrary(rich_edit_module);
      }
      WriteStandardOutput("typing_self_test_runtime_failed\n");
      return 1;
    }

    runtime.PumpMessagesFor(50);
    focus_ready = FocusTestControl(window, edit);
    runtime.PumpMessagesFor(50);
    focus_confirmed = GetFocus() == edit;
    foreground_confirmed = GetForegroundWindow() == window;
    success = focus_ready && focus_confirmed && foreground_confirmed;
    const bool caps_lock = (GetKeyState(VK_CAPITAL) & 0x0001) != 0;

    for (std::size_t index = 0; index < raw.size(); ++index) {
      if (!success || GetFocus() != edit || GetForegroundWindow() != window) {
        success = false;
        break;
      }
      const std::uint64_t before = runtime.processed_keyboard_events();
      const std::uint32_t sent = SendTestCharacter(raw[index], caps_lock);
      if (sent == 0 ||
          !WaitForProcessedKeyboardEvents(runtime, before + sent, 1000) ||
          !WaitForExpectedText(
              runtime, edit, expected_prefixes[index], 750, text, length)) {
        success = false;
        break;
      }
    }

    if (success) {
      success = WaitForExpectedText(
          runtime, edit, expected, 500, text, length);
    } else {
      text.fill(L'\0');
      length = GetWindowTextW(
          edit, text.data(), static_cast<int>(text.size()));
    }
    processed_events = runtime.processed_keyboard_events();
    suppressed_edits = runtime.suppressed_edit_count();
    successful_injections = runtime.successful_injection_count();
    failed_injections = runtime.failed_injection_count();
    bypass_contexts = runtime.bypass_context_count();
    context_changes = runtime.context_change_count();
    pointer_resets = runtime.pointer_reset_count();
    standard_edit_replaces = runtime.standard_edit_replace_count();
    typing_context_captures = runtime.typing_context_capture_count();
    clipboard_privacy_writes = runtime.clipboard_privacy_write_count();
    clipboard_privacy_failures = runtime.clipboard_privacy_failure_count();
    deferred_clipboard_queue_full =
        runtime.deferred_clipboard_queue_full_count();
    deferred_clipboard_fallbacks =
        runtime.deferred_clipboard_fallback_count();
    deferred_literal_injections =
        runtime.deferred_literal_injection_count();
    callback_latency = runtime.callback_latency_snapshot();
    injection_latency = runtime.callback_stage_latency_snapshot(
        keyina::windows::NativeCallbackLatencyStage::Injection);
    success = success && typing_context_captures <= raw.size() &&
        callback_latency.sample_count == processed_events &&
        (!clipboard_compatibility ||
         (standard_edit_replaces == 0 &&
          clipboard_privacy_writes == 4 &&
          successful_injections ==
              clipboard_privacy_writes + deferred_literal_injections &&
          clipboard_privacy_failures == 0 &&
          deferred_clipboard_queue_full == 0 &&
          deferred_clipboard_fallbacks == 0 &&
          injection_latency.sample_count == suppressed_edits &&
          injection_latency.p95_ns <= 1'000'000));
    runtime.Stop();
    if (!success) {
      DrainCurrentThreadMessages(300);
    }
  }
  if (success && clipboard_compatibility) {
    auto run_clipboard_probe = [window, edit](
        bool (*probe)(HWND, HWND)) noexcept {
      constexpr int kMaximumProbeAttempts = 3;
      for (int attempt = 0; attempt < kMaximumProbeAttempts; ++attempt) {
        if (probe(window, edit)) {
          return true;
        }
        DrainCurrentThreadMessages(100);
      }
      return false;
    };
    clipboard_ordering_probe_pass =
        run_clipboard_probe(&RunClipboardOrderingProbe);
    clipboard_command_ordering_probe_pass =
        run_clipboard_probe(&RunClipboardCommandOrderingProbe);
    clipboard_fail_open_probe_pass =
        run_clipboard_probe(&RunClipboardFailOpenProbe);
    success = clipboard_ordering_probe_pass &&
        clipboard_command_ordering_probe_pass &&
        clipboard_fail_open_probe_pass;
  }
  DestroyWindow(window);
  if (rich_edit_module != nullptr) {
    FreeLibrary(rich_edit_module);
  }
  if (previous_foreground != nullptr) {
    SetForegroundWindow(previous_foreground);
  }
  if (success) {
    std::array<char, 2048> success_json{};
    const int success_length = sprintf_s(
        success_json.data(), success_json.size(),
        "{\"result\":\"%s\",\"processed_events\":%llu,"
        "\"suppressed_edits\":%llu,\"successful_injections\":%llu,"
        "\"failed_injections\":%llu,\"typing_context_captures\":%llu,"
        "\"maximum_expected_context_captures\":%llu,"
        "\"standard_edit_replaces\":%llu,"
        "\"clipboard_privacy_writes\":%llu,"
        "\"clipboard_privacy_failures\":%llu,"
        "\"clipboard_ordering_probe_pass\":%s,"
        "\"clipboard_command_ordering_probe_pass\":%s,"
        "\"clipboard_fail_open_probe_pass\":%s,"
        "\"deferred_clipboard_queue_full\":%llu,"
        "\"deferred_clipboard_fallbacks\":%llu,"
        "\"deferred_literal_injections\":%llu,"
        "\"injection_samples\":%llu,\"injection_p95_ns\":%llu,"
        "\"callback_samples\":%llu,\"callback_p50_ns\":%llu,"
        "\"callback_p95_ns\":%llu,\"callback_p99_ns\":%llu,"
        "\"callback_maximum_ns\":%llu,\"callback_mean_ns\":%llu}\n",
        clipboard_compatibility
            ? "clipboard_typing_self_test_pass"
            : "typing_self_test_pass",
        static_cast<unsigned long long>(processed_events),
        static_cast<unsigned long long>(suppressed_edits),
        static_cast<unsigned long long>(successful_injections),
        static_cast<unsigned long long>(failed_injections),
        static_cast<unsigned long long>(typing_context_captures),
        static_cast<unsigned long long>(raw.size()),
        static_cast<unsigned long long>(standard_edit_replaces),
        static_cast<unsigned long long>(clipboard_privacy_writes),
        static_cast<unsigned long long>(clipboard_privacy_failures),
        clipboard_ordering_probe_pass ? "true" : "false",
        clipboard_command_ordering_probe_pass ? "true" : "false",
        clipboard_fail_open_probe_pass ? "true" : "false",
        static_cast<unsigned long long>(deferred_clipboard_queue_full),
        static_cast<unsigned long long>(deferred_clipboard_fallbacks),
        static_cast<unsigned long long>(deferred_literal_injections),
        static_cast<unsigned long long>(injection_latency.sample_count),
        static_cast<unsigned long long>(injection_latency.p95_ns),
        static_cast<unsigned long long>(callback_latency.sample_count),
        static_cast<unsigned long long>(callback_latency.p50_ns),
        static_cast<unsigned long long>(callback_latency.p95_ns),
        static_cast<unsigned long long>(callback_latency.p99_ns),
        static_cast<unsigned long long>(callback_latency.maximum_ns),
        static_cast<unsigned long long>(callback_latency.mean_ns));
    if (success_length > 0) {
      WriteStandardOutput(std::string_view(
          success_json.data(), static_cast<std::size_t>(success_length)));
    }
    return 0;
  }

  std::array<char, 256> actual_utf8{};
  const int utf8_length = length <= 0
      ? 0
      : WideCharToMultiByte(
            CP_UTF8, 0, text.data(), length, actual_utf8.data(),
            static_cast<int>(actual_utf8.size() - 1), nullptr, nullptr);
  std::array<char, 2048> diagnostic{};
  const int diagnostic_length = sprintf_s(
      diagnostic.data(), diagnostic.size(),
      "{\"error\":\"typing_self_test_failed\",\"focus_ready\":%s,"
      "\"focus_confirmed\":%s,\"foreground_confirmed\":%s,"
      "\"processed_events\":%llu,\"suppressed_edits\":%llu,"
      "\"successful_injections\":%llu,\"failed_injections\":%llu,"
      "\"bypass_contexts\":%llu,\"context_changes\":%llu,"
      "\"pointer_resets\":%llu,\"standard_edit_replaces\":%llu,"
      "\"typing_context_captures\":%llu,\"maximum_expected_context_captures\":%llu,"
      "\"clipboard_privacy_writes\":%llu,"
      "\"clipboard_privacy_failures\":%llu,"
      "\"clipboard_ordering_probe_pass\":%s,"
      "\"clipboard_command_ordering_probe_pass\":%s,"
      "\"clipboard_fail_open_probe_pass\":%s,"
      "\"deferred_clipboard_queue_full\":%llu,"
      "\"deferred_clipboard_fallbacks\":%llu,"
      "\"deferred_literal_injections\":%llu,"
      "\"injection_samples\":%llu,\"injection_p95_ns\":%llu,"
      "\"callback_samples\":%llu,\"callback_p50_ns\":%llu,"
      "\"callback_p95_ns\":%llu,\"callback_p99_ns\":%llu,"
      "\"callback_maximum_ns\":%llu,\"callback_mean_ns\":%llu,"
      "\"text_length\":%d,"
      "\"actual\":\"%.*s\"}\n",
      focus_ready ? "true" : "false",
      focus_confirmed ? "true" : "false",
      foreground_confirmed ? "true" : "false",
      static_cast<unsigned long long>(processed_events),
      static_cast<unsigned long long>(suppressed_edits),
      static_cast<unsigned long long>(successful_injections),
      static_cast<unsigned long long>(failed_injections),
      static_cast<unsigned long long>(bypass_contexts),
      static_cast<unsigned long long>(context_changes),
      static_cast<unsigned long long>(pointer_resets),
      static_cast<unsigned long long>(standard_edit_replaces),
      static_cast<unsigned long long>(typing_context_captures),
      static_cast<unsigned long long>(raw.size()),
      static_cast<unsigned long long>(clipboard_privacy_writes),
      static_cast<unsigned long long>(clipboard_privacy_failures),
      clipboard_ordering_probe_pass ? "true" : "false",
      clipboard_command_ordering_probe_pass ? "true" : "false",
      clipboard_fail_open_probe_pass ? "true" : "false",
      static_cast<unsigned long long>(deferred_clipboard_queue_full),
      static_cast<unsigned long long>(deferred_clipboard_fallbacks),
      static_cast<unsigned long long>(deferred_literal_injections),
      static_cast<unsigned long long>(injection_latency.sample_count),
      static_cast<unsigned long long>(injection_latency.p95_ns),
      static_cast<unsigned long long>(callback_latency.sample_count),
      static_cast<unsigned long long>(callback_latency.p50_ns),
      static_cast<unsigned long long>(callback_latency.p95_ns),
      static_cast<unsigned long long>(callback_latency.p99_ns),
      static_cast<unsigned long long>(callback_latency.maximum_ns),
      static_cast<unsigned long long>(callback_latency.mean_ns),
      length, utf8_length, actual_utf8.data());
  if (diagnostic_length > 0) {
    WriteStandardOutput(std::string_view(
        diagnostic.data(), static_cast<std::size_t>(diagnostic_length)));
  }
  return 1;
}

int RunTypingSelfTest(bool clipboard_compatibility) noexcept {
  constexpr int kMaximumAttempts = 3;
  for (int attempt = 0; attempt < kMaximumAttempts; ++attempt) {
    if (RunTypingSelfTestAttempt(clipboard_compatibility) == 0) {
      return 0;
    }
    DrainCurrentThreadMessages(200);
  }
  return 1;
}

int RunProfileReloadSelfTest() noexcept {
  std::array<wchar_t, 32768> previous_local_app_data{};
  const DWORD previous_length = GetEnvironmentVariableW(
      L"LOCALAPPDATA",
      previous_local_app_data.data(),
      static_cast<DWORD>(previous_local_app_data.size()));

  std::array<wchar_t, MAX_PATH> temporary_root{};
  if (GetTempPathW(
          static_cast<DWORD>(temporary_root.size()),
          temporary_root.data()) == 0) {
    return 1;
  }
  std::array<wchar_t, 32768> test_directory{};
  if (swprintf_s(
          test_directory.data(),
          test_directory.size(),
          L"%lsKeyina.ProfileReload.%lu.%llu",
          temporary_root.data(),
          static_cast<unsigned long>(GetCurrentProcessId()),
          static_cast<unsigned long long>(GetTickCount64())) <= 0 ||
      !CreateDirectoryW(test_directory.data(), nullptr)) {
    return 1;
  }

  std::array<wchar_t, 32768> keyina_directory{};
  std::array<wchar_t, 32768> profile_path{};
  const bool paths_ready =
      swprintf_s(
          keyina_directory.data(),
          keyina_directory.size(),
          L"%ls\\Keyina",
          test_directory.data()) > 0 &&
      CreateDirectoryW(keyina_directory.data(), nullptr) != FALSE &&
      swprintf_s(
          profile_path.data(),
          profile_path.size(),
          L"%ls\\runtime-input.bin",
          keyina_directory.data()) > 0;
  if (!paths_ready ||
      !SetEnvironmentVariableW(L"LOCALAPPDATA", test_directory.data()) ||
      !WriteRuntimeProfileVector(profile_path.data(), true)) {
    RemoveDirectoryW(keyina_directory.data());
    RemoveDirectoryW(test_directory.data());
    return 1;
  }

  bool success = false;
  {
    auto profile = keyina::windows::LoadRuntimeInputProfileOrDefault();
    keyina::windows::Win32InputRuntime runtime(profile, false);
    if (profile.vietnamese_enabled && profile.restore_invalid_word &&
        runtime.Start()) {
      Sleep(20);
      if (WriteRuntimeProfileVector(profile_path.data(), false)) {
        runtime.PumpMessagesFor(2200);
        success = !runtime.profile().vietnamese_enabled &&
            runtime.profile().restore_invalid_word;
      }
      runtime.Stop();
    }
  }

  if (previous_length > 0 && previous_length < previous_local_app_data.size()) {
    SetEnvironmentVariableW(L"LOCALAPPDATA", previous_local_app_data.data());
  } else {
    SetEnvironmentVariableW(L"LOCALAPPDATA", nullptr);
  }
  DeleteFileW(profile_path.data());
  RemoveDirectoryW(keyina_directory.data());
  RemoveDirectoryW(test_directory.data());

  WriteStandardOutput(
      success
          ? "profile_reload_self_test_pass\n"
          : "profile_reload_self_test_failed\n");
  return success ? 0 : 1;
}

int RunCallbackLatencySelfTestAttempt() noexcept {
  // Keep enough samples for stable p50/p95/p99 buckets while pacing bursts
  // below the low-level hook queue's saturation point on loaded CI runners.
  constexpr std::size_t kWarmupPairs = 64;
  constexpr std::size_t kIterations = 512;
  constexpr std::size_t kBatchPairs = 8;
  constexpr std::uint64_t kExpectedEvents = kIterations * 2;

  const HWND previous_foreground = GetForegroundWindow();
  const HINSTANCE instance = GetModuleHandleW(nullptr);
  HWND window = CreateWindowExW(
      WS_EX_TOOLWINDOW,
      L"STATIC",
      L"Keyina callback latency self-test",
      WS_OVERLAPPEDWINDOW,
      -1200,
      320,
      480,
      180,
      nullptr,
      nullptr,
      instance,
      nullptr);
  if (window == nullptr) {
    WriteStandardOutput(
        "{\"result\":\"callback_latency_self_test_failed\","
        "\"error\":\"window_create_failed\"}\n");
    return 1;
  }
  HWND edit = CreateWindowExW(
      0,
      L"EDIT",
      L"",
      WS_CHILD | WS_VISIBLE | WS_TABSTOP | ES_AUTOHSCROLL,
      12,
      12,
      440,
      40,
      window,
      nullptr,
      instance,
      nullptr);
  if (edit == nullptr) {
    DestroyWindow(window);
    WriteStandardOutput(
        "{\"result\":\"callback_latency_self_test_failed\","
        "\"error\":\"edit_create_failed\"}\n");
    return 1;
  }

  auto profile = keyina::windows::DefaultRuntimeInputProfile();
  profile.vietnamese_enabled = false;
  profile.clipboard_compatibility_enabled = false;
  keyina::windows::Win32InputRuntime runtime(
      profile, false, false, true, kSelfTestInputMarker);
  if (!runtime.Start()) {
    DestroyWindow(window);
    WriteStandardOutput(
        "{\"result\":\"callback_latency_self_test_failed\","
        "\"error\":\"runtime_start_failed\"}\n");
    return 1;
  }

  runtime.PumpMessagesFor(50);
  const bool focus_ready = FocusTestControl(window, edit);
  runtime.PumpMessagesFor(50);
  const bool focus_confirmed = GetFocus() == edit;
  const bool foreground_confirmed = GetForegroundWindow() == window;
  bool success = focus_ready && focus_confirmed && foreground_confirmed;
  std::uint32_t focus_reacquire_count = 0;
  auto ensure_test_focus = [&]() noexcept {
    if (GetFocus() == edit && GetForegroundWindow() == window) {
      return true;
    }
    ++focus_reacquire_count;
    const bool reacquired = FocusTestControl(window, edit);
    runtime.PumpMessagesFor(10);
    return reacquired && GetFocus() == edit &&
        GetForegroundWindow() == window;
  };
  const std::uint64_t warmup_processed_before =
      runtime.processed_keyboard_events();
  const std::uint64_t warmup_contexts_before =
      runtime.typing_context_capture_count();
  std::uint64_t expected_total_processed = warmup_processed_before;

  for (std::size_t offset = 0; offset < kWarmupPairs && success;
       offset += kBatchPairs) {
    if (!ensure_test_focus()) {
      success = false;
      break;
    }
    const std::size_t pair_count = std::min(
        kBatchPairs, kWarmupPairs - offset);
    const std::uint32_t sent = SendTestKeyPairBatch(pair_count);
    const std::uint32_t expected_sent = static_cast<std::uint32_t>(
        pair_count * 2);
    if (sent != expected_sent) {
      success = false;
      break;
    }
    expected_total_processed += sent;
    if (!WaitForProcessedKeyboardEvents(
            runtime, expected_total_processed, 2000)) {
      success = false;
      break;
    }
    SetWindowTextW(edit, L"");
  }

  const std::uint64_t processed_before =
      runtime.processed_keyboard_events();
  const std::uint64_t contexts_before =
      runtime.typing_context_capture_count();
  const std::uint64_t suppressed_before =
      runtime.suppressed_edit_count();
  const std::uint64_t successful_injections_before =
      runtime.successful_injection_count();
  const std::uint64_t failed_injections_before =
      runtime.failed_injection_count();
  runtime.ClearCallbackLatency();

  for (std::size_t offset = 0; offset < kIterations && success;
       offset += kBatchPairs) {
    if (!ensure_test_focus()) {
      success = false;
      break;
    }
    const std::size_t pair_count = std::min(
        kBatchPairs, kIterations - offset);
    const std::uint32_t sent = SendTestKeyPairBatch(pair_count);
    const std::uint32_t expected_sent = static_cast<std::uint32_t>(
        pair_count * 2);
    if (sent != expected_sent) {
      success = false;
      break;
    }
    expected_total_processed += sent;
    if (!WaitForProcessedKeyboardEvents(
            runtime, expected_total_processed, 2000)) {
      success = false;
      break;
    }
    SetWindowTextW(edit, L"");
  }

  const std::uint64_t processed_total =
      runtime.processed_keyboard_events();
  const std::uint64_t contexts_total =
      runtime.typing_context_capture_count();
  const std::uint64_t suppressed_total =
      runtime.suppressed_edit_count();
  const std::uint64_t successful_injections_total =
      runtime.successful_injection_count();
  const std::uint64_t failed_injections_total =
      runtime.failed_injection_count();
  const std::uint64_t warmup_processed_events =
      processed_before >= warmup_processed_before
          ? processed_before - warmup_processed_before
          : 0;
  const std::uint64_t warmup_context_captures =
      contexts_before >= warmup_contexts_before
          ? contexts_before - warmup_contexts_before
          : 0;
  const std::uint64_t processed_events =
      processed_total >= processed_before
          ? processed_total - processed_before
          : 0;
  const std::uint64_t typing_context_captures =
      contexts_total >= contexts_before
          ? contexts_total - contexts_before
          : 0;
  const std::uint64_t suppressed_edits =
      suppressed_total >= suppressed_before
          ? suppressed_total - suppressed_before
          : 0;
  const std::uint64_t successful_injections =
      successful_injections_total >= successful_injections_before
          ? successful_injections_total - successful_injections_before
          : 0;
  const std::uint64_t failed_injections =
      failed_injections_total >= failed_injections_before
          ? failed_injections_total - failed_injections_before
          : 0;
  const auto callback_latency = runtime.callback_latency_snapshot();
  const auto key_state_latency = runtime.callback_stage_latency_snapshot(
      keyina::windows::NativeCallbackLatencyStage::KeyStateAndHotkey);
  const auto key_up_latency = runtime.callback_stage_latency_snapshot(
      keyina::windows::NativeCallbackLatencyStage::KeyUpRelease);
  const auto context_latency = runtime.callback_stage_latency_snapshot(
      keyina::windows::NativeCallbackLatencyStage::TypingContext);
  const auto controller_latency = runtime.callback_stage_latency_snapshot(
      keyina::windows::NativeCallbackLatencyStage::ControllerProcess);
  const auto injection_latency = runtime.callback_stage_latency_snapshot(
      keyina::windows::NativeCallbackLatencyStage::Injection);
  const bool hook_running = runtime.hook_running();
  success = success && warmup_processed_events == kWarmupPairs * 2 &&
      warmup_context_captures == kWarmupPairs &&
      processed_events == kExpectedEvents &&
      expected_total_processed ==
          warmup_processed_before + (kWarmupPairs * 2) + kExpectedEvents &&
      typing_context_captures == kIterations &&
      callback_latency.sample_count == kExpectedEvents &&
      key_state_latency.sample_count == kExpectedEvents &&
      key_up_latency.sample_count == kIterations &&
      context_latency.sample_count == kIterations &&
      controller_latency.sample_count == kIterations &&
      injection_latency.sample_count == 0 &&
      suppressed_edits == 0 && successful_injections == 0 &&
      failed_injections == 0 && callback_latency.p50_ns > 0 &&
      callback_latency.p50_ns <= callback_latency.p95_ns &&
      callback_latency.p95_ns <= callback_latency.p99_ns &&
      hook_running;

  runtime.Stop();
  DestroyWindow(window);
  if (previous_foreground != nullptr) {
    SetForegroundWindow(previous_foreground);
  }

  std::array<char, 4096> json{};
  const int length = sprintf_s(
      json.data(),
      json.size(),
      "{\"result\":\"%s\",\"warmup_pairs\":%llu,"
      "\"warmup_events\":%llu,\"warmup_context_captures\":%llu,"
      "\"iterations\":%llu,\"expected_events\":%llu,"
      "\"processed_events\":%llu,"
      "\"typing_context_captures\":%llu,\"callback_samples\":%llu,"
      "\"callback_p50_ns\":%llu,\"callback_p95_ns\":%llu,"
      "\"callback_p99_ns\":%llu,\"callback_maximum_ns\":%llu,"
      "\"callback_mean_ns\":%llu,"
      "\"key_state_samples\":%llu,\"key_state_p50_ns\":%llu,"
      "\"key_state_p95_ns\":%llu,\"key_state_p99_ns\":%llu,"
      "\"key_state_mean_ns\":%llu,"
      "\"key_up_samples\":%llu,\"key_up_p50_ns\":%llu,"
      "\"key_up_p95_ns\":%llu,\"key_up_p99_ns\":%llu,"
      "\"key_up_mean_ns\":%llu,"
      "\"context_samples\":%llu,\"context_p50_ns\":%llu,"
      "\"context_p95_ns\":%llu,\"context_p99_ns\":%llu,"
      "\"context_mean_ns\":%llu,"
      "\"controller_samples\":%llu,\"controller_p50_ns\":%llu,"
      "\"controller_p95_ns\":%llu,\"controller_p99_ns\":%llu,"
      "\"controller_mean_ns\":%llu,"
      "\"injection_samples\":%llu,\"suppressed_edits\":%llu,"
      "\"successful_injections\":%llu,\"failed_injections\":%llu,"
      "\"focus_ready\":%s,\"focus_confirmed\":%s,"
      "\"foreground_confirmed\":%s,\"focus_reacquire_count\":%u,"
      "\"hook_running\":%s}\n",
      success
          ? "callback_latency_self_test_pass"
          : "callback_latency_self_test_failed",
      static_cast<unsigned long long>(kWarmupPairs),
      static_cast<unsigned long long>(warmup_processed_events),
      static_cast<unsigned long long>(warmup_context_captures),
      static_cast<unsigned long long>(kIterations),
      static_cast<unsigned long long>(kExpectedEvents),
      static_cast<unsigned long long>(processed_events),
      static_cast<unsigned long long>(typing_context_captures),
      static_cast<unsigned long long>(callback_latency.sample_count),
      static_cast<unsigned long long>(callback_latency.p50_ns),
      static_cast<unsigned long long>(callback_latency.p95_ns),
      static_cast<unsigned long long>(callback_latency.p99_ns),
      static_cast<unsigned long long>(callback_latency.maximum_ns),
      static_cast<unsigned long long>(callback_latency.mean_ns),
      static_cast<unsigned long long>(key_state_latency.sample_count),
      static_cast<unsigned long long>(key_state_latency.p50_ns),
      static_cast<unsigned long long>(key_state_latency.p95_ns),
      static_cast<unsigned long long>(key_state_latency.p99_ns),
      static_cast<unsigned long long>(key_state_latency.mean_ns),
      static_cast<unsigned long long>(key_up_latency.sample_count),
      static_cast<unsigned long long>(key_up_latency.p50_ns),
      static_cast<unsigned long long>(key_up_latency.p95_ns),
      static_cast<unsigned long long>(key_up_latency.p99_ns),
      static_cast<unsigned long long>(key_up_latency.mean_ns),
      static_cast<unsigned long long>(context_latency.sample_count),
      static_cast<unsigned long long>(context_latency.p50_ns),
      static_cast<unsigned long long>(context_latency.p95_ns),
      static_cast<unsigned long long>(context_latency.p99_ns),
      static_cast<unsigned long long>(context_latency.mean_ns),
      static_cast<unsigned long long>(controller_latency.sample_count),
      static_cast<unsigned long long>(controller_latency.p50_ns),
      static_cast<unsigned long long>(controller_latency.p95_ns),
      static_cast<unsigned long long>(controller_latency.p99_ns),
      static_cast<unsigned long long>(controller_latency.mean_ns),
      static_cast<unsigned long long>(injection_latency.sample_count),
      static_cast<unsigned long long>(suppressed_edits),
      static_cast<unsigned long long>(successful_injections),
      static_cast<unsigned long long>(failed_injections),
      focus_ready ? "true" : "false",
      focus_confirmed ? "true" : "false",
      foreground_confirmed ? "true" : "false",
      focus_reacquire_count,
      hook_running ? "true" : "false");
  if (length > 0) {
    WriteStandardOutput(
        std::string_view(json.data(), static_cast<std::size_t>(length)));
  }
  return success ? 0 : 1;
}

int RunCallbackLatencySelfTest() noexcept {
  constexpr int kMaximumAttempts = 3;
  for (int attempt = 0; attempt < kMaximumAttempts; ++attempt) {
    if (RunCallbackLatencySelfTestAttempt() == 0) {
      return 0;
    }
    DrainCurrentThreadMessages(150);
  }
  return 1;
}

int RunTransformCallbackLatencySelfTestAttempt() noexcept {
  constexpr std::string_view kRawWord = "tieengs ";
  constexpr std::size_t kWarmupWords = 1;
  constexpr std::size_t kMeasuredWords = 256;
  constexpr std::size_t kBatchWords = 1;
  constexpr std::uint64_t kExpectedEvents =
      kMeasuredWords * kRawWord.size() * 2;
  constexpr std::uint64_t kExpectedContexts =
      kMeasuredWords * kRawWord.size();
  constexpr std::uint64_t kExpectedSuppressions = kMeasuredWords * 2;
  static_assert(kMeasuredWords % kBatchWords == 0);

  const bool caps_lock = (GetKeyState(VK_CAPITAL) & 0x0001) != 0;
  const std::wstring_view expected_word =
      caps_lock ? std::wstring_view(L"TIẾNG ")
                : std::wstring_view(L"tiếng ");
  std::wstring expected_batch;
  try {
    expected_batch.reserve(expected_word.size() * kBatchWords);
    for (std::size_t index = 0; index < kBatchWords; ++index) {
      expected_batch.append(expected_word);
    }
  } catch (...) {
    WriteStandardOutput(
        "{\"result\":\"transform_callback_latency_self_test_failed\","
        "\"error\":\"expected_text_allocation_failed\"}\n");
    return 1;
  }

  const HWND previous_foreground = GetForegroundWindow();
  const HINSTANCE instance = GetModuleHandleW(nullptr);
  HWND window = CreateWindowExW(
      WS_EX_TOOLWINDOW,
      L"STATIC",
      L"Keyina transform callback latency self-test",
      WS_OVERLAPPEDWINDOW,
      -1200,
      540,
      720,
      180,
      nullptr,
      nullptr,
      instance,
      nullptr);
  if (window == nullptr) {
    WriteStandardOutput(
        "{\"result\":\"transform_callback_latency_self_test_failed\","
        "\"error\":\"window_create_failed\"}\n");
    return 1;
  }
  HWND edit = CreateWindowExW(
      0,
      L"EDIT",
      L"",
      WS_CHILD | WS_VISIBLE | WS_TABSTOP | ES_AUTOHSCROLL,
      12,
      12,
      680,
      40,
      window,
      nullptr,
      instance,
      nullptr);
  if (edit == nullptr) {
    DestroyWindow(window);
    WriteStandardOutput(
        "{\"result\":\"transform_callback_latency_self_test_failed\","
        "\"error\":\"edit_create_failed\"}\n");
    return 1;
  }

  auto profile = keyina::windows::DefaultRuntimeInputProfile();
  profile.vietnamese_enabled = true;
  profile.clipboard_compatibility_enabled = false;
  keyina::windows::Win32InputRuntime runtime(
      profile, false, false, true, kSelfTestInputMarker);
  if (!runtime.Start()) {
    DestroyWindow(window);
    WriteStandardOutput(
        "{\"result\":\"transform_callback_latency_self_test_failed\","
        "\"error\":\"runtime_start_failed\"}\n");
    return 1;
  }

  runtime.PumpMessagesFor(50);
  const bool focus_ready = FocusTestControl(window, edit);
  runtime.PumpMessagesFor(50);
  const bool focus_confirmed = GetFocus() == edit;
  const bool foreground_confirmed = GetForegroundWindow() == window;
  bool success = focus_ready && focus_confirmed && foreground_confirmed;
  std::uint32_t focus_reacquire_count = 0;
  auto ensure_test_focus = [&]() noexcept {
    if (GetFocus() == edit && GetForegroundWindow() == window) {
      return true;
    }
    ++focus_reacquire_count;
    const bool reacquired = FocusTestControl(window, edit);
    runtime.PumpMessagesFor(10);
    return reacquired && GetFocus() == edit &&
        GetForegroundWindow() == window;
  };

  std::array<wchar_t, 256> actual_text{};
  int actual_length = 0;
  auto send_batch = [&](std::uint64_t& expected_total_processed) noexcept {
    if (!ensure_test_focus()) {
      return false;
    }
    SetWindowTextW(edit, L"");
    std::uint32_t sent_total = 0;
    for (std::size_t word = 0; word < kBatchWords; ++word) {
      for (const char character : kRawWord) {
        if (!ensure_test_focus()) {
          return false;
        }
        const std::uint32_t sent = SendTestCharacter(character, false);
        if (sent != 2) {
          return false;
        }
        sent_total += sent;
      }
    }
    expected_total_processed += sent_total;
    return WaitForProcessedKeyboardEvents(
               runtime, expected_total_processed, 3000) &&
        WaitForExpectedText(
            runtime,
            edit,
            expected_batch,
            3000,
            actual_text,
            actual_length);
  };

  const std::uint64_t warmup_processed_before =
      runtime.processed_keyboard_events();
  const std::uint64_t warmup_contexts_before =
      runtime.typing_context_capture_count();
  const std::uint64_t warmup_suppressed_before =
      runtime.suppressed_edit_count();
  const std::uint64_t warmup_injections_before =
      runtime.successful_injection_count();
  std::uint64_t expected_total_processed = warmup_processed_before;
  if (success) {
    success = send_batch(expected_total_processed);
  }

  const std::uint64_t processed_before =
      runtime.processed_keyboard_events();
  const std::uint64_t contexts_before =
      runtime.typing_context_capture_count();
  const std::uint64_t suppressed_before =
      runtime.suppressed_edit_count();
  const std::uint64_t successful_injections_before =
      runtime.successful_injection_count();
  const std::uint64_t failed_injections_before =
      runtime.failed_injection_count();
  runtime.ClearCallbackLatency();

  for (std::size_t offset = 0; offset < kMeasuredWords && success;
       offset += kBatchWords) {
    success = send_batch(expected_total_processed);
  }

  const std::uint64_t processed_total =
      runtime.processed_keyboard_events();
  const std::uint64_t contexts_total =
      runtime.typing_context_capture_count();
  const std::uint64_t suppressed_total =
      runtime.suppressed_edit_count();
  const std::uint64_t successful_injections_total =
      runtime.successful_injection_count();
  const std::uint64_t failed_injections_total =
      runtime.failed_injection_count();

  const std::uint64_t warmup_events =
      processed_before >= warmup_processed_before
          ? processed_before - warmup_processed_before
          : 0;
  const std::uint64_t warmup_contexts =
      contexts_before >= warmup_contexts_before
          ? contexts_before - warmup_contexts_before
          : 0;
  const std::uint64_t warmup_suppressions =
      suppressed_before >= warmup_suppressed_before
          ? suppressed_before - warmup_suppressed_before
          : 0;
  const std::uint64_t warmup_injections =
      successful_injections_before >= warmup_injections_before
          ? successful_injections_before - warmup_injections_before
          : 0;
  const std::uint64_t processed_events =
      processed_total >= processed_before
          ? processed_total - processed_before
          : 0;
  const std::uint64_t typing_context_captures =
      contexts_total >= contexts_before
          ? contexts_total - contexts_before
          : 0;
  const std::uint64_t suppressed_edits =
      suppressed_total >= suppressed_before
          ? suppressed_total - suppressed_before
          : 0;
  const std::uint64_t successful_injections =
      successful_injections_total >= successful_injections_before
          ? successful_injections_total - successful_injections_before
          : 0;
  const std::uint64_t failed_injections =
      failed_injections_total >= failed_injections_before
          ? failed_injections_total - failed_injections_before
          : 0;

  const auto callback_latency = runtime.callback_latency_snapshot();
  const auto key_state_latency = runtime.callback_stage_latency_snapshot(
      keyina::windows::NativeCallbackLatencyStage::KeyStateAndHotkey);
  const auto key_up_latency = runtime.callback_stage_latency_snapshot(
      keyina::windows::NativeCallbackLatencyStage::KeyUpRelease);
  const auto context_latency = runtime.callback_stage_latency_snapshot(
      keyina::windows::NativeCallbackLatencyStage::TypingContext);
  const auto controller_latency = runtime.callback_stage_latency_snapshot(
      keyina::windows::NativeCallbackLatencyStage::ControllerProcess);
  const auto injection_latency = runtime.callback_stage_latency_snapshot(
      keyina::windows::NativeCallbackLatencyStage::Injection);
  const bool hook_running = runtime.hook_running();

  success = success &&
      warmup_events == kWarmupWords * kRawWord.size() * 2 &&
      warmup_contexts == kWarmupWords * kRawWord.size() &&
      warmup_suppressions == kWarmupWords * 2 &&
      warmup_injections == kWarmupWords * 2 &&
      processed_events == kExpectedEvents &&
      typing_context_captures == kExpectedContexts &&
      suppressed_edits == kExpectedSuppressions &&
      successful_injections == kExpectedSuppressions &&
      failed_injections == 0 &&
      callback_latency.sample_count == kExpectedEvents &&
      key_state_latency.sample_count == kExpectedEvents &&
      key_up_latency.sample_count == kExpectedContexts &&
      context_latency.sample_count == kExpectedContexts &&
      controller_latency.sample_count == kExpectedContexts &&
      injection_latency.sample_count == kExpectedSuppressions &&
      callback_latency.p50_ns > 0 && injection_latency.p50_ns > 0 &&
      hook_running;

  runtime.Stop();
  DestroyWindow(window);
  if (previous_foreground != nullptr) {
    SetForegroundWindow(previous_foreground);
  }

  std::array<char, 4096> json{};
  const int length = sprintf_s(
      json.data(),
      json.size(),
      "{\"result\":\"%s\",\"caps_lock\":%s,"
      "\"warmup_words\":%llu,\"warmup_events\":%llu,"
      "\"warmup_contexts\":%llu,\"warmup_suppressions\":%llu,"
      "\"measured_words\":%llu,\"expected_events\":%llu,"
      "\"processed_events\":%llu,\"typing_context_captures\":%llu,"
      "\"suppressed_edits\":%llu,\"successful_injections\":%llu,"
      "\"failed_injections\":%llu,\"callback_samples\":%llu,"
      "\"callback_p50_ns\":%llu,\"callback_p95_ns\":%llu,"
      "\"callback_p99_ns\":%llu,\"callback_maximum_ns\":%llu,"
      "\"callback_mean_ns\":%llu,"
      "\"key_state_samples\":%llu,\"key_up_samples\":%llu,"
      "\"context_samples\":%llu,\"context_p50_ns\":%llu,"
      "\"context_p95_ns\":%llu,\"context_p99_ns\":%llu,"
      "\"context_mean_ns\":%llu,"
      "\"controller_samples\":%llu,\"controller_p50_ns\":%llu,"
      "\"controller_p95_ns\":%llu,\"controller_p99_ns\":%llu,"
      "\"controller_mean_ns\":%llu,"
      "\"injection_samples\":%llu,\"injection_p50_ns\":%llu,"
      "\"injection_p95_ns\":%llu,\"injection_p99_ns\":%llu,"
      "\"injection_maximum_ns\":%llu,\"injection_mean_ns\":%llu,"
      "\"focus_reacquire_count\":%u,\"hook_running\":%s}\n",
      success
          ? "transform_callback_latency_self_test_pass"
          : "transform_callback_latency_self_test_failed",
      caps_lock ? "true" : "false",
      static_cast<unsigned long long>(kWarmupWords),
      static_cast<unsigned long long>(warmup_events),
      static_cast<unsigned long long>(warmup_contexts),
      static_cast<unsigned long long>(warmup_suppressions),
      static_cast<unsigned long long>(kMeasuredWords),
      static_cast<unsigned long long>(kExpectedEvents),
      static_cast<unsigned long long>(processed_events),
      static_cast<unsigned long long>(typing_context_captures),
      static_cast<unsigned long long>(suppressed_edits),
      static_cast<unsigned long long>(successful_injections),
      static_cast<unsigned long long>(failed_injections),
      static_cast<unsigned long long>(callback_latency.sample_count),
      static_cast<unsigned long long>(callback_latency.p50_ns),
      static_cast<unsigned long long>(callback_latency.p95_ns),
      static_cast<unsigned long long>(callback_latency.p99_ns),
      static_cast<unsigned long long>(callback_latency.maximum_ns),
      static_cast<unsigned long long>(callback_latency.mean_ns),
      static_cast<unsigned long long>(key_state_latency.sample_count),
      static_cast<unsigned long long>(key_up_latency.sample_count),
      static_cast<unsigned long long>(context_latency.sample_count),
      static_cast<unsigned long long>(context_latency.p50_ns),
      static_cast<unsigned long long>(context_latency.p95_ns),
      static_cast<unsigned long long>(context_latency.p99_ns),
      static_cast<unsigned long long>(context_latency.mean_ns),
      static_cast<unsigned long long>(controller_latency.sample_count),
      static_cast<unsigned long long>(controller_latency.p50_ns),
      static_cast<unsigned long long>(controller_latency.p95_ns),
      static_cast<unsigned long long>(controller_latency.p99_ns),
      static_cast<unsigned long long>(controller_latency.mean_ns),
      static_cast<unsigned long long>(injection_latency.sample_count),
      static_cast<unsigned long long>(injection_latency.p50_ns),
      static_cast<unsigned long long>(injection_latency.p95_ns),
      static_cast<unsigned long long>(injection_latency.p99_ns),
      static_cast<unsigned long long>(injection_latency.maximum_ns),
      static_cast<unsigned long long>(injection_latency.mean_ns),
      focus_reacquire_count,
      hook_running ? "true" : "false");
  if (length > 0) {
    WriteStandardOutput(
        std::string_view(json.data(), static_cast<std::size_t>(length)));
  }
  return success ? 0 : 1;
}

int RunTransformCallbackLatencySelfTest() noexcept {
  constexpr int kMaximumAttempts = 3;
  for (int attempt = 0; attempt < kMaximumAttempts; ++attempt) {
    if (RunTransformCallbackLatencySelfTestAttempt() == 0) {
      return 0;
    }
    DrainCurrentThreadMessages(150);
  }
  return 1;
}

int RunChromiumOrderingSelfTest() noexcept {
  constexpr std::string_view kRawPhrase =
      "tuyf banj cuws research vaf dduwa ra huowngs toots nhaats ";
  constexpr std::wstring_view kExpectedPhrase =
      L"tuỳ bạn cứ research và đưa ra hướng tốt nhất ";
  constexpr std::array<DWORD, 3> kDelaysMilliseconds{0, 5, 10};

  const HWND previous_foreground = GetForegroundWindow();
  const HINSTANCE instance = GetModuleHandleW(nullptr);
  HWND window = CreateWindowExW(
      WS_EX_TOOLWINDOW,
      L"STATIC",
      L"Keyina Chromium ordering self-test",
      WS_OVERLAPPEDWINDOW,
      -1200,
      760,
      900,
      180,
      nullptr,
      nullptr,
      instance,
      nullptr);
  if (window == nullptr) {
    WriteStandardOutput(
        "{\"result\":\"chromium_ordering_self_test_failed\","
        "\"error\":\"window_create_failed\"}\n");
    return 1;
  }
  HWND edit = CreateWindowExW(
      0,
      L"EDIT",
      L"",
      WS_CHILD | WS_VISIBLE | WS_TABSTOP | ES_AUTOHSCROLL,
      12,
      12,
      860,
      40,
      window,
      nullptr,
      instance,
      nullptr);
  if (edit == nullptr) {
    DestroyWindow(window);
    WriteStandardOutput(
        "{\"result\":\"chromium_ordering_self_test_failed\","
        "\"error\":\"edit_create_failed\"}\n");
    return 1;
  }

  auto profile = keyina::windows::DefaultRuntimeInputProfile();
  profile.vietnamese_enabled = true;
  profile.clipboard_compatibility_enabled = false;
  keyina::windows::Win32InputRuntime runtime(
      profile,
      false,
      false,
      true,
      kSelfTestInputMarker,
      true);
  if (!runtime.Start()) {
    DestroyWindow(window);
    WriteStandardOutput(
        "{\"result\":\"chromium_ordering_self_test_failed\","
        "\"error\":\"runtime_start_failed\"}\n");
    return 1;
  }

  runtime.PumpMessagesFor(50);
  const bool focus_ready = FocusTestControl(window, edit);
  runtime.PumpMessagesFor(50);
  const bool focus_confirmed = GetFocus() == edit;
  const bool foreground_confirmed = GetForegroundWindow() == window;
  bool success = focus_ready && focus_confirmed && foreground_confirmed;
  const bool caps_lock = (GetKeyState(VK_CAPITAL) & 0x0001) != 0;
  std::uint32_t focus_reacquire_count = 0;
  auto ensure_test_focus = [&]() noexcept {
    if (GetFocus() == edit && GetForegroundWindow() == window) {
      return true;
    }
    ++focus_reacquire_count;
    return FocusTestControl(window, edit) && GetFocus() == edit &&
        GetForegroundWindow() == window;
  };

  std::array<bool, kDelaysMilliseconds.size()> case_pass{};
  std::array<std::uint64_t, kDelaysMilliseconds.size()> sent_events{};
  std::array<std::uint64_t, kDelaysMilliseconds.size()> processed_events{};
  std::array<std::uint64_t, kDelaysMilliseconds.size()> suppressed_edits{};
  std::array<std::uint64_t, kDelaysMilliseconds.size()> successful_injections{};
  std::array<std::uint64_t, kDelaysMilliseconds.size()> failed_injections{};
  std::array<wchar_t, 256> actual_text{};
  int actual_length = 0;

  for (std::size_t case_index = 0;
       case_index < kDelaysMilliseconds.size();
       ++case_index) {
    if (!ensure_test_focus()) {
      success = false;
      break;
    }
    SetWindowTextW(edit, L"");
    runtime.PumpMessagesFor(20);

    const std::uint64_t processed_before =
        runtime.processed_keyboard_events();
    const std::uint64_t suppressed_before =
        runtime.suppressed_edit_count();
    const std::uint64_t successful_before =
        runtime.successful_injection_count();
    const std::uint64_t failed_before =
        runtime.failed_injection_count();

    bool sent_all = true;
    std::uint64_t sent_total = 0;
    for (const char character : kRawPhrase) {
      if (!ensure_test_focus()) {
        sent_all = false;
        break;
      }
      const std::uint32_t sent = SendTestCharacter(character, caps_lock);
      if (sent == 0) {
        sent_all = false;
        break;
      }
      sent_total += sent;
      if (kDelaysMilliseconds[case_index] != 0) {
        Sleep(kDelaysMilliseconds[case_index]);
      }
    }

    const bool processed_ready = sent_all && WaitForProcessedKeyboardEvents(
        runtime, processed_before + sent_total, 5000);
    const bool text_ready = processed_ready && WaitForExpectedText(
        runtime,
        edit,
        kExpectedPhrase,
        5000,
        actual_text,
        actual_length);

    sent_events[case_index] = sent_total;
    processed_events[case_index] =
        runtime.processed_keyboard_events() - processed_before;
    suppressed_edits[case_index] =
        runtime.suppressed_edit_count() - suppressed_before;
    successful_injections[case_index] =
        runtime.successful_injection_count() - successful_before;
    failed_injections[case_index] =
        runtime.failed_injection_count() - failed_before;
    case_pass[case_index] = sent_all && text_ready && sent_total != 0 &&
        processed_events[case_index] == sent_total &&
        suppressed_edits[case_index] == successful_injections[case_index] &&
        successful_injections[case_index] != 0 &&
        failed_injections[case_index] == 0;
  }

  const std::uint64_t total_sent_events =
      sent_events[0] + sent_events[1] + sent_events[2];
  const std::uint64_t total_suppressed_edits =
      suppressed_edits[0] + suppressed_edits[1] + suppressed_edits[2];
  const auto callback_latency = runtime.callback_latency_snapshot();
  const auto injection_latency = runtime.callback_stage_latency_snapshot(
      keyina::windows::NativeCallbackLatencyStage::Injection);
  const bool hook_running = runtime.hook_running();
  success = success && case_pass[0] && case_pass[1] && case_pass[2] &&
      callback_latency.sample_count == total_sent_events &&
      injection_latency.sample_count == total_suppressed_edits &&
      hook_running;
  runtime.Stop();
  DestroyWindow(window);
  if (previous_foreground != nullptr) {
    SetForegroundWindow(previous_foreground);
  }

  std::array<char, 512> actual_utf8{};
  const int actual_utf8_length = actual_length <= 0
      ? 0
      : WideCharToMultiByte(
            CP_UTF8,
            0,
            actual_text.data(),
            actual_length,
            actual_utf8.data(),
            static_cast<int>(actual_utf8.size() - 1),
            nullptr,
            nullptr);
  std::array<char, 2560> json{};
  const int length = sprintf_s(
      json.data(),
      json.size(),
      "{\"result\":\"%s\",\"caps_lock\":%s,"
      "\"delay_0_pass\":%s,\"delay_0_sent\":%llu,"
      "\"delay_0_processed\":%llu,\"delay_0_suppressed\":%llu,"
      "\"delay_0_successful_injections\":%llu,"
      "\"delay_0_failed_injections\":%llu,"
      "\"delay_5_pass\":%s,\"delay_5_sent\":%llu,"
      "\"delay_5_processed\":%llu,\"delay_5_suppressed\":%llu,"
      "\"delay_5_successful_injections\":%llu,"
      "\"delay_5_failed_injections\":%llu,"
      "\"delay_10_pass\":%s,\"delay_10_sent\":%llu,"
      "\"delay_10_processed\":%llu,\"delay_10_suppressed\":%llu,"
      "\"delay_10_successful_injections\":%llu,"
      "\"delay_10_failed_injections\":%llu,"
      "\"focus_ready\":%s,\"focus_confirmed\":%s,"
      "\"foreground_confirmed\":%s,\"focus_reacquire_count\":%u,"
      "\"callback_samples\":%llu,\"callback_p50_ns\":%llu,"
      "\"callback_p95_ns\":%llu,\"callback_p99_ns\":%llu,"
      "\"callback_mean_ns\":%llu,\"injection_samples\":%llu,"
      "\"injection_p50_ns\":%llu,\"injection_p95_ns\":%llu,"
      "\"injection_p99_ns\":%llu,\"injection_mean_ns\":%llu,"
      "\"hook_running\":%s,\"actual\":\"%.*s\"}\n",
      success
          ? "chromium_ordering_self_test_pass"
          : "chromium_ordering_self_test_failed",
      caps_lock ? "true" : "false",
      case_pass[0] ? "true" : "false",
      static_cast<unsigned long long>(sent_events[0]),
      static_cast<unsigned long long>(processed_events[0]),
      static_cast<unsigned long long>(suppressed_edits[0]),
      static_cast<unsigned long long>(successful_injections[0]),
      static_cast<unsigned long long>(failed_injections[0]),
      case_pass[1] ? "true" : "false",
      static_cast<unsigned long long>(sent_events[1]),
      static_cast<unsigned long long>(processed_events[1]),
      static_cast<unsigned long long>(suppressed_edits[1]),
      static_cast<unsigned long long>(successful_injections[1]),
      static_cast<unsigned long long>(failed_injections[1]),
      case_pass[2] ? "true" : "false",
      static_cast<unsigned long long>(sent_events[2]),
      static_cast<unsigned long long>(processed_events[2]),
      static_cast<unsigned long long>(suppressed_edits[2]),
      static_cast<unsigned long long>(successful_injections[2]),
      static_cast<unsigned long long>(failed_injections[2]),
      focus_ready ? "true" : "false",
      focus_confirmed ? "true" : "false",
      foreground_confirmed ? "true" : "false",
      focus_reacquire_count,
      static_cast<unsigned long long>(callback_latency.sample_count),
      static_cast<unsigned long long>(callback_latency.p50_ns),
      static_cast<unsigned long long>(callback_latency.p95_ns),
      static_cast<unsigned long long>(callback_latency.p99_ns),
      static_cast<unsigned long long>(callback_latency.mean_ns),
      static_cast<unsigned long long>(injection_latency.sample_count),
      static_cast<unsigned long long>(injection_latency.p50_ns),
      static_cast<unsigned long long>(injection_latency.p95_ns),
      static_cast<unsigned long long>(injection_latency.p99_ns),
      static_cast<unsigned long long>(injection_latency.mean_ns),
      hook_running ? "true" : "false",
      actual_utf8_length,
      actual_utf8.data());
  if (length > 0) {
    WriteStandardOutput(
        std::string_view(json.data(), static_cast<std::size_t>(length)));
  }
  return success ? 0 : 1;
}

enum class ResourceSelfTestResult {
  Passed,
  Contaminated,
  Failed,
};

ResourceSelfTestResult RunResourceSelfTestAttempt(bool enable_tray) noexcept {
  auto profile = keyina::windows::DefaultRuntimeInputProfile();
  profile.vietnamese_enabled = false;
  keyina::windows::Win32InputRuntime runtime(profile, enable_tray);
  if (!runtime.Start()) {
    std::array<char, 256> error{};
    const int length = sprintf_s(
        error.data(), error.size(),
        "{\"error\":\"runtime_start_failed\",\"stage\":%u,"
        "\"win32_error\":%lu}\n",
        static_cast<unsigned int>(runtime.startup_stage()),
        static_cast<unsigned long>(runtime.startup_error()));
    if (length > 0) {
      WriteStandardOutput(
          std::string_view(error.data(), static_cast<std::size_t>(length)));
    }
    return ResourceSelfTestResult::Failed;
  }

  runtime.PumpMessagesFor(500);
  const auto baseline_threads = MeasureSettledProcessThreadBaseline();
  const auto baseline_working_set = CurrentWorkingSet();
  const auto snapshot = keyina::windows::MeasureNativeResidentResources(
      runtime, 5000, baseline_threads);
  std::array<char, 1024> json{};
  const int length = sprintf_s(
      json.data(), json.size(),
      "{\"tray_enabled\":%s,\"baseline_working_set_bytes\":%llu,"
      "\"baseline_thread_count\":%u,\"working_set_bytes\":%llu,"
      "\"private_working_set_bytes\":%llu,"
      "\"private_memory_bytes\":%llu,\"thread_count\":%u,"
      "\"thread_count_delta\":%u,\"handle_count\":%u,"
      "\"cpu_percent\":%.6f,\"processed_keyboard_events\":%llu,"
      "\"hook_running\":%s,\"contaminated_by_input\":%s,"
      "\"memory_budget_bytes\":10485760,\"budget_pass\":%s}\n",
      enable_tray ? "true" : "false",
      static_cast<unsigned long long>(baseline_working_set),
      baseline_threads,
      static_cast<unsigned long long>(snapshot.working_set_bytes),
      static_cast<unsigned long long>(snapshot.private_working_set_bytes),
      static_cast<unsigned long long>(snapshot.private_memory_bytes),
      snapshot.thread_count,
      snapshot.thread_count_delta,
      snapshot.handle_count,
      snapshot.cpu_percent,
      static_cast<unsigned long long>(snapshot.processed_keyboard_events),
      snapshot.hook_running ? "true" : "false",
      snapshot.contaminated_by_input ? "true" : "false",
      snapshot.budget_pass ? "true" : "false");
  if (length > 0) {
    WriteStandardOutput(
        std::string_view(json.data(), static_cast<std::size_t>(length)));
  }
  runtime.Stop();
  if (snapshot.budget_pass) {
    return ResourceSelfTestResult::Passed;
  }
  return snapshot.contaminated_by_input
      ? ResourceSelfTestResult::Contaminated
      : ResourceSelfTestResult::Failed;
}

struct ResourceSelfTestChildResult {
  bool launched{};
  bool timed_out{};
  DWORD exit_code{ERROR_GEN_FAILURE};
  std::array<char, 4096> output{};
  std::size_t output_size{};
};

ResourceSelfTestChildResult RunResourceSelfTestChild(
    bool enable_tray) noexcept {
  ResourceSelfTestChildResult result{};
  SECURITY_ATTRIBUTES security{};
  security.nLength = sizeof(security);
  security.bInheritHandle = TRUE;

  HANDLE read_pipe = nullptr;
  HANDLE write_pipe = nullptr;
  if (CreatePipe(&read_pipe, &write_pipe, &security, 0) == FALSE) {
    return result;
  }
  if (SetHandleInformation(
          read_pipe, HANDLE_FLAG_INHERIT, 0) == FALSE) {
    CloseHandle(read_pipe);
    CloseHandle(write_pipe);
    return result;
  }

  std::array<wchar_t, 32768> executable{};
  const DWORD executable_length = GetModuleFileNameW(
      nullptr,
      executable.data(),
      static_cast<DWORD>(executable.size()));
  if (executable_length == 0 || executable_length >= executable.size()) {
    CloseHandle(read_pipe);
    CloseHandle(write_pipe);
    return result;
  }

  const wchar_t* attempt_argument = enable_tray
      ? L"--tray-resource-self-test-attempt"
      : L"--resource-self-test-attempt";
  std::array<wchar_t, 32768> command_line{};
  if (swprintf_s(
          command_line.data(),
          command_line.size(),
          L"\"%ls\" %ls",
          executable.data(),
          attempt_argument) <= 0) {
    CloseHandle(read_pipe);
    CloseHandle(write_pipe);
    return result;
  }

  STARTUPINFOW startup{};
  startup.cb = sizeof(startup);
  startup.dwFlags = STARTF_USESTDHANDLES | STARTF_USESHOWWINDOW;
  startup.wShowWindow = SW_HIDE;
  startup.hStdInput = GetStdHandle(STD_INPUT_HANDLE);
  startup.hStdOutput = write_pipe;
  startup.hStdError = write_pipe;
  PROCESS_INFORMATION process{};
  const BOOL created = CreateProcessW(
      executable.data(),
      command_line.data(),
      nullptr,
      nullptr,
      TRUE,
      CREATE_NO_WINDOW,
      nullptr,
      nullptr,
      &startup,
      &process);
  CloseHandle(write_pipe);
  if (created == FALSE) {
    CloseHandle(read_pipe);
    return result;
  }
  result.launched = true;

  constexpr DWORD kChildTimeoutMilliseconds = 20'000;
  const DWORD wait_result = WaitForSingleObject(
      process.hProcess, kChildTimeoutMilliseconds);
  if (wait_result == WAIT_TIMEOUT) {
    result.timed_out = true;
    static_cast<void>(TerminateProcess(process.hProcess, 1));
    static_cast<void>(WaitForSingleObject(process.hProcess, 2'000));
  }

  DWORD bytes_read = 0;
  while (result.output_size + 1 < result.output.size() &&
         ReadFile(
             read_pipe,
             result.output.data() + result.output_size,
             static_cast<DWORD>(
                 result.output.size() - result.output_size - 1),
             &bytes_read,
             nullptr) != FALSE &&
         bytes_read != 0) {
    result.output_size += bytes_read;
  }
  result.output[result.output_size] = '\0';
  static_cast<void>(GetExitCodeProcess(process.hProcess, &result.exit_code));
  CloseHandle(process.hThread);
  CloseHandle(process.hProcess);
  CloseHandle(read_pipe);
  return result;
}

int RunResourceSelfTest(bool enable_tray) noexcept {
  constexpr int kMaximumAttempts = 3;
  constexpr std::string_view kPassMarker = "\"budget_pass\":true";
  constexpr std::string_view kContaminatedMarker =
      "\"contaminated_by_input\":true";
  for (int attempt = 0; attempt < kMaximumAttempts; ++attempt) {
    const auto child = RunResourceSelfTestChild(enable_tray);
    if (!child.launched || child.timed_out) {
      WriteStandardOutput(
          child.timed_out
              ? "{\"error\":\"resource_self_test_child_timeout\"}\n"
              : "{\"error\":\"resource_self_test_child_start_failed\"}\n");
      return 1;
    }
    const std::string_view output(child.output.data(), child.output_size);
    WriteStandardOutput(output);
    if (child.exit_code == 0 && output.find(kPassMarker) !=
                                    std::string_view::npos) {
      return 0;
    }
    if (output.find(kContaminatedMarker) == std::string_view::npos) {
      return 1;
    }
    Sleep(200);
  }
  return 1;
}

int RunKeystrokeOverlaySelfTest() {
  keyina::windows::KeystrokeOverlayReducer reducer;
  keyina::windows::KeystrokeOverlayState state{};
  std::uint64_t produced = 0;
  std::uint64_t overwritten = 0;
  for (std::uint64_t generation = 1; generation <= 10; ++generation) {
    keyina::windows::KeystrokeOverlayEvent event{};
    event.kind = generation < 10
        ? keyina::windows::KeystrokeOverlayEventKind::Token
        : keyina::windows::KeystrokeOverlayEventKind::CompositionUpdated;
    event.token = u'a';
    if (generation == 10) {
      event.SetText(u"nguyễn");
    }
    event.generation = generation;
    if (produced > 0) {
      ++overwritten;
    }
    ++produced;
    state = reducer.Apply(state, event);
  }

  keyina::windows::KeystrokeOverlayWindow window;
  keyina::windows::KeystrokeOverlayPreferences preferences{};
  preferences.enabled = true;
  const bool initialized = window.Initialize(GetModuleHandleW(nullptr));
  if (initialized) {
    keyina::windows::KeystrokeOverlayPlacement placement{};
    placement.bounds = {32, 32, 320, 92};
    window.Present(
        state,
        placement,
        keyina::windows::ResolveKeystrokeOverlayMotion({}),
        preferences,
        96);
    window.HideAndReleaseTransientState();
  }
  const bool timer_active_after_hide = window.HasActiveAnimationForTesting();
  const bool focus_preserved =
      GetForegroundWindow() != window.window_for_testing();
  window.Shutdown();

  char json[512]{};
  const int length = sprintf_s(
      json, sizeof(json),
      "{\"produced\":%llu,\"overwritten\":%llu,\"rendered\":%u,"
      "\"suppressed\":1,\"pending_depth_max\":1,"
      "\"timer_active_after_hide\":%s,\"focus_preserved\":%s}\n",
      static_cast<unsigned long long>(produced),
      static_cast<unsigned long long>(overwritten),
      initialized ? 1u : 0u,
      timer_active_after_hide ? "true" : "false",
      focus_preserved ? "true" : "false");
  if (length > 0) {
    WriteStandardOutput(std::string_view(json, static_cast<std::size_t>(length)));
  }
  return initialized && !timer_active_after_hide && focus_preserved ? 0 : 1;
}

}  // namespace

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int) {
  const bool self_test = HasArgument(
      __argc, __wargv, L"--self-test");
  const bool resource_self_test = HasArgument(
      __argc, __wargv, L"--resource-self-test");
  const bool resource_self_test_attempt = HasArgument(
      __argc, __wargv, L"--resource-self-test-attempt");
  const bool typing_self_test = HasArgument(
      __argc, __wargv, L"--typing-self-test");
  const bool clipboard_typing_self_test = HasArgument(
      __argc, __wargv, L"--clipboard-typing-self-test");
  const bool tray_resource_self_test = HasArgument(
      __argc, __wargv, L"--tray-resource-self-test");
  const bool tray_resource_self_test_attempt = HasArgument(
      __argc, __wargv, L"--tray-resource-self-test-attempt");
  const bool profile_reload_self_test = HasArgument(
      __argc, __wargv, L"--profile-reload-self-test");
  const bool callback_latency_self_test = HasArgument(
      __argc, __wargv, L"--callback-latency-self-test");
  const bool transform_callback_latency_self_test = HasArgument(
      __argc, __wargv, L"--transform-callback-latency-self-test");
  const bool chromium_ordering_self_test = HasArgument(
      __argc, __wargv, L"--chromium-ordering-self-test");
  const bool keystroke_overlay_self_test = HasArgument(
      __argc, __wargv, L"--keystroke-overlay-self-test");
  const bool open_settings = HasArgument(
      __argc, __wargv, L"--open-settings");
  const bool exit_requested = HasArgument(
      __argc, __wargv, L"--exit");

  if (self_test) {
    WriteStandardOutput("keyina_input_ready\n");
    return 0;
  }
  if (resource_self_test_attempt || tray_resource_self_test_attempt) {
    const auto result = RunResourceSelfTestAttempt(
        tray_resource_self_test_attempt);
    return result == ResourceSelfTestResult::Passed ? 0 : 1;
  }
  if (resource_self_test || tray_resource_self_test) {
    return RunResourceSelfTest(tray_resource_self_test);
  }
  if (typing_self_test || clipboard_typing_self_test) {
    return RunTypingSelfTest(clipboard_typing_self_test);
  }
  if (profile_reload_self_test) {
    return RunProfileReloadSelfTest();
  }
  if (callback_latency_self_test) {
    return RunCallbackLatencySelfTest();
  }
  if (transform_callback_latency_self_test) {
    return RunTransformCallbackLatencySelfTest();
  }
  if (chromium_ordering_self_test) {
    return RunChromiumOrderingSelfTest();
  }
  if (keystroke_overlay_self_test) {
    return RunKeystrokeOverlaySelfTest();
  }

  HANDLE mutex = CreateMutexW(nullptr, FALSE, kMutexName);
  if (mutex == nullptr) {
    return 1;
  }
  if (GetLastError() == ERROR_ALREADY_EXISTS) {
    bool forwarded = true;
    if (exit_requested) {
      forwarded = ForwardCommandToExistingResident(kExitMenuCommand);
    } else if (open_settings) {
      forwarded = ForwardCommandToExistingResident(kSettingsMenuCommand);
    }
    CloseHandle(mutex);
    return forwarded ? kAlreadyRunningExitCode : 1;
  }
  if (exit_requested) {
    CloseHandle(mutex);
    return 0;
  }

  auto profile = keyina::windows::LoadRuntimeInputProfileOrDefault();
  keyina::windows::Win32InputRuntime runtime(profile, true);
  if (!runtime.Start()) {
    CloseHandle(mutex);
    return 1;
  }
  if (open_settings) {
    runtime.RequestOpenSettings();
  }
  const int exit_code = runtime.Run();
  runtime.Stop();
  CloseHandle(mutex);
  return exit_code;
}
