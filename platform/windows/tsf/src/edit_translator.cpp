#include <keyina/tsf/edit_translator.h>

#include <limits>

namespace keyina::tsf {
namespace {

constexpr bool IsValidScalar(char32_t scalar) noexcept {
  return scalar <= 0x10FFFF && !(scalar >= 0xD800 && scalar <= 0xDFFF);
}

bool AppendUtf16(std::wstring& output, char32_t scalar) {
  if (!IsValidScalar(scalar)) {
    return false;
  }
  if (scalar <= 0xFFFF) {
    output.push_back(static_cast<wchar_t>(scalar));
    return true;
  }

  const char32_t value = scalar - 0x10000;
  output.push_back(static_cast<wchar_t>(0xD800 + (value >> 10U)));
  output.push_back(static_cast<wchar_t>(0xDC00 + (value & 0x3FFU)));
  return true;
}

std::optional<std::size_t> CountUtf16Units(
    std::u32string_view value) noexcept {
  std::size_t units = 0;
  for (const char32_t scalar : value) {
    if (!IsValidScalar(scalar)) {
      return std::nullopt;
    }
    const std::size_t increment = scalar <= 0xFFFF ? 1U : 2U;
    if (units > std::numeric_limits<std::size_t>::max() - increment) {
      return std::nullopt;
    }
    units += increment;
  }
  return units;
}

}  // namespace

std::optional<Utf16Edit> TranslateEdit(
    const TextEdit& edit,
    std::u32string_view owned_composition) noexcept {
  static_assert(sizeof(wchar_t) == 2,
                "The Windows TSF adapter requires 16-bit wchar_t");

  if (edit.erase_codepoints > owned_composition.size()) {
    return std::nullopt;
  }

  const auto erased_suffix = owned_composition.substr(
      owned_composition.size() - edit.erase_codepoints);
  const auto erase_units = CountUtf16Units(erased_suffix);
  if (!erase_units.has_value()) {
    return std::nullopt;
  }

  Utf16Edit translated;
  translated.erase_utf16_units = *erase_units;
  translated.consumed = edit.consumed;
  translated.commit_before = edit.commit_before;
  translated.insert.reserve(edit.insert.size());
  for (const char32_t scalar : edit.insert) {
    if (!AppendUtf16(translated.insert, scalar)) {
      return std::nullopt;
    }
  }
  return translated;
}

}  // namespace keyina::tsf
