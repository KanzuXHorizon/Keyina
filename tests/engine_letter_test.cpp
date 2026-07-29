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
