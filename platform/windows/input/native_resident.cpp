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

bool SendTestCharacter(char character, bool caps_lock) noexcept {
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
             static_cast<UINT>(count), inputs.data(), sizeof(INPUT)) == count;
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

  auto profile = keyina::windows::DefaultRuntimeInputProfile();
  profile.vietnamese_enabled = true;
  keyina::windows::Win32InputRuntime runtime(profile, false);
  if (!runtime.Start()) {
    DestroyWindow(window);
    WriteStandardOutput("typing_self_test_runtime_failed\n");
    return 1;
  }

  runtime.PumpMessagesFor(50);
  const bool focus_ready = FocusTestControl(window, edit);
  runtime.PumpMessagesFor(50);
  const bool focus_confirmed = GetFocus() == edit;
  const bool foreground_confirmed = GetForegroundWindow() == window;
  bool success = focus_ready && focus_confirmed && foreground_confirmed;
  const bool caps_lock = (GetKeyState(VK_CAPITAL) & 0x0001) != 0;
  constexpr std::string_view raw = "tieengs vieetj";
  for (const char character : raw) {
    if (!success || !SendTestCharacter(character, caps_lock)) {
      success = false;
      break;
    }
    runtime.PumpMessagesFor(20);
  }
  runtime.PumpMessagesFor(300);

  std::array<wchar_t, 64> text{};
  const int length = GetWindowTextW(
      edit, text.data(), static_cast<int>(text.size()));
  success = success && length > 0 &&
      std::wstring_view(text.data(), static_cast<std::size_t>(length)) ==
          L"tiếng việt";

  runtime.Stop();
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
      static_cast<unsigned long long>(runtime.processed_keyboard_events()),
      length, utf8_length, actual_utf8.data());
  if (diagnostic_length > 0) {
    WriteStandardOutput(std::string_view(
        diagnostic.data(), static_cast<std::size_t>(diagnostic_length)));
  }
  return 1;
}

int RunResourceSelfTest(bool enable_tray) noexcept {
  const auto baseline_threads = CountProcessThreads();
  const auto baseline_working_set = CurrentWorkingSet();
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
