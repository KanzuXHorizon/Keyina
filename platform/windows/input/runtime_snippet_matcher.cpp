#include <keyina/windows/runtime_snippet_matcher.h>

#include <algorithm>
#include <array>
#include <chrono>
#include <cstdio>
#include <ctime>
#include <utility>

namespace keyina::windows {
namespace {

char32_t FoldAscii(char32_t value) noexcept {
  if (value >= U'A' && value <= U'Z') {
    return value - U'A' + U'a';
  }
  return value;
}

bool Equals(std::u32string_view left, std::u32string_view right,
            bool case_sensitive) noexcept {
  if (left.size() != right.size()) {
    return false;
  }
  for (std::size_t index = 0; index < left.size(); ++index) {
    if (case_sensitive) {
      if (left[index] != right[index]) {
        return false;
      }
    } else if (FoldAscii(left[index]) != FoldAscii(right[index])) {
      return false;
    }
  }
  return true;
}

bool StartsWith(std::u32string_view value, std::u32string_view prefix,
                bool case_sensitive) noexcept {
  if (prefix.size() > value.size()) {
    return false;
  }
  return Equals(value.substr(0, prefix.size()), prefix, case_sensitive);
}

bool Contains(std::u32string_view values, char32_t candidate) noexcept {
  return values.find(candidate) != std::u32string_view::npos;
}

bool ContainsHash(const std::vector<std::uint64_t>& values,
                  std::uint64_t candidate) noexcept {
  return std::find(values.begin(), values.end(), candidate) != values.end();
}

bool ScopeAllows(const RuntimeSnippetDefinition& definition,
                 std::uint64_t application_hash) noexcept {
  if (ContainsHash(definition.excluded_application_hashes,
                   application_hash)) {
    return false;
  }
  return definition.allowed_application_hashes.empty() ||
         ContainsHash(definition.allowed_application_hashes,
                      application_hash);
}

bool AppendTwoDigits(int value, std::u16string& output) {
  if (value < 0 || value > 99) {
    return false;
  }
  output.push_back(static_cast<char16_t>(u'0' + (value / 10)));
  output.push_back(static_cast<char16_t>(u'0' + (value % 10)));
  return true;
}

bool AppendFourDigits(int value, std::u16string& output) {
  if (value < 0 || value > 9999) {
    return false;
  }
  output.push_back(static_cast<char16_t>(u'0' + ((value / 1000) % 10)));
  output.push_back(static_cast<char16_t>(u'0' + ((value / 100) % 10)));
  output.push_back(static_cast<char16_t>(u'0' + ((value / 10) % 10)));
  output.push_back(static_cast<char16_t>(u'0' + (value % 10)));
  return true;
}

bool AppendDate(const RuntimeSnippetDateTime& now, std::u16string& output) {
  return AppendFourDigits(now.year, output) &&
         (output.push_back(u'-'), true) &&
         AppendTwoDigits(now.month, output) &&
         (output.push_back(u'-'), true) &&
         AppendTwoDigits(now.day, output);
}

bool AppendTime(const RuntimeSnippetDateTime& now, std::u16string& output) {
  return AppendTwoDigits(now.hour, output) &&
         (output.push_back(u':'), true) &&
         AppendTwoDigits(now.minute, output);
}

RuntimeSnippetDefinition CommandDefinition(
    std::u32string trigger, RuntimeSnippetCommand command) {
  RuntimeSnippetDefinition definition{};
  definition.trigger = std::move(trigger);
  definition.delimiters = U" \t\r\n";
  definition.command = command;
  definition.case_sensitive = true;
  definition.preserve_delimiter = false;
  return definition;
}

RuntimeSnippetDefinition TextDefinition(std::u32string trigger,
                                        std::u16string expansion) {
  RuntimeSnippetDefinition definition{};
  definition.trigger = std::move(trigger);
  definition.expansion = std::move(expansion);
  definition.delimiters = U" \t\r\n";
  definition.case_sensitive = true;
  definition.preserve_delimiter = true;
  return definition;
}

}  // namespace

RuntimeSnippetMatcher::RuntimeSnippetMatcher(
    const RuntimeSnippetProfile& profile) noexcept
    : profile_(&profile) {
  token_.reserve(kMaximumRuntimeSnippetTriggerCodepoints);
  failed_token_.reserve(kMaximumRuntimeSnippetTriggerCodepoints + 1);
  RebuildStartIndex();
}

void RuntimeSnippetMatcher::ApplyProfile(
    const RuntimeSnippetProfile& profile) noexcept {
  profile_ = &profile;
  Reset();
  RebuildStartIndex();
}

RuntimeSnippetMatch RuntimeSnippetMatcher::ProcessCharacter(
    char32_t character) {
  if (profile_ == nullptr || profile_->entries.empty()) {
    return {};
  }

  if (token_.empty()) {
    if (!CanStart(character)) {
      return {};
    }
    token_.push_back(character);
    if (HasViablePrefix(token_)) {
      return RuntimeSnippetMatch{
          RuntimeSnippetMatchStatus::Prefix,
          nullptr,
          token_,
          0,
      };
    }
    token_.clear();
    return {};
  }

  if (token_.size() >= kMaximumRuntimeSnippetTriggerCodepoints) {
    failed_token_ = token_;
    failed_token_.push_back(character);
    token_.clear();
    return RuntimeSnippetMatch{
        RuntimeSnippetMatchStatus::FailedCandidate,
        nullptr,
        failed_token_,
        0,
    };
  }

  token_.push_back(character);
  if (HasViablePrefix(token_)) {
    return RuntimeSnippetMatch{
        RuntimeSnippetMatchStatus::Prefix,
        nullptr,
        token_,
        0,
    };
  }

  failed_token_ = token_;
  token_.clear();
  return RuntimeSnippetMatch{
      RuntimeSnippetMatchStatus::FailedCandidate,
      nullptr,
      failed_token_,
      0,
  };
}

RuntimeSnippetMatch RuntimeSnippetMatcher::ProcessDelimiter(
    char32_t delimiter,
    std::uint64_t application_hash) noexcept {
  if (token_.empty()) {
    return {};
  }

  const RuntimeSnippetDefinition* definition =
      FindExact(token_, delimiter, application_hash);
  if (definition == nullptr) {
    failed_token_ = token_;
    token_.clear();
    return RuntimeSnippetMatch{
        RuntimeSnippetMatchStatus::FailedCandidate,
        nullptr,
        failed_token_,
        0,
    };
  }

  const auto erase_codepoints = static_cast<std::uint16_t>(token_.size());
  token_.clear();
  return RuntimeSnippetMatch{
      RuntimeSnippetMatchStatus::Match,
      definition,
      {},
      erase_codepoints,
  };
}

void RuntimeSnippetMatcher::ProcessBackspace() noexcept {
  if (!token_.empty()) {
    token_.pop_back();
  }
  failed_token_.clear();
}

void RuntimeSnippetMatcher::Reset() noexcept {
  token_.clear();
  failed_token_.clear();
}

void RuntimeSnippetMatcher::RebuildStartIndex() {
  ascii_starters_.fill(false);
  unicode_starters_.clear();
  if (profile_ == nullptr) {
    return;
  }
  unicode_starters_.reserve(profile_->entries.size());
  for (const auto& definition : profile_->entries) {
    if (definition.trigger.empty()) {
      continue;
    }
    const char32_t starter = definition.trigger.front();
    if (starter < ascii_starters_.size()) {
      ascii_starters_[static_cast<std::size_t>(starter)] = true;
      if (!definition.case_sensitive) {
        const char32_t folded = FoldAscii(starter);
        ascii_starters_[static_cast<std::size_t>(folded)] = true;
        if (folded >= U'a' && folded <= U'z') {
          ascii_starters_[static_cast<std::size_t>(
              folded - U'a' + U'A')] = true;
        }
      }
    } else if (std::find(
                   unicode_starters_.begin(),
                   unicode_starters_.end(),
                   starter) == unicode_starters_.end()) {
      unicode_starters_.push_back(starter);
    }
  }
}

bool RuntimeSnippetMatcher::CanStart(char32_t character) const noexcept {
  if (character < ascii_starters_.size()) {
    return ascii_starters_[static_cast<std::size_t>(character)];
  }
  return std::find(
             unicode_starters_.begin(),
             unicode_starters_.end(),
             character) != unicode_starters_.end();
}

bool RuntimeSnippetMatcher::HasViablePrefix(
    std::u32string_view token) const noexcept {
  if (profile_ == nullptr) {
    return false;
  }
  return std::any_of(
      profile_->entries.begin(),
      profile_->entries.end(),
      [token](const RuntimeSnippetDefinition& definition) {
        return StartsWith(definition.trigger, token,
                          definition.case_sensitive);
      });
}

const RuntimeSnippetDefinition* RuntimeSnippetMatcher::FindExact(
    std::u32string_view token,
    char32_t delimiter,
    std::uint64_t application_hash) const noexcept {
  if (profile_ == nullptr) {
    return nullptr;
  }
  for (const auto& definition : profile_->entries) {
    if (Equals(definition.trigger, token, definition.case_sensitive) &&
        Contains(definition.delimiters, delimiter) &&
        ScopeAllows(definition, application_hash)) {
      return &definition;
    }
  }
  return nullptr;
}

bool ExpandRuntimeSnippetTemplate(
    std::u16string_view input,
    const RuntimeSnippetDateTime& now,
    std::u16string& output) {
  output.clear();
  output.reserve(input.size() + 32);
  std::size_t index = 0;
  while (index < input.size()) {
    if (input[index] != u'$' || index + 1 >= input.size() ||
        input[index + 1] != u'{') {
      output.push_back(input[index++]);
      continue;
    }

    const auto end = input.find(u'}', index + 2);
    if (end == std::u16string_view::npos) {
      return false;
    }
    const auto variable = input.substr(index + 2, end - index - 2);
    if (variable == u"date") {
      if (!AppendDate(now, output)) {
        return false;
      }
    } else if (variable == u"time") {
      if (!AppendTime(now, output)) {
        return false;
      }
    } else if (variable == u"datetime") {
      if (!AppendDate(now, output)) {
        return false;
      }
      output.push_back(u' ');
      if (!AppendTime(now, output)) {
        return false;
      }
    } else {
      return false;
    }
    index = end + 1;
  }
  return true;
}

RuntimeSnippetDateTime CurrentRuntimeSnippetDateTime() noexcept {
  const auto now = std::chrono::system_clock::now();
  const std::time_t current = std::chrono::system_clock::to_time_t(now);
  std::tm local{};
#if defined(_WIN32)
  if (localtime_s(&local, &current) != 0) {
    return {};
  }
#else
  if (localtime_r(&current, &local) == nullptr) {
    return {};
  }
#endif
  return RuntimeSnippetDateTime{
      local.tm_year + 1900,
      local.tm_mon + 1,
      local.tm_mday,
      local.tm_hour,
      local.tm_min,
  };
}

RuntimeSnippetProfile DefaultRuntimeSnippetProfile() {
  RuntimeSnippetProfile profile{};
  profile.entries.reserve(5);
  profile.entries.push_back(CommandDefinition(
      U";kvi", RuntimeSnippetCommand::ToggleVietnamese));
  profile.entries.push_back(CommandDefinition(
      U";kvoice", RuntimeSnippetCommand::ToggleDictation));
  profile.entries.push_back(TextDefinition(U";kdate", u"${date}"));
  profile.entries.push_back(TextDefinition(U";ktime", u"${time}"));
  profile.entries.push_back(TextDefinition(
      U";kdatetime", u"${datetime}"));
  return profile;
}

}  // namespace keyina::windows
