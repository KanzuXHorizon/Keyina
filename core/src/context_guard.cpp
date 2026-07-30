#include <keyina/context_guard.h>

#include <cassert>
#include <cstddef>
#include <string_view>

namespace keyina {
namespace {

constexpr bool IsAsciiLower(char32_t value) noexcept {
  return value >= U'a' && value <= U'z';
}

constexpr bool IsAsciiUpper(char32_t value) noexcept {
  return value >= U'A' && value <= U'Z';
}

constexpr bool IsAsciiLetter(char32_t value) noexcept {
  return IsAsciiLower(value) || IsAsciiUpper(value);
}

constexpr bool IsAsciiDigit(char32_t value) noexcept {
  return value >= U'0' && value <= U'9';
}

constexpr bool IsAsciiHex(char32_t value) noexcept {
  return IsAsciiDigit(value) ||
         (value >= U'a' && value <= U'f') ||
         (value >= U'A' && value <= U'F');
}

constexpr char32_t ToAsciiLower(char32_t value) noexcept {
  return IsAsciiUpper(value) ? value + (U'a' - U'A') : value;
}

bool StartsWithInsensitive(std::u32string_view value,
                           std::u32string_view prefix) noexcept {
  if (value.size() < prefix.size()) {
    return false;
  }
  for (std::size_t index = 0; index < prefix.size(); ++index) {
    if (ToAsciiLower(value[index]) != ToAsciiLower(prefix[index])) {
      return false;
    }
  }
  return true;
}

bool Contains(std::u32string_view value,
              std::u32string_view needle) noexcept {
  return value.find(needle) != std::u32string_view::npos;
}

bool IsUrl(std::u32string_view token) noexcept {
  return Contains(token, U"://") || StartsWithInsensitive(token, U"www.") ||
         StartsWithInsensitive(token, U"http:") ||
         StartsWithInsensitive(token, U"https:") ||
         StartsWithInsensitive(token, U"ftp:");
}

bool IsEmail(std::u32string_view token) noexcept {
  const auto at = token.find(U'@');
  if (at == std::u32string_view::npos || at == 0 || at + 1 >= token.size()) {
    return false;
  }

  bool domain_has_dot = false;
  for (std::size_t index = 0; index < token.size(); ++index) {
    const char32_t value = token[index];
    if (index > at && value == U'.') {
      domain_has_dot = true;
    }
    const bool allowed = IsAsciiLetter(value) || IsAsciiDigit(value) ||
                         value == U'.' || value == U'_' || value == U'-' ||
                         value == U'+' || value == U'@';
    if (!allowed) {
      return false;
    }
  }
  return domain_has_dot;
}

bool IsFilePath(std::u32string_view token) noexcept {
  if (token.size() >= 3 && IsAsciiLetter(token[0]) && token[1] == U':' &&
      (token[2] == U'\\' || token[2] == U'/')) {
    return true;
  }
  if (token.starts_with(U"\\\\") || token.starts_with(U"./") ||
      token.starts_with(U"../") || token.starts_with(U"~/")) {
    return true;
  }
  return token.find(U'\\') != std::u32string_view::npos ||
         token.find(U'/') != std::u32string_view::npos;
}

bool IsVersionOrHash(std::u32string_view token) noexcept {
  std::size_t start = 0;
  if (token.size() > 1 && (token.front() == U'v' || token.front() == U'V')) {
    start = 1;
  }

  std::size_t dots = 0;
  bool has_digit = false;
  bool version_chars_only = start < token.size();
  for (std::size_t index = start; index < token.size(); ++index) {
    const char32_t value = token[index];
    if (value == U'.') {
      ++dots;
    } else if (IsAsciiDigit(value)) {
      has_digit = true;
    } else {
      version_chars_only = false;
      break;
    }
  }
  if (version_chars_only && has_digit && dots >= 2) {
    return true;
  }

  if (token.size() < 7) {
    return false;
  }
  bool has_hex_digit = false;
  bool has_hex_letter = false;
  for (const char32_t value : token) {
    if (!IsAsciiHex(value)) {
      return false;
    }
    has_hex_digit = has_hex_digit || IsAsciiDigit(value);
    has_hex_letter = has_hex_letter ||
                     (ToAsciiLower(value) >= U'a' &&
                      ToAsciiLower(value) <= U'f');
  }
  return has_hex_digit && has_hex_letter;
}

bool IsIdentifier(std::u32string_view token) noexcept {
  bool has_letter = false;
  bool has_digit = false;
  bool has_identifier_punctuation = false;
  bool has_case_transition = false;
  bool previous_lower = false;

  for (std::size_t index = 0; index < token.size(); ++index) {
    const char32_t value = token[index];
    if (IsAsciiLetter(value)) {
      has_letter = true;
      has_case_transition = has_case_transition ||
                            (previous_lower && IsAsciiUpper(value));
      previous_lower = IsAsciiLower(value);
      continue;
    }
    previous_lower = false;
    if (IsAsciiDigit(value)) {
      has_digit = true;
      continue;
    }
    if (value == U'_') {
      has_identifier_punctuation = true;
      continue;
    }
    if (value == U':' && index + 1 < token.size() && token[index + 1] == U':') {
      has_identifier_punctuation = true;
      ++index;
      continue;
    }
    return false;
  }

  return has_identifier_punctuation || has_case_transition ||
         (has_letter && has_digit && !IsVersionOrHash(token));
}

bool IsShellToken(std::u32string_view token) noexcept {
  if (token.empty()) {
    return false;
  }
  if (token.front() == U'-' || token.front() == U'$' ||
      token.front() == U'%') {
    return true;
  }
  return Contains(token, U"&&") || Contains(token, U"||") ||
         token.find(U'=') != std::u32string_view::npos;
}

GuardResult ClassifyTokenReference(std::u32string_view token,
                                   const GuardContext& context) noexcept {
  if (context.modifier_chord) {
    return {false, GuardReason::ModifierChord};
  }
  if (IsUrl(token)) {
    return {false, GuardReason::Url};
  }
  if (IsEmail(token)) {
    return {false, GuardReason::Email};
  }
  if (IsFilePath(token)) {
    return {false, GuardReason::FilePath};
  }
  if (IsIdentifier(token)) {
    return {false, GuardReason::Identifier};
  }
  if (IsVersionOrHash(token)) {
    return {false, GuardReason::VersionOrHash};
  }
  if (IsShellToken(token)) {
    return {false, GuardReason::ShellToken};
  }
  if (context.application_bypass) {
    return {false, GuardReason::ApplicationBypass};
  }
  return {true, GuardReason::None};
}

GuardResult ClassifyTokenFast(std::u32string_view token,
                              const GuardContext& context) noexcept {
  if (context.modifier_chord) {
    return {false, GuardReason::ModifierChord};
  }

  bool previous_lower = false;
  bool ascii_identifier_eligible = true;
  bool case_transition = false;
  for (const char32_t value : token) {
    if (IsAsciiLetter(value)) {
      case_transition = case_transition ||
                        (ascii_identifier_eligible && previous_lower &&
                         IsAsciiUpper(value));
      previous_lower = IsAsciiLower(value);
      continue;
    }
    if (value > 0x7FU) {
      ascii_identifier_eligible = false;
      previous_lower = false;
      continue;
    }
    return ClassifyTokenReference(token, context);
  }

  if (ascii_identifier_eligible && case_transition) {
    return {false, GuardReason::Identifier};
  }
  if (context.application_bypass) {
    return {false, GuardReason::ApplicationBypass};
  }
  return {true, GuardReason::None};
}

}  // namespace

GuardResult ClassifyToken(std::u32string_view token,
                          const GuardContext& context) noexcept {
  const GuardResult result = ClassifyTokenFast(token, context);
#ifndef NDEBUG
  assert(result == ClassifyTokenReference(token, context));
#endif
  return result;
}

}  // namespace keyina
