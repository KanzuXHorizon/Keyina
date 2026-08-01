#pragma once

#include <cstdint>

namespace keyina {

enum class InputCharacterClass : std::uint8_t {
  NoText,
  Composition,
  CommitBoundary,
};

[[nodiscard]] constexpr bool IsAsciiCompositionCharacter(
    char32_t character) noexcept {
  return (character >= U'A' && character <= U'Z') ||
         (character >= U'a' && character <= U'z');
}

[[nodiscard]] constexpr bool IsQuickTelexCompositionCharacter(
    char32_t character) noexcept {
  return character == U'[' || character == U']' ||
         character == U'{' || character == U'}';
}

[[nodiscard]] constexpr InputCharacterClass ClassifyInputCharacter(
    char32_t character,
    bool quick_telex_letters) noexcept {
  if (character == U'\0') {
    return InputCharacterClass::NoText;
  }
  if (IsAsciiCompositionCharacter(character) ||
      (quick_telex_letters &&
       IsQuickTelexCompositionCharacter(character))) {
    return InputCharacterClass::Composition;
  }
  return InputCharacterClass::CommitBoundary;
}

}  // namespace keyina
