#include <array>
#include <string_view>

#include <keyina/context_guard.h>

#include "test_support.h"

namespace {

struct GuardCase {
  std::u32string_view token;
  bool transform;
  keyina::GuardReason reason;
};

}  // namespace

KEYINA_TEST(transforms_normal_vietnamese_tokens) {
  constexpr std::array<GuardCase, 4> cases = {{
      {U"xin", true, keyina::GuardReason::None},
      {U"chao", true, keyina::GuardReason::None},
      {U"duong", true, keyina::GuardReason::None},
      {U"tieng", true, keyina::GuardReason::None},
  }};

  for (const auto& test : cases) {
    const auto result = keyina::ClassifyToken(test.token, {});
    KEYINA_EXPECT_EQ(result.transform, test.transform);
    KEYINA_EXPECT_EQ(result.reason, test.reason);
  }
}

KEYINA_TEST(protects_technical_tokens_with_stable_reasons) {
  constexpr std::array<GuardCase, 10> cases = {{
      {U"https://example.com", false, keyina::GuardReason::Url},
      {U"www.example.com", false, keyina::GuardReason::Url},
      {U"name@example.com", false, keyina::GuardReason::Email},
      {U"C:\\Temp\\file.txt", false, keyina::GuardReason::FilePath},
      {U"../src/main.cpp", false, keyina::GuardReason::FilePath},
      {U"snake_case", false, keyina::GuardReason::Identifier},
      {U"camelCase", false, keyina::GuardReason::Identifier},
      {U"v2.3.1", false, keyina::GuardReason::VersionOrHash},
      {U"a3f48c91", false, keyina::GuardReason::VersionOrHash},
      {U"--help", false, keyina::GuardReason::ShellToken},
  }};

  for (const auto& test : cases) {
    const auto result = keyina::ClassifyToken(test.token, {});
    KEYINA_EXPECT_EQ(result.transform, test.transform);
    KEYINA_EXPECT_EQ(result.reason, test.reason);
  }
}

KEYINA_TEST(modifier_chords_and_app_bypass_take_explicit_paths) {
  keyina::GuardContext modifier_context{};
  modifier_context.modifier_chord = true;
  const auto modifier = keyina::ClassifyToken(U"xin", modifier_context);
  KEYINA_EXPECT_EQ(modifier.transform, false);
  KEYINA_EXPECT_EQ(modifier.reason, keyina::GuardReason::ModifierChord);

  keyina::GuardContext app_context{};
  app_context.application_bypass = true;
  const auto app = keyina::ClassifyToken(U"xin", app_context);
  KEYINA_EXPECT_EQ(app.transform, false);
  KEYINA_EXPECT_EQ(app.reason, keyina::GuardReason::ApplicationBypass);
}

KEYINA_TEST(specific_rules_win_over_generic_identifier_rules) {
  KEYINA_EXPECT_EQ(keyina::ClassifyToken(U"https://my_api/v2", {}).reason,
                   keyina::GuardReason::Url);
  KEYINA_EXPECT_EQ(keyina::ClassifyToken(U"first_last@example.com", {}).reason,
                   keyina::GuardReason::Email);
  KEYINA_EXPECT_EQ(keyina::ClassifyToken(U"C:\\snake_case\\v2.3.1", {}).reason,
                   keyina::GuardReason::FilePath);
}

KEYINA_TEST(empty_token_remains_transformable) {
  const auto result = keyina::ClassifyToken({}, {});
  KEYINA_EXPECT_EQ(result.transform, true);
  KEYINA_EXPECT_EQ(result.reason, keyina::GuardReason::None);
}
