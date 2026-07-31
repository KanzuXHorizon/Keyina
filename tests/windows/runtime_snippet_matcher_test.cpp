#include <keyina/windows/runtime_snippet_matcher.h>

#include "../test_support.h"

#include <cstdint>
#include <string>

namespace {

using keyina::windows::RuntimeSnippetCommand;
using keyina::windows::RuntimeSnippetDateTime;
using keyina::windows::RuntimeSnippetDefinition;
using keyina::windows::RuntimeSnippetMatchStatus;
using keyina::windows::RuntimeSnippetMatcher;
using keyina::windows::RuntimeSnippetProfile;

RuntimeSnippetDefinition Definition(
    std::u32string trigger, std::u16string expansion,
    bool preserve_delimiter = false,
    RuntimeSnippetCommand command = RuntimeSnippetCommand::None,
    bool case_sensitive = true) {
  RuntimeSnippetDefinition definition{};
  definition.trigger = std::move(trigger);
  definition.expansion = std::move(expansion);
  definition.delimiters = U" \t\r\n";
  definition.case_sensitive = case_sensitive;
  definition.preserve_delimiter = preserve_delimiter;
  definition.command = command;
  return definition;
}

}  // namespace

KEYINA_TEST(runtime_snippet_matcher_tracks_raw_prefix_before_telex) {
  RuntimeSnippetProfile profile{};
  profile.entries.push_back(Definition(
      U";aws", u"literal", false, RuntimeSnippetCommand::None));
  RuntimeSnippetMatcher matcher(profile);

  KEYINA_EXPECT_EQ(matcher.ProcessCharacter(U';').status,
                   RuntimeSnippetMatchStatus::Prefix);
  KEYINA_EXPECT_EQ(matcher.ProcessCharacter(U'a').status,
                   RuntimeSnippetMatchStatus::Prefix);
  KEYINA_EXPECT_EQ(matcher.ProcessCharacter(U'w').status,
                   RuntimeSnippetMatchStatus::Prefix);
  KEYINA_EXPECT_EQ(matcher.ProcessCharacter(U's').status,
                   RuntimeSnippetMatchStatus::Prefix);
  const auto match = matcher.ProcessDelimiter(U' ', 0);
  KEYINA_EXPECT_EQ(match.status, RuntimeSnippetMatchStatus::Match);
  KEYINA_EXPECT_EQ(match.erase_codepoints, std::uint16_t{4});
  KEYINA_EXPECT_EQ(match.definition->expansion, std::u16string{u"literal"});
}

KEYINA_TEST(runtime_snippet_matcher_reports_failed_candidate_for_telex_replay) {
  RuntimeSnippetProfile profile{};
  profile.entries.push_back(Definition(U";aws", u"literal"));
  RuntimeSnippetMatcher matcher(profile);

  (void)matcher.ProcessCharacter(U';');
  (void)matcher.ProcessCharacter(U'a');
  (void)matcher.ProcessCharacter(U'w');
  const auto failed = matcher.ProcessCharacter(U'q');

  KEYINA_EXPECT_EQ(failed.status, RuntimeSnippetMatchStatus::FailedCandidate);
  KEYINA_EXPECT_EQ(failed.token, std::u32string_view{U";awq"});
}

KEYINA_TEST(runtime_snippet_matcher_respects_case_delimiter_and_application_scope) {
  RuntimeSnippetProfile profile{};
  auto definition = Definition(U";Case", u"value", true,
                               RuntimeSnippetCommand::None, false);
  definition.delimiters = U" ";
  definition.allowed_application_hashes.push_back(42);
  definition.excluded_application_hashes.push_back(99);
  profile.entries.push_back(std::move(definition));
  RuntimeSnippetMatcher matcher(profile);

  for (const auto character : std::u32string_view{U";case"}) {
    KEYINA_EXPECT_EQ(matcher.ProcessCharacter(character).status,
                     RuntimeSnippetMatchStatus::Prefix);
  }
  KEYINA_EXPECT_EQ(matcher.ProcessDelimiter(U'\t', 42).status,
                   RuntimeSnippetMatchStatus::FailedCandidate);

  matcher.Reset();
  for (const auto character : std::u32string_view{U";case"}) {
    (void)matcher.ProcessCharacter(character);
  }
  KEYINA_EXPECT_EQ(matcher.ProcessDelimiter(U' ', 99).status,
                   RuntimeSnippetMatchStatus::FailedCandidate);

  matcher.Reset();
  for (const auto character : std::u32string_view{U";case"}) {
    (void)matcher.ProcessCharacter(character);
  }
  KEYINA_EXPECT_EQ(matcher.ProcessDelimiter(U' ', 42).status,
                   RuntimeSnippetMatchStatus::Match);
}

KEYINA_TEST(runtime_snippet_matcher_keeps_candidate_after_backspace) {
  RuntimeSnippetProfile profile{};
  profile.entries.push_back(Definition(U";kvi", u"value"));
  RuntimeSnippetMatcher matcher(profile);

  (void)matcher.ProcessCharacter(U';');
  (void)matcher.ProcessCharacter(U'k');
  (void)matcher.ProcessCharacter(U'v');
  matcher.ProcessBackspace();
  KEYINA_EXPECT_EQ(matcher.ProcessCharacter(U'v').status,
                   RuntimeSnippetMatchStatus::Prefix);
  KEYINA_EXPECT_EQ(matcher.ProcessCharacter(U'i').status,
                   RuntimeSnippetMatchStatus::Prefix);
  KEYINA_EXPECT_EQ(matcher.ProcessDelimiter(U' ', 0).status,
                   RuntimeSnippetMatchStatus::Match);
}

KEYINA_TEST(runtime_snippet_variables_expand_at_activation_time) {
  std::u16string output;
  KEYINA_EXPECT_TRUE(keyina::windows::ExpandRuntimeSnippetTemplate(
      u"${date} ${time} ${datetime}",
      RuntimeSnippetDateTime{2026, 7, 31, 17, 24}, output));
  KEYINA_EXPECT_EQ(output,
                   std::u16string{u"2026-07-31 17:24 2026-07-31 17:24"});
}
