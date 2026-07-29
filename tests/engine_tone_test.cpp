#include <array>
#include <string>
#include <string_view>

#include <keyina/engine.h>

#include "test_support.h"

namespace {

std::u32string TypeSequence(keyina::Engine& engine, std::u32string_view raw) {
  std::u32string external;
  for (const char32_t character : raw) {
    const auto edit = engine.Process(
        {keyina::KeyKind::Character, character, false, false, false});
    KEYINA_EXPECT_TRUE(edit.consumed);
    KEYINA_EXPECT_TRUE(edit.erase_codepoints <= external.size());
    external.erase(external.size() - edit.erase_codepoints);
    external.append(edit.insert);
  }
  KEYINA_EXPECT_EQ(external, std::u32string{engine.VisibleText()});
  return external;
}

struct ToneCase {
  std::u32string_view raw;
  std::u32string_view expected;
};

}  // namespace

KEYINA_TEST(applies_telex_tones_to_vietnamese_nuclei) {
  constexpr std::array<ToneCase, 7> cases = {{
      {U"as", U"á"},
      {U"af", U"à"},
      {U"ar", U"ả"},
      {U"ax", U"ã"},
      {U"aj", U"ạ"},
      {U"aaf", U"ầ"},
      {U"tieengs", U"tiếng"},
  }};

  for (const auto& test : cases) {
    keyina::Engine engine;
    KEYINA_EXPECT_EQ(TypeSequence(engine, test.raw),
                     std::u32string{test.expected});
  }
}

KEYINA_TEST(handles_horn_cluster_and_final_consonant_tone_placement) {
  keyina::Engine engine;
  KEYINA_EXPECT_EQ(TypeSequence(engine, U"dduowngf"),
                   std::u32string{U"đường"});
}

KEYINA_TEST(replaces_an_existing_tone_instead_of_stacking_marks) {
  keyina::Engine engine;
  KEYINA_EXPECT_EQ(TypeSequence(engine, U"asf"), std::u32string{U"à"});
}

KEYINA_TEST(preserves_and_relocates_tone_when_vowel_shape_changes) {
  keyina::Engine engine;
  KEYINA_EXPECT_EQ(TypeSequence(engine, U"asw"), std::u32string{U"ắ"});
}

KEYINA_TEST(modern_and_traditional_policies_place_open_diphthong_tones_differently) {
  keyina::Engine modern({keyina::TonePlacement::Modern, false});
  keyina::Engine traditional({keyina::TonePlacement::Traditional, false});

  KEYINA_EXPECT_EQ(TypeSequence(modern, U"hoaf"), std::u32string{U"hoà"});
  KEYINA_EXPECT_EQ(TypeSequence(traditional, U"hoaf"),
                   std::u32string{U"hòa"});
}

KEYINA_TEST(tone_key_without_a_vowel_remains_literal) {
  keyina::Engine engine;
  KEYINA_EXPECT_EQ(TypeSequence(engine, U"s"), std::u32string{U"s"});
}
