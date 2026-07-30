#include <string>
#include <string_view>

#include <keyina/engine.h>

#include "test_support.h"

namespace {

void Apply(std::u32string& external, const keyina::TextEdit& edit) {
  KEYINA_EXPECT_TRUE(edit.erase_codepoints <= external.size());
  external.erase(external.size() - edit.erase_codepoints);
  external.append(edit.insert);
}

void TypeInto(keyina::Engine& engine, std::u32string& external,
              std::u32string_view raw) {
  for (const char32_t character : raw) {
    const auto edit = engine.Process(
        {keyina::KeyKind::Character, character, false, false, false});
    KEYINA_EXPECT_TRUE(edit.consumed);
    Apply(external, edit);
  }
}

}  // namespace

KEYINA_TEST(backspace_rebuilds_the_previous_composition_exactly) {
  keyina::Engine engine;
  std::u32string external;
  TypeInto(engine, external, U"tieengs");
  KEYINA_EXPECT_EQ(external, std::u32string{U"tiếng"});

  const auto remove_tone =
      engine.Process({keyina::KeyKind::Backspace, U'\0', false, false, false});
  KEYINA_EXPECT_TRUE(remove_tone.consumed);
  Apply(external, remove_tone);
  KEYINA_EXPECT_EQ(external, std::u32string{U"tiêng"});
  KEYINA_EXPECT_EQ(engine.RawKeys(), std::u32string_view{U"tieeng"});

  const auto remove_g =
      engine.Process({keyina::KeyKind::Backspace, U'\0', false, false, false});
  KEYINA_EXPECT_TRUE(remove_g.consumed);
  Apply(external, remove_g);
  KEYINA_EXPECT_EQ(external, std::u32string{U"tiên"});
}

KEYINA_TEST(backspace_then_retyping_modifier_reuses_the_rebuilt_composition) {
  keyina::Engine engine;
  std::u32string external;
  TypeInto(engine, external, U"nguyenx");
  KEYINA_EXPECT_EQ(external, std::u32string{U"nguyẽn"});

  const auto remove_tone =
      engine.Process({keyina::KeyKind::Backspace, U'\0', false, false, false});
  KEYINA_EXPECT_TRUE(remove_tone.consumed);
  Apply(external, remove_tone);
  KEYINA_EXPECT_EQ(external, std::u32string{U"nguyen"});

  TypeInto(engine, external, U"e");
  KEYINA_EXPECT_EQ(external, std::u32string{U"nguyên"});
  KEYINA_EXPECT_EQ(engine.RawKeys(), std::u32string_view{U"nguyene"});
}

KEYINA_TEST(backspace_through_a_modifier_restores_the_literal_letter) {
  keyina::Engine engine;
  std::u32string external;
  TypeInto(engine, external, U"aa");
  KEYINA_EXPECT_EQ(external, std::u32string{U"â"});

  auto edit =
      engine.Process({keyina::KeyKind::Backspace, U'\0', false, false, false});
  Apply(external, edit);
  KEYINA_EXPECT_EQ(external, std::u32string{U"a"});
  KEYINA_EXPECT_EQ(engine.RawKeys(), std::u32string_view{U"a"});

  edit = engine.Process(
      {keyina::KeyKind::Backspace, U'\0', false, false, false});
  Apply(external, edit);
  KEYINA_EXPECT_TRUE(external.empty());
  KEYINA_EXPECT_TRUE(engine.RawKeys().empty());
}

KEYINA_TEST(backspace_with_no_owned_composition_passes_through) {
  keyina::Engine engine;
  const auto edit =
      engine.Process({keyina::KeyKind::Backspace, U'\0', false, false, false});
  KEYINA_EXPECT_EQ(edit.consumed, false);
  KEYINA_EXPECT_EQ(edit.erase_codepoints, std::size_t{0});
  KEYINA_EXPECT_TRUE(edit.insert.empty());
}

KEYINA_TEST(context_transition_restores_raw_keys_in_one_edit) {
  keyina::Engine engine;
  std::u32string external;
  TypeInto(engine, external, U"as");
  KEYINA_EXPECT_EQ(external, std::u32string{U"á"});

  const auto edit = engine.Process(
      {keyina::KeyKind::Character, U'_', false, false, false});
  KEYINA_EXPECT_TRUE(edit.consumed);
  Apply(external, edit);

  KEYINA_EXPECT_EQ(external, std::u32string{U"as_"});
  KEYINA_EXPECT_EQ(engine.VisibleText(), std::u32string_view{U"as_"});
  KEYINA_EXPECT_EQ(engine.RawKeys(), std::u32string_view{U"as_"});
}

KEYINA_TEST(active_history_is_bounded_and_requests_a_safe_commit) {
  keyina::Engine engine;
  std::u32string external;
  TypeInto(engine, external, std::u32string(64, U'b'));
  KEYINA_EXPECT_EQ(engine.RawKeys().size(), std::size_t{64});

  const auto edit = engine.Process(
      {keyina::KeyKind::Character, U'c', false, false, false});

  KEYINA_EXPECT_TRUE(edit.consumed);
  KEYINA_EXPECT_TRUE(edit.commit_before);
  KEYINA_EXPECT_EQ(edit.erase_codepoints, std::size_t{0});
  KEYINA_EXPECT_EQ(edit.insert, std::u32string{U"c"});
  KEYINA_EXPECT_EQ(engine.RawKeys(), std::u32string_view{U"c"});
  KEYINA_EXPECT_EQ(engine.VisibleText(), std::u32string_view{U"c"});
}

KEYINA_TEST(reset_discards_internal_history_without_editing_application_text) {
  keyina::Engine engine;
  std::u32string external;
  TypeInto(engine, external, U"aaf");
  KEYINA_EXPECT_EQ(external, std::u32string{U"ầ"});

  const auto edit =
      engine.Process({keyina::KeyKind::Reset, U'\0', false, false, false});
  KEYINA_EXPECT_EQ(edit.consumed, false);
  KEYINA_EXPECT_TRUE(edit.insert.empty());
  KEYINA_EXPECT_TRUE(engine.RawKeys().empty());
  KEYINA_EXPECT_TRUE(engine.VisibleText().empty());
  KEYINA_EXPECT_EQ(external, std::u32string{U"ầ"});
}
