#pragma once

#include <keyina/windows/runtime_snippet_profile.h>

#include <array>
#include <cstdint>
#include <string>
#include <string_view>
#include <vector>

namespace keyina::windows {

enum class RuntimeSnippetMatchStatus : std::uint8_t {
  None = 0,
  Prefix,
  FailedCandidate,
  Match,
};

struct RuntimeSnippetDateTime {
  int year{};
  int month{};
  int day{};
  int hour{};
  int minute{};
};

struct RuntimeSnippetMatch {
  RuntimeSnippetMatchStatus status{RuntimeSnippetMatchStatus::None};
  const RuntimeSnippetDefinition* definition{nullptr};
  std::u32string_view token;
  std::uint16_t erase_codepoints{};
};

class RuntimeSnippetMatcher {
 public:
  explicit RuntimeSnippetMatcher(
      const RuntimeSnippetProfile& profile) noexcept;

  void ApplyProfile(const RuntimeSnippetProfile& profile) noexcept;
  [[nodiscard]] RuntimeSnippetMatch ProcessCharacter(char32_t character);
  [[nodiscard]] RuntimeSnippetMatch ProcessDelimiter(
      char32_t delimiter,
      std::uint64_t application_hash) noexcept;
  void ProcessBackspace() noexcept;
  void Reset() noexcept;

  [[nodiscard]] bool active() const noexcept { return !token_.empty(); }
  [[nodiscard]] std::u32string_view token() const noexcept { return token_; }
  [[nodiscard]] std::vector<const RuntimeSnippetDefinition*> Suggestions(
      std::size_t maximum) const;

 private:
  void RebuildStartIndex();
  [[nodiscard]] bool CanStart(char32_t character) const noexcept;
  [[nodiscard]] bool HasViablePrefix(std::u32string_view token) const noexcept;
  [[nodiscard]] const RuntimeSnippetDefinition* FindExact(
      std::u32string_view token,
      char32_t delimiter,
      std::uint64_t application_hash) const noexcept;

  const RuntimeSnippetProfile* profile_{nullptr};
  std::u32string token_;
  std::u32string failed_token_;
  std::array<bool, 128> ascii_starters_{};
  std::vector<char32_t> unicode_starters_;
};

[[nodiscard]] bool ExpandRuntimeSnippetTemplate(
    std::u16string_view input,
    const RuntimeSnippetDateTime& now,
    std::u16string& output);
[[nodiscard]] RuntimeSnippetDateTime CurrentRuntimeSnippetDateTime() noexcept;
[[nodiscard]] RuntimeSnippetProfile DefaultRuntimeSnippetProfile();

}  // namespace keyina::windows
