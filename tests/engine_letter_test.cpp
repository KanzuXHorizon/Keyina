#include <array>
#include <string>
#include <string_view>

#include <keyina/engine.h>

#include "test_support.h"

namespace {

std::u32string Type(keyina::Engine& engine, std::u32string_view raw) {
  std::u32string external;
  for (const char32_t character : raw) {
    const auto edit = engine.Process(
        {keyina::KeyKind::Character, character, false, false, false});
    KEYINA_EXPECT_TRUE(edit.consumed);
    KEYINA_EXPECT_TRUE(edit.erase_codepoints <= external.size());
    external.erase(external.size() - edit.erase_codepoints);
    external.append(edit.insert);
    KEYINA_EXPECT_EQ(external, std::u32string{engine.VisibleText()});
  }
  return external;
}

struct LetterCase {
  std::u32string_view raw;
  std::u32string_view expected;
};

}  // namespace

KEYINA_TEST(applies_telex_letter_modifiers) {
  constexpr std::array<LetterCase, 8> cases = {{
      {U"aa", U"â"},
      {U"aw", U"ă"},
      {U"ee", U"ê"},
      {U"oo", U"ô"},
      {U"ow", U"ơ"},
      {U"uw", U"ư"},
      {U"dd", U"đ"},
      {U"uow", U"ươ"},
  }};

  for (const auto& test : cases) {
    keyina::Engine engine;
    KEYINA_EXPECT_EQ(Type(engine, test.raw), std::u32string{test.expected});
    KEYINA_EXPECT_EQ(engine.RawKeys(), test.raw);
  }
}

KEYINA_TEST(preserves_uppercase_when_modifying_letters) {
  constexpr std::array<LetterCase, 5> cases = {{
      {U"AA", U"Â"},
      {U"Aw", U"Ă"},
      {U"OW", U"Ơ"},
      {U"UW", U"Ư"},
      {U"DD", U"Đ"},
  }};

  for (const auto& test : cases) {
    keyina::Engine engine;
    KEYINA_EXPECT_EQ(Type(engine, test.raw), std::u32string{test.expected});
  }
}

KEYINA_TEST(repeated_letter_modifier_escapes_to_literal_telex) {
  constexpr std::array<LetterCase, 7> cases = {{
      {U"aaa", U"aa"},
      {U"aww", U"aw"},
      {U"eee", U"ee"},
      {U"ooo", U"oo"},
      {U"oww", U"ow"},
      {U"uww", U"uw"},
      {U"ddd", U"dd"},
  }};

  for (const auto& test : cases) {
    keyina::Engine engine;
    KEYINA_EXPECT_EQ(Type(engine, test.raw), std::u32string{test.expected});
  }
}

KEYINA_TEST(applies_delayed_w_modifier_across_the_vowel_nucleus) {
  constexpr std::array<LetterCase, 13> cases = {{
      {U"nuwax", U"nữa"},
      {U"nuawx", U"nữa"},
      {U"nuwxa", U"nữa"},
      {U"truocws", U"trước"},
      {U"truowcs", U"trước"},
      {U"truwocs", U"trước"},
      {U"truowsc", U"trước"},
      {U"truocsw", U"trước"},
      {U"dduocwj", U"được"},
      {U"dduowcj", U"được"},
      {U"dduwocj", U"được"},
      {U"dduowjc", U"được"},
      {U"vieetj", U"việt"},
  }};

  for (const auto& test : cases) {
    keyina::Engine engine;
    const auto actual = Type(engine, test.raw);
    if (actual != test.expected) {
      std::string raw;
      raw.reserve(test.raw.size());
      for (const char32_t value : test.raw) {
        raw.push_back(static_cast<char>(value));
      }
      throw std::runtime_error("failed delayed modifier case: " + raw);
    }
  }
}

KEYINA_TEST(delayed_d_modifier_does_not_corrupt_latin_words) {
  keyina::Engine engine;
  KEYINA_EXPECT_EQ(Type(engine, U"david"), std::u32string{U"david"});
}

KEYINA_TEST(leaves_non_modifier_characters_literal) {
  keyina::Engine engine;
  KEYINA_EXPECT_EQ(Type(engine, U"xin chao"), std::u32string{U"xin chao"});
}

KEYINA_TEST(commit_boundary_clears_active_composition_without_consuming_key) {
  keyina::Engine engine;
  KEYINA_EXPECT_EQ(Type(engine, U"aa"), std::u32string{U"â"});

  const auto edit = engine.Process(
      {keyina::KeyKind::CommitBoundary, U' ', false, false, false});

  KEYINA_EXPECT_EQ(edit.consumed, false);
  KEYINA_EXPECT_EQ(edit.erase_codepoints, std::size_t{0});
  KEYINA_EXPECT_TRUE(edit.insert.empty());
  KEYINA_EXPECT_TRUE(engine.VisibleText().empty());
  KEYINA_EXPECT_TRUE(engine.RawKeys().empty());
}

KEYINA_TEST(modifier_chord_resets_composition_and_passes_through) {
  keyina::Engine engine;
  Type(engine, U"aa");

  const auto edit = engine.Process(
      {keyina::KeyKind::Character, U'c', false, true, false});

  KEYINA_EXPECT_EQ(edit.consumed, false);
  KEYINA_EXPECT_TRUE(engine.VisibleText().empty());
  KEYINA_EXPECT_TRUE(engine.RawKeys().empty());
}
