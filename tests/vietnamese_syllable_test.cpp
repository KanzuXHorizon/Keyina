#include <array>
#include <string>
#include <string_view>

#include <keyina/engine.h>
#include <keyina/vietnamese_syllable.h>

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
  return external;
}

void ApplyEdit(std::u32string& external, const keyina::TextEdit& edit) {
  KEYINA_EXPECT_TRUE(edit.erase_codepoints <= external.size());
  external.erase(external.size() - edit.erase_codepoints);
  external.append(edit.insert);
}

}  // namespace

KEYINA_TEST(recognizes_delayed_d_candidate_as_a_valid_syllable) {
  const auto analysis = keyina::AnalyzeVietnameseSyllable(U"đong");
  KEYINA_EXPECT_EQ(analysis.status, keyina::SyllableStatus::Valid);
  KEYINA_EXPECT_EQ(analysis.error, keyina::SyllableError::None);
}

KEYINA_TEST(classifies_valid_onset_prefixes_as_recoverable) {
  constexpr std::array<std::u32string_view, 6> recoverable = {
      U"b", U"ch", U"ng", U"ngh", U"ph", U"tr",
  };
  for (const auto prefix : recoverable) {
    const auto analysis = keyina::AnalyzeVietnameseSyllable(prefix);
    KEYINA_EXPECT_EQ(analysis.status, keyina::SyllableStatus::RecoverablePrefix);
    KEYINA_EXPECT_EQ(analysis.error, keyina::SyllableError::MissingNucleus);
  }

  const auto impossible = keyina::AnalyzeVietnameseSyllable(U"zr");
  KEYINA_EXPECT_EQ(impossible.status, keyina::SyllableStatus::Impossible);
  KEYINA_EXPECT_EQ(impossible.error, keyina::SyllableError::MissingNucleus);
}

KEYINA_TEST(validates_representative_vietnamese_syllables) {
  constexpr std::array<std::u32string_view, 28> valid = {
      U"a", U"ai", U"anh", U"bạn", U"chuyện", U"được", U"giếng",
      U"hoà", U"hòa", U"khỏe", U"khuỷu", U"nghiêng", U"người",
      U"những", U"phố", U"quốc", U"quyển", U"rượu", U"sáng",
      U"thật", U"tiếng", U"trường", U"tuyệt", U"việt", U"xanh",
      U"yêu", U"ủy", U"ươu",
  };

  for (std::size_t index = 0; index < valid.size(); ++index) {
    if (!keyina::IsValidVietnameseSyllable(valid[index])) {
      throw std::runtime_error("valid syllable rejected at index " +
                               std::to_string(index));
    }
  }
}

KEYINA_TEST(rejects_structurally_invalid_vietnamese_syllables) {
  constexpr std::array<std::u32string_view, 16> invalid = {
      U"hâhhâhhâhh", U"băk", U"cấk", U"dượcp", U"giiếng", U"nghả",
      U"qúa", U"quư", U"tháhh", U"trưc", U"ưư", U"ă", U"âhh",
      U"pề", U"kă", U"nghưt",
  };

  for (std::size_t index = 0; index < invalid.size(); ++index) {
    if (keyina::IsValidVietnameseSyllable(invalid[index])) {
      throw std::runtime_error("invalid syllable accepted at index " +
                               std::to_string(index));
    }
  }
}

KEYINA_TEST(commit_boundary_never_rewrites_visible_text) {
  keyina::Engine engine({
      .tone_placement = keyina::TonePlacement::Modern,
      .application_bypass = false,
      .restore_invalid_word = true,
  });
  const auto external = TypeSequence(engine, U"haahhaahhaahh");
  KEYINA_EXPECT_EQ(external, U"hâhhâhhâhh");

  const auto boundary = engine.Process(
      {keyina::KeyKind::CommitBoundary, U' ', false, false, false});
  KEYINA_EXPECT_EQ(boundary, keyina::TextEdit{});
  KEYINA_EXPECT_TRUE(engine.VisibleText().empty());
}

KEYINA_TEST(restore_invalid_word_keeps_valid_telex_and_literal_latin) {
  keyina::Engine engine({
      .tone_placement = keyina::TonePlacement::Modern,
      .application_bypass = false,
      .restore_invalid_word = true,
  });

  std::u32string external = TypeSequence(engine, U"dduocwj");
  auto boundary = engine.Process(
      {keyina::KeyKind::CommitBoundary, U' ', false, false, false});
  if (boundary.consumed) {
    ApplyEdit(external, boundary);
  } else {
    external.push_back(U' ');
  }
  KEYINA_EXPECT_EQ(external, U"được ");

  external = TypeSequence(engine, U"openai");
  boundary = engine.Process(
      {keyina::KeyKind::CommitBoundary, U' ', false, false, false});
  if (boundary.consumed) {
    ApplyEdit(external, boundary);
  } else {
    external.push_back(U' ');
  }
  KEYINA_EXPECT_EQ(external, U"openai ");
}

KEYINA_TEST(checked_codas_only_accept_acute_or_dot_tones) {
  KEYINA_EXPECT_TRUE(keyina::IsValidVietnameseSyllable(U"mát"));
  KEYINA_EXPECT_TRUE(keyina::IsValidVietnameseSyllable(U"mặt"));
  KEYINA_EXPECT_TRUE(!keyina::IsValidVietnameseSyllable(U"màt"));
  KEYINA_EXPECT_TRUE(!keyina::IsValidVietnameseSyllable(U"mảt"));
  KEYINA_EXPECT_TRUE(!keyina::IsValidVietnameseSyllable(U"mãt"));
}

KEYINA_TEST(accepts_gi_with_i_as_the_nucleus) {
  constexpr std::array<std::u32string_view, 6> valid = {
      U"gì", U"gỉ", U"gìn", U"gìn", U"gị", U"GÌ",
  };
  for (std::size_t index = 0; index < valid.size(); ++index) {
    if (!keyina::IsValidVietnameseSyllable(valid[index])) {
      throw std::runtime_error("gi nucleus syllable rejected at index " +
                               std::to_string(index));
    }
  }
}

KEYINA_TEST(rejects_more_than_one_tone_bearing_vowel) {
  KEYINA_EXPECT_TRUE(!keyina::IsValidVietnameseSyllable(U"óá"));
  KEYINA_EXPECT_TRUE(!keyina::IsValidVietnameseSyllable(U"ủỷ"));
  KEYINA_EXPECT_TRUE(!keyina::IsValidVietnameseSyllable(U"ệế"));
}

KEYINA_TEST(analyzer_reports_shared_structural_parts_and_precise_errors) {
  const auto valid = keyina::AnalyzeVietnameseSyllable(U"nghiêng");
  KEYINA_EXPECT_EQ(valid.status, keyina::SyllableStatus::Valid);
  KEYINA_EXPECT_EQ(valid.error, keyina::SyllableError::None);
  KEYINA_EXPECT_EQ(valid.onset, std::u32string_view{U"ngh"});
  KEYINA_EXPECT_EQ(valid.nucleus, std::u32string_view{U"iê"});
  KEYINA_EXPECT_EQ(valid.coda, std::u32string_view{U"ng"});
  KEYINA_EXPECT_EQ(valid.tone, keyina::Tone::None);

  const auto missing = keyina::AnalyzeVietnameseSyllable(U"tr");
  KEYINA_EXPECT_EQ(missing.status, keyina::SyllableStatus::RecoverablePrefix);
  KEYINA_EXPECT_EQ(missing.error, keyina::SyllableError::MissingNucleus);

  const auto bad_onset = keyina::AnalyzeVietnameseSyllable(U"zrường");
  KEYINA_EXPECT_EQ(bad_onset.status, keyina::SyllableStatus::Impossible);
  KEYINA_EXPECT_EQ(bad_onset.error, keyina::SyllableError::InvalidOnset);

  const auto bad_tone = keyina::AnalyzeVietnameseSyllable(U"màt");
  KEYINA_EXPECT_EQ(bad_tone.status, keyina::SyllableStatus::Impossible);
  KEYINA_EXPECT_EQ(bad_tone.error, keyina::SyllableError::InvalidTone);
}

KEYINA_TEST(classifies_foreign_shaped_tokens_as_ambiguous_without_hiding_garbage) {
  const auto foreign = keyina::AnalyzeVietnameseSyllable(U"café");
  KEYINA_EXPECT_EQ(foreign.status, keyina::SyllableStatus::Ambiguous);
  KEYINA_EXPECT_EQ(foreign.error, keyina::SyllableError::InvalidCoda);

  const auto repeated = keyina::AnalyzeVietnameseSyllable(U"hâhhâhhâhh");
  KEYINA_EXPECT_EQ(repeated.status, keyina::SyllableStatus::Impossible);
}

KEYINA_TEST(commit_boundary_does_not_autocorrect_ambiguous_tokens) {
  keyina::Engine engine({
      .tone_placement = keyina::TonePlacement::Modern,
      .application_bypass = false,
      .restore_invalid_word = true,
  });
  const auto rendered = TypeSequence(engine, U"cazees");
  KEYINA_EXPECT_TRUE(rendered != std::u32string{U"cazees"});

  const auto boundary = engine.Process(
      {keyina::KeyKind::CommitBoundary, U' ', false, false, false});
  KEYINA_EXPECT_EQ(boundary, keyina::TextEdit{});
}

KEYINA_TEST(selects_tone_offsets_from_orthographic_nucleus_rules) {
  KEYINA_EXPECT_EQ(keyina::SelectVietnameseToneOffset(U"oa", false, true),
                   std::size_t{1});
  KEYINA_EXPECT_EQ(keyina::SelectVietnameseToneOffset(U"oa", false, false),
                   std::size_t{0});
  KEYINA_EXPECT_EQ(keyina::SelectVietnameseToneOffset(U"oe", false, true),
                   std::size_t{1});
  KEYINA_EXPECT_EQ(keyina::SelectVietnameseToneOffset(U"uy", false, true),
                   std::size_t{1});
  KEYINA_EXPECT_EQ(keyina::SelectVietnameseToneOffset(U"iê", true, true),
                   std::size_t{1});
  KEYINA_EXPECT_EQ(keyina::SelectVietnameseToneOffset(U"ươ", true, true),
                   std::size_t{1});
  KEYINA_EXPECT_EQ(keyina::SelectVietnameseToneOffset(U"oai", false, true),
                   std::size_t{1});
  KEYINA_EXPECT_EQ(keyina::SelectVietnameseToneOffset(U"oai", true, true),
                   std::size_t{2});
  KEYINA_EXPECT_EQ(keyina::SelectVietnameseToneOffset(U"ae", false, true),
                   std::u32string_view::npos);
}

KEYINA_TEST(validates_broad_dictionary_derived_edge_corpus) {
  constexpr std::array<std::u32string_view, 28> valid = {
      U"ngoằn", U"khuya", U"khoẻ", U"khỏe", U"quẹo", U"giỏi",
      U"già", U"nghỉ", U"nghệ", U"oách", U"uỵch", U"huỳnh",
      U"khuynh", U"loãng", U"choắt", U"khoảnh", U"ngoảnh", U"thoáng",
      U"chuyển", U"nguyện", U"khuyên", U"tưởng", U"mượn", U"thuở",
      U"xoay", U"khuây", U"ngoáy", U"khuỷu",
  };
  for (std::size_t index = 0; index < valid.size(); ++index) {
    if (!keyina::IsValidVietnameseSyllable(valid[index])) {
      throw std::runtime_error("dictionary-derived syllable rejected at index " +
                               std::to_string(index));
    }
  }
}
