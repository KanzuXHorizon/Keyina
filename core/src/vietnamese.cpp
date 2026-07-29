#include <keyina/vietnamese.h>

#include <array>
#include <cstddef>
#include <string_view>

namespace keyina {
namespace {

struct Stem {
  char32_t base;
  VowelShape shape;
  std::u32string_view lowercase;
  std::u32string_view uppercase;
};

constexpr std::array<Stem, 12> kStems = {{
    {U'a', VowelShape::Plain, U"aáàảãạ", U"AÁÀẢÃẠ"},
    {U'a', VowelShape::Breve, U"ăắằẳẵặ", U"ĂẮẰẲẴẶ"},
    {U'a', VowelShape::Circumflex, U"âấầẩẫậ", U"ÂẤẦẨẪẬ"},
    {U'e', VowelShape::Plain, U"eéèẻẽẹ", U"EÉÈẺẼẸ"},
    {U'e', VowelShape::Circumflex, U"êếềểễệ", U"ÊẾỀỂỄỆ"},
    {U'i', VowelShape::Plain, U"iíìỉĩị", U"IÍÌỈĨỊ"},
    {U'o', VowelShape::Plain, U"oóòỏõọ", U"OÓÒỎÕỌ"},
    {U'o', VowelShape::Circumflex, U"ôốồổỗộ", U"ÔỐỒỔỖỘ"},
    {U'o', VowelShape::Horn, U"ơớờởỡợ", U"ƠỚỜỞỠỢ"},
    {U'u', VowelShape::Plain, U"uúùủũụ", U"UÚÙỦŨỤ"},
    {U'u', VowelShape::Horn, U"ưứừửữự", U"ƯỨỪỬỮỰ"},
    {U'y', VowelShape::Plain, U"yýỳỷỹỵ", U"YÝỲỶỸỴ"},
}};

constexpr std::size_t ToneIndex(Tone tone) noexcept {
  switch (tone) {
    case Tone::None:
      return 0;
    case Tone::Acute:
      return 1;
    case Tone::Grave:
      return 2;
    case Tone::Hook:
      return 3;
    case Tone::Tilde:
      return 4;
    case Tone::Dot:
      return 5;
  }
  return kStems.front().lowercase.size();
}

}  // namespace

std::optional<VietnameseLetter> DecomposeVietnamese(char32_t scalar) noexcept {
  if (scalar == U'đ') {
    return VietnameseLetter{U'đ', VowelShape::Plain, Tone::None, false};
  }
  if (scalar == U'Đ') {
    return VietnameseLetter{U'đ', VowelShape::Plain, Tone::None, true};
  }

  for (const auto& stem : kStems) {
    for (std::size_t index = 0; index < stem.lowercase.size(); ++index) {
      if (stem.lowercase[index] == scalar) {
        return VietnameseLetter{stem.base, stem.shape,
                                static_cast<Tone>(index), false};
      }
      if (stem.uppercase[index] == scalar) {
        return VietnameseLetter{stem.base, stem.shape,
                                static_cast<Tone>(index), true};
      }
    }
  }

  return std::nullopt;
}

std::optional<char32_t> ComposeVietnamese(
    const VietnameseLetter& letter) noexcept {
  if (letter.base == U'đ') {
    if (letter.shape == VowelShape::Plain && letter.tone == Tone::None) {
      return letter.uppercase ? U'Đ' : U'đ';
    }
    return std::nullopt;
  }

  const std::size_t tone_index = ToneIndex(letter.tone);
  for (const auto& stem : kStems) {
    if (stem.base == letter.base && stem.shape == letter.shape &&
        tone_index < stem.lowercase.size()) {
      return letter.uppercase ? stem.uppercase[tone_index]
                              : stem.lowercase[tone_index];
    }
  }

  return std::nullopt;
}

bool IsVietnameseVowel(char32_t scalar) noexcept {
  const auto decomposed = DecomposeVietnamese(scalar);
  return decomposed.has_value() && decomposed->base != U'đ';
}

}  // namespace keyina
