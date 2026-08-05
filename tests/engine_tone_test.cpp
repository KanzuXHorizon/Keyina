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

KEYINA_TEST(relocates_early_tone_keys_after_later_final_consonants) {
  constexpr std::array<ToneCase, 8> cases = {{
      {U"vieejt", U"việt"},
      {U"tieesng", U"tiếng"},
      {U"vowis", U"với"},
      {U"voisw", U"với"},
      {U"vaanx", U"vẫn"},
      {U"mootj", U"một"},
      {U"motoj", U"một"},
      {U"moot", U"môt"},
  }};

  for (const auto& test : cases) {
    keyina::Engine engine;
    const auto actual = TypeSequence(engine, test.raw);
    if (actual != test.expected) {
      std::string raw;
      raw.reserve(test.raw.size());
      for (const char32_t value : test.raw) {
        raw.push_back(static_cast<char>(value));
      }
      throw std::runtime_error("failed early tone case: " + raw);
    }
  }
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
  constexpr std::array<ToneCase, 3> modern_cases = {{
      {U"hoaf", U"hoà"},
      {U"khoer", U"khoẻ"},
      {U"thuyr", U"thuỷ"},
  }};
  constexpr std::array<ToneCase, 3> traditional_cases = {{
      {U"hoaf", U"hòa"},
      {U"khoer", U"khỏe"},
      {U"thuyr", U"thủy"},
  }};

  for (const auto& test : modern_cases) {
    keyina::Engine engine({keyina::TonePlacement::Modern, false});
    KEYINA_EXPECT_EQ(TypeSequence(engine, test.raw),
                     std::u32string{test.expected});
  }
  for (const auto& test : traditional_cases) {
    keyina::Engine engine({keyina::TonePlacement::Traditional, false});
    KEYINA_EXPECT_EQ(TypeSequence(engine, test.raw),
                     std::u32string{test.expected});
  }
}

KEYINA_TEST(consonantal_glides_are_not_treated_as_open_uy_clusters) {
  for (const auto placement : {keyina::TonePlacement::Modern,
                               keyina::TonePlacement::Traditional}) {
    keyina::Engine quy({placement, false});
    keyina::Engine gia({placement, false});
    KEYINA_EXPECT_EQ(TypeSequence(quy, U"quyf"), std::u32string{U"quỳ"});
    KEYINA_EXPECT_EQ(TypeSequence(gia, U"giaf"), std::u32string{U"già"});
  }
}

KEYINA_TEST(z_removes_existing_tone_but_preserves_vowel_shape) {
  constexpr std::array<ToneCase, 5> cases = {{
      {U"asz", U"a"},
      {U"aasz", U"â"},
      {U"owjsz", U"ơ"},
      {U"owjz", U"ơ"},
      {U"aszs", U"á"},
  }};

  for (const auto& test : cases) {
    keyina::Engine engine;
    KEYINA_EXPECT_EQ(TypeSequence(engine, test.raw),
                     std::u32string{test.expected});
  }
}

KEYINA_TEST(z_preserves_vowel_shape_with_invalid_latin_restore_enabled) {
  constexpr std::array<ToneCase, 3> cases = {{
      {U"aasz", U"â"},
      {U"owjz", U"ơ"},
      {U"uwxz", U"ư"},
  }};

  for (const auto& test : cases) {
    keyina::Engine engine({
        .restore_invalid_word = true,
    });
    KEYINA_EXPECT_EQ(TypeSequence(engine, test.raw),
                     std::u32string{test.expected});
  }

  for (const auto& test : std::array<ToneCase, 3>{{
           {U"asdz", U"asdz"},
           {U"jazz", U"jazz"},
           {U"fizz", U"fizz"},
       }}) {
    keyina::Engine engine({
        .restore_invalid_word = true,
    });
    KEYINA_EXPECT_EQ(TypeSequence(engine, test.raw),
                     std::u32string{test.expected});
  }
}

KEYINA_TEST(invalid_vowel_nucleus_does_not_receive_a_tone) {
  keyina::Engine invalid({
      .restore_invalid_word = true,
  });
  keyina::Engine valid({
      .restore_invalid_word = true,
  });

  KEYINA_EXPECT_EQ(TypeSequence(invalid, U"laiuj"),
                   std::u32string{U"laiuj"});
  KEYINA_EXPECT_EQ(TypeSequence(valid, U"laij"),
                   std::u32string{U"lại"});
}

KEYINA_TEST(z_without_an_existing_tone_remains_literal) {
  keyina::Engine engine;
  KEYINA_EXPECT_EQ(TypeSequence(engine, U"az"), std::u32string{U"az"});
}

KEYINA_TEST(repeated_tone_key_escapes_to_literal_telex) {
  constexpr std::array<ToneCase, 5> cases = {{
      {U"ass", U"as"},
      {U"aff", U"af"},
      {U"arr", U"ar"},
      {U"axx", U"ax"},
      {U"ajj", U"aj"},
  }};

  for (const auto& test : cases) {
    keyina::Engine engine;
    KEYINA_EXPECT_EQ(TypeSequence(engine, test.raw),
                     std::u32string{test.expected});
  }
}

KEYINA_TEST(latin_word_russt_preserves_repeated_s) {
  keyina::Engine engine({
      .restore_invalid_word = true,
  });
  KEYINA_EXPECT_EQ(TypeSequence(engine, U"russt"),
                   std::u32string{U"russt"});
}

KEYINA_TEST(latin_word_lossless_preserves_repeated_s) {
  keyina::Engine engine({
      .restore_invalid_word = true,
  });
  KEYINA_EXPECT_EQ(TypeSequence(engine, U"lossless"),
                   std::u32string{U"lossless"});
}

KEYINA_TEST(latin_word_classless_preserves_repeated_s) {
  keyina::Engine engine({
      .restore_invalid_word = true,
  });
  KEYINA_EXPECT_EQ(TypeSequence(engine, U"classless"),
                   std::u32string{U"classless"});
}

KEYINA_TEST(latin_word_assessment_preserves_repeated_s) {
  keyina::Engine engine({
      .restore_invalid_word = true,
  });
  KEYINA_EXPECT_EQ(TypeSequence(engine, U"assessment"),
                   std::u32string{U"assessment"});
}

KEYINA_TEST(tone_key_without_a_vowel_remains_literal) {
  keyina::Engine engine;
  KEYINA_EXPECT_EQ(TypeSequence(engine, U"s"), std::u32string{U"s"});
}
