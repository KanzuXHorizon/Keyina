#include <cstddef>
#include <optional>
#include <string>

#include <keyina/engine.h>
#include <keyina/tsf/edit_translator.h>

#include "../test_support.h"

KEYINA_TEST(translates_erased_codepoints_to_utf16_units) {
  const keyina::TextEdit edit{1, U"x", true, false};
  const auto translated = keyina::tsf::TranslateEdit(edit, U"a🙂");

  KEYINA_EXPECT_TRUE(translated.has_value());
  KEYINA_EXPECT_EQ(translated->erase_utf16_units, std::size_t{2});
  KEYINA_EXPECT_EQ(translated->insert, std::wstring{L"x"});
  KEYINA_EXPECT_EQ(translated->consumed, true);
  KEYINA_EXPECT_EQ(translated->commit_before, false);
}

KEYINA_TEST(encodes_non_bmp_insert_text_as_surrogate_pairs) {
  const keyina::TextEdit edit{0, U"ắ🙂", true, true};
  const auto translated = keyina::tsf::TranslateEdit(edit, U"");

  KEYINA_EXPECT_TRUE(translated.has_value());
  KEYINA_EXPECT_EQ(translated->insert.size(), std::size_t{3});
  KEYINA_EXPECT_EQ(translated->commit_before, true);
}

KEYINA_TEST(rejects_edits_outside_the_owned_composition) {
  const keyina::TextEdit edit{2, U"", true, false};
  KEYINA_EXPECT_EQ(keyina::tsf::TranslateEdit(edit, U"a"), std::nullopt);
}

KEYINA_TEST(rejects_invalid_unicode_scalars) {
  const keyina::TextEdit surrogate{
      0, std::u32string{static_cast<char32_t>(0xD800)}, true, false};
  const keyina::TextEdit too_large{0, std::u32string{0x110000}, true, false};

  KEYINA_EXPECT_EQ(keyina::tsf::TranslateEdit(surrogate, U""), std::nullopt);
  KEYINA_EXPECT_EQ(keyina::tsf::TranslateEdit(too_large, U""), std::nullopt);
}
