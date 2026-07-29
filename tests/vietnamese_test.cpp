#include <array>
#include <cstddef>
#include <optional>

#include <keyina/vietnamese.h>

#include "test_support.h"

namespace {

using keyina::Tone;
using keyina::VietnameseLetter;
using keyina::VowelShape;

struct StemCase {
  char32_t base;
  VowelShape shape;
  const char32_t* lowercase;
  const char32_t* uppercase;
};

constexpr std::array<Tone, 6> kTones = {
    Tone::None, Tone::Acute, Tone::Grave,
    Tone::Hook, Tone::Tilde, Tone::Dot,
};

constexpr std::array<StemCase, 12> kStems = {{
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

}  // namespace

KEYINA_TEST(decomposes_every_precomposed_vietnamese_vowel) {
  for (const auto& stem : kStems) {
    for (std::size_t index = 0; index < kTones.size(); ++index) {
      const auto lower = keyina::DecomposeVietnamese(stem.lowercase[index]);
      KEYINA_EXPECT_TRUE(lower.has_value());
      KEYINA_EXPECT_EQ(lower->base, stem.base);
      KEYINA_EXPECT_EQ(lower->shape, stem.shape);
      KEYINA_EXPECT_EQ(lower->tone, kTones[index]);
      KEYINA_EXPECT_EQ(lower->uppercase, false);

      const auto upper = keyina::DecomposeVietnamese(stem.uppercase[index]);
      KEYINA_EXPECT_TRUE(upper.has_value());
      KEYINA_EXPECT_EQ(upper->base, stem.base);
      KEYINA_EXPECT_EQ(upper->shape, stem.shape);
      KEYINA_EXPECT_EQ(upper->tone, kTones[index]);
      KEYINA_EXPECT_EQ(upper->uppercase, true);
    }
  }
}

KEYINA_TEST(composes_every_supported_vietnamese_vowel) {
  for (const auto& stem : kStems) {
    for (std::size_t index = 0; index < kTones.size(); ++index) {
      const VietnameseLetter lower{stem.base, stem.shape, kTones[index], false};
      const VietnameseLetter upper{stem.base, stem.shape, kTones[index], true};

      KEYINA_EXPECT_EQ(keyina::ComposeVietnamese(lower),
                       std::optional<char32_t>{stem.lowercase[index]});
      KEYINA_EXPECT_EQ(keyina::ComposeVietnamese(upper),
                       std::optional<char32_t>{stem.uppercase[index]});
    }
  }
}

KEYINA_TEST(round_trips_every_supported_scalar) {
  for (const auto& stem : kStems) {
    for (std::size_t index = 0; index < kTones.size(); ++index) {
      const auto lower = keyina::DecomposeVietnamese(stem.lowercase[index]);
      const auto upper = keyina::DecomposeVietnamese(stem.uppercase[index]);
      KEYINA_EXPECT_EQ(keyina::ComposeVietnamese(*lower),
                       std::optional<char32_t>{stem.lowercase[index]});
      KEYINA_EXPECT_EQ(keyina::ComposeVietnamese(*upper),
                       std::optional<char32_t>{stem.uppercase[index]});
    }
  }
}

KEYINA_TEST(supports_crossed_d_without_treating_it_as_a_vowel) {
  const auto lower = keyina::DecomposeVietnamese(U'đ');
  const auto upper = keyina::DecomposeVietnamese(U'Đ');

  KEYINA_EXPECT_TRUE(lower.has_value());
  KEYINA_EXPECT_EQ(lower->base, U'đ');
  KEYINA_EXPECT_EQ(lower->uppercase, false);
  KEYINA_EXPECT_TRUE(upper.has_value());
  KEYINA_EXPECT_EQ(upper->base, U'đ');
  KEYINA_EXPECT_EQ(upper->uppercase, true);
  KEYINA_EXPECT_EQ(keyina::ComposeVietnamese(*lower),
                   std::optional<char32_t>{U'đ'});
  KEYINA_EXPECT_EQ(keyina::ComposeVietnamese(*upper),
                   std::optional<char32_t>{U'Đ'});
  KEYINA_EXPECT_EQ(keyina::IsVietnameseVowel(U'đ'), false);
}

KEYINA_TEST(rejects_unsupported_letter_combinations) {
  KEYINA_EXPECT_EQ(keyina::DecomposeVietnamese(U'z'), std::nullopt);
  KEYINA_EXPECT_EQ(
      keyina::ComposeVietnamese(
          VietnameseLetter{U'i', VowelShape::Horn, Tone::None, false}),
      std::nullopt);
}
