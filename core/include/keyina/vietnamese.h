#pragma once

#include <optional>

namespace keyina {

enum class Tone {
  None,
  Acute,
  Grave,
  Hook,
  Tilde,
  Dot,
};

enum class VowelShape {
  Plain,
  Breve,
  Circumflex,
  Horn,
};

struct VietnameseLetter {
  char32_t base;
  VowelShape shape;
  Tone tone;
  bool uppercase;

  friend constexpr bool operator==(const VietnameseLetter&,
                                   const VietnameseLetter&) = default;
};

[[nodiscard]] std::optional<VietnameseLetter> DecomposeVietnamese(
    char32_t scalar) noexcept;

[[nodiscard]] std::optional<char32_t> ComposeVietnamese(
    const VietnameseLetter& letter) noexcept;

[[nodiscard]] bool IsVietnameseVowel(char32_t scalar) noexcept;

}  // namespace keyina
