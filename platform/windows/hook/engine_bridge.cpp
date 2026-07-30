#include <windows.h>

#include <keyina/engine.h>

#include <algorithm>
#include <cstddef>
#include <cstdint>
#include <limits>
#include <new>
#include <string>
#include <string_view>

namespace {

struct EngineHandle {
  EngineHandle() : engine(config) {}

  void Configure(keyina::EngineConfig next) {
    config = next;
    engine = keyina::Engine(config);
  }

  keyina::EngineConfig config{};
  keyina::Engine engine;
};

struct EngineEditResult {
  std::uint32_t erase_codepoints;
  std::uint32_t insert_utf16_units;
  std::uint8_t consumed;
  std::uint8_t commit_before;
  std::uint8_t reserved[2];
};

bool AppendUtf16(char32_t codepoint, std::wstring& output) {
  if (codepoint <= 0xD7FF ||
      (codepoint >= 0xE000 && codepoint <= 0xFFFF)) {
    output.push_back(static_cast<wchar_t>(codepoint));
    return true;
  }
  if (codepoint < 0x10000 || codepoint > 0x10FFFF) {
    return false;
  }
  const char32_t adjusted = codepoint - 0x10000;
  output.push_back(static_cast<wchar_t>(0xD800 + (adjusted >> 10)));
  output.push_back(static_cast<wchar_t>(0xDC00 + (adjusted & 0x3FF)));
  return true;
}

bool ToUtf16(std::u32string_view input, std::wstring& output) {
  output.clear();
  output.reserve(input.size());
  for (const char32_t codepoint : input) {
    if (!AppendUtf16(codepoint, output)) {
      output.clear();
      return false;
    }
  }
  return true;
}

bool CopyUtf16(std::wstring_view source, wchar_t* destination,
               std::uint32_t capacity, std::uint32_t* required) {
  if (required == nullptr ||
      source.size() > std::numeric_limits<std::uint32_t>::max()) {
    return false;
  }
  *required = static_cast<std::uint32_t>(source.size());
  if (source.empty()) {
    return true;
  }
  if (destination == nullptr || capacity < source.size()) {
    return false;
  }
  std::copy(source.begin(), source.end(), destination);
  return true;
}

}  // namespace

extern "C" __declspec(dllexport) void* __cdecl keyina_engine_create() {
  return new (std::nothrow) EngineHandle{};
}

extern "C" __declspec(dllexport) void __cdecl keyina_engine_destroy(
    void* handle) {
  delete static_cast<EngineHandle*>(handle);
}

extern "C" __declspec(dllexport) void __cdecl keyina_engine_reset(
    void* handle) {
  if (handle != nullptr) {
    static_cast<EngineHandle*>(handle)->engine.Reset();
  }
}

extern "C" __declspec(dllexport) int __cdecl keyina_engine_configure(
    void* handle, std::uint8_t traditional_tone_placement,
    std::uint8_t application_bypass, std::uint8_t restore_invalid_word) {
  if (handle == nullptr) {
    return 0;
  }
  static_cast<EngineHandle*>(handle)->Configure({
      traditional_tone_placement != 0 ? keyina::TonePlacement::Traditional
                                      : keyina::TonePlacement::Modern,
      application_bypass != 0,
      restore_invalid_word != 0,
  });
  return 1;
}

extern "C" __declspec(dllexport) int __cdecl keyina_engine_process(
    void* handle, std::uint32_t kind, std::uint32_t character,
    std::uint8_t shift, std::uint8_t control, std::uint8_t alt,
    wchar_t* insert_buffer, std::uint32_t insert_capacity,
    EngineEditResult* result) {
  if (handle == nullptr || result == nullptr || kind > 3) {
    return 0;
  }

  const keyina::KeyEvent event{
      static_cast<keyina::KeyKind>(kind),
      static_cast<char32_t>(character),
      shift != 0,
      control != 0,
      alt != 0,
  };
  const keyina::TextEdit edit =
      static_cast<EngineHandle*>(handle)->engine.Process(event);

  std::wstring insert;
  if (!ToUtf16(edit.insert, insert) ||
      edit.erase_codepoints > std::numeric_limits<std::uint32_t>::max()) {
    return 0;
  }

  std::uint32_t required = 0;
  if (!CopyUtf16(insert, insert_buffer, insert_capacity, &required)) {
    result->erase_codepoints =
        static_cast<std::uint32_t>(edit.erase_codepoints);
    result->insert_utf16_units = required;
    result->consumed = edit.consumed ? 1 : 0;
    result->commit_before = edit.commit_before ? 1 : 0;
    return insert_buffer == nullptr && insert_capacity == 0 ? 2 : 0;
  }

  result->erase_codepoints = static_cast<std::uint32_t>(edit.erase_codepoints);
  result->insert_utf16_units = required;
  result->consumed = edit.consumed ? 1 : 0;
  result->commit_before = edit.commit_before ? 1 : 0;
  return 1;
}

extern "C" __declspec(dllexport) int __cdecl keyina_engine_visible(
    void* handle, wchar_t* buffer, std::uint32_t capacity,
    std::uint32_t* required) {
  if (handle == nullptr) {
    return 0;
  }
  std::wstring visible;
  if (!ToUtf16(static_cast<EngineHandle*>(handle)->engine.VisibleText(),
               visible)) {
    return 0;
  }
  if (CopyUtf16(visible, buffer, capacity, required)) {
    return 1;
  }
  return buffer == nullptr && capacity == 0 ? 2 : 0;
}
