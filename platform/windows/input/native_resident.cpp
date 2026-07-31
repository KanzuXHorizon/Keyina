#include <keyina/windows/win32_input_runtime.h>

#include <psapi.h>
#include <tlhelp32.h>
#include <windows.h>

#include <array>
#include <cstdio>
#include <string_view>

namespace {

constexpr int kAlreadyRunningExitCode = 17;
constexpr wchar_t kMutexName[] = L"Local\\Keyina.NativeInput";

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
  const HWND previous_foreground = GetForegroundWindow();
  const DWORD current_thread = GetCurrentThreadId();
  DWORD foreground_thread = 0;
  if (previous_foreground != nullptr) {
    foreground_thread = GetWindowThreadProcessId(previous_foreground, nullptr);
  }
  const bool attached = foreground_thread != 0 &&
      foreground_thread != current_thread &&
      AttachThreadInput(current_thread, foreground_thread, TRUE) != FALSE;

  ShowWindow(window, SW_SHOW);
  SetWindowPos(
      window, HWND_TOP, -1200, 100, 480, 180,
      SWP_SHOWWINDOW | SWP_NOOWNERZORDER);
  const bool foreground = SetForegroundWindow(window) != FALSE;
  SetActiveWindow(window);
  SetFocus(edit);
  const bool focused = GetFocus() == edit;

  if (attached) {
    AttachThreadInput(current_thread, foreground_thread, FALSE);
  }
  return foreground && focused;
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

bool WaitForExpectedText(
    keyina::windows::Win32InputRuntime& runtime,
    HWND edit,
    std::wstring_view expected,
    DWORD timeout_milliseconds,
    std::array<wchar_t, 64>& text,
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

int RunTypingSelfTest() noexcept {
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
  HWND edit = CreateWindowExW(
      0, L"EDIT", L"", WS_CHILD | WS_VISIBLE | WS_TABSTOP | ES_AUTOHSCROLL,
      12, 12, 440, 40, window, nullptr, instance, nullptr);
  if (edit == nullptr) {
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
  std::uint64_t processed_events = 0;
  std::array<wchar_t, 64> text{};
  int length = 0;

  for (int attempt = 0; attempt < kMaximumAttempts && !success; ++attempt) {
    DrainCurrentThreadMessages(200);
    SetWindowTextW(edit, L"");
    DrainCurrentThreadMessages(20);
    auto profile = keyina::windows::DefaultRuntimeInputProfile();
    profile.vietnamese_enabled = true;
    keyina::windows::Win32InputRuntime runtime(profile, false);
    if (!runtime.Start()) {
      DestroyWindow(window);
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
    runtime.Stop();
    if (!success) {
      DrainCurrentThreadMessages(300);
    }
  }
  DestroyWindow(window);
  if (previous_foreground != nullptr) {
    SetForegroundWindow(previous_foreground);
  }
  if (success) {
    WriteStandardOutput("typing_self_test_pass\n");
    return 0;
  }

  std::array<char, 256> actual_utf8{};
  const int utf8_length = length <= 0
      ? 0
      : WideCharToMultiByte(
            CP_UTF8, 0, text.data(), length, actual_utf8.data(),
            static_cast<int>(actual_utf8.size() - 1), nullptr, nullptr);
  std::array<char, 512> diagnostic{};
  const int diagnostic_length = sprintf_s(
      diagnostic.data(), diagnostic.size(),
      "{\"error\":\"typing_self_test_failed\",\"focus_ready\":%s,"
      "\"focus_confirmed\":%s,\"foreground_confirmed\":%s,"
      "\"processed_events\":%llu,\"text_length\":%d,"
      "\"actual\":\"%.*s\"}\n",
      focus_ready ? "true" : "false",
      focus_confirmed ? "true" : "false",
      foreground_confirmed ? "true" : "false",
      static_cast<unsigned long long>(processed_events),
      length, utf8_length, actual_utf8.data());
  if (diagnostic_length > 0) {
    WriteStandardOutput(std::string_view(
        diagnostic.data(), static_cast<std::size_t>(diagnostic_length)));
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

int RunResourceSelfTest(bool enable_tray) noexcept {
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
    return 1;
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
  return snapshot.budget_pass ? 0 : 1;
}

}  // namespace

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int) {
  const bool self_test = HasArgument(
      __argc, __wargv, L"--self-test");
  const bool resource_self_test = HasArgument(
      __argc, __wargv, L"--resource-self-test");
  const bool typing_self_test = HasArgument(
      __argc, __wargv, L"--typing-self-test");
  const bool tray_resource_self_test = HasArgument(
      __argc, __wargv, L"--tray-resource-self-test");
  const bool profile_reload_self_test = HasArgument(
      __argc, __wargv, L"--profile-reload-self-test");

  if (self_test) {
    WriteStandardOutput("keyina_input_ready\n");
    return 0;
  }
  if (resource_self_test || tray_resource_self_test) {
    return RunResourceSelfTest(tray_resource_self_test);
  }
  if (typing_self_test) {
    return RunTypingSelfTest();
  }
  if (profile_reload_self_test) {
    return RunProfileReloadSelfTest();
  }

  HANDLE mutex = CreateMutexW(nullptr, FALSE, kMutexName);
  if (mutex == nullptr) {
    return 1;
  }
  if (GetLastError() == ERROR_ALREADY_EXISTS) {
    CloseHandle(mutex);
    return kAlreadyRunningExitCode;
  }

  auto profile = keyina::windows::LoadRuntimeInputProfileOrDefault();
  keyina::windows::Win32InputRuntime runtime(profile, true);
  if (!runtime.Start()) {
    CloseHandle(mutex);
    return 1;
  }
  const int exit_code = runtime.Run();
  runtime.Stop();
  CloseHandle(mutex);
  return exit_code;
}
