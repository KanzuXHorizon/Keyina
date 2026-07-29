#include <array>
#include <cstddef>
#include <string>
#include <string_view>

#include <keyina/engine.h>

#include "test_support.h"

namespace {

void VerifySequence(std::u32string_view sequence) {
  keyina::Engine engine;
  std::u32string external;

  for (const char32_t character : sequence) {
    const auto edit = engine.Process(
        {keyina::KeyKind::Character, character, false, false, false});
    KEYINA_EXPECT_TRUE(edit.consumed);
    KEYINA_EXPECT_EQ(edit.commit_before, false);
    KEYINA_EXPECT_TRUE(edit.erase_codepoints <= external.size());
    external.erase(external.size() - edit.erase_codepoints);
    external.append(edit.insert);
    KEYINA_EXPECT_EQ(external, std::u32string{engine.VisibleText()});
    KEYINA_EXPECT_TRUE(engine.RawKeys().size() <= keyina::kMaxActiveKeys);
  }

  while (!engine.RawKeys().empty()) {
    const auto edit = engine.Process(
        {keyina::KeyKind::Backspace, U'\0', false, false, false});
    KEYINA_EXPECT_TRUE(edit.consumed);
    KEYINA_EXPECT_TRUE(edit.erase_codepoints <= external.size());
    external.erase(external.size() - edit.erase_codepoints);
    external.append(edit.insert);
    KEYINA_EXPECT_EQ(external, std::u32string{engine.VisibleText()});
  }
  KEYINA_EXPECT_TRUE(external.empty());

  engine.Reset();
  KEYINA_EXPECT_TRUE(engine.RawKeys().empty());
  KEYINA_EXPECT_TRUE(engine.VisibleText().empty());
}

void GenerateAndVerify(std::u32string& sequence, std::size_t remaining) {
  VerifySequence(sequence);
  if (remaining == 0) {
    return;
  }

  constexpr std::array<char32_t, 5> alphabet = {
      U'a', U'w', U's', U'd', U'b',
  };
  for (const char32_t character : alphabet) {
    sequence.push_back(character);
    GenerateAndVerify(sequence, remaining - 1);
    sequence.pop_back();
  }
}

}  // namespace

KEYINA_TEST(generated_sequences_preserve_edit_and_rollback_invariants) {
  std::u32string sequence;
  sequence.reserve(6);
  GenerateAndVerify(sequence, 6);
}

KEYINA_TEST(valid_unicode_scalars_do_not_break_engine_state) {
  constexpr std::array<char32_t, 8> scalars = {
      U'á', U'Đ', U'ư', U'漢', U'🙂', U'\u0301', U'ß', U'ø',
  };
  keyina::Engine engine;
  std::u32string external;
  for (const char32_t scalar : scalars) {
    const auto edit = engine.Process(
        {keyina::KeyKind::Character, scalar, false, false, false});
    KEYINA_EXPECT_TRUE(edit.erase_codepoints <= external.size());
    external.erase(external.size() - edit.erase_codepoints);
    external.append(edit.insert);
    KEYINA_EXPECT_EQ(external, std::u32string{engine.VisibleText()});
  }
}
