#pragma once

#include <cstddef>
#include <cstdint>
#include <span>
#include <string>
#include <vector>

namespace keyina::windows {

inline constexpr std::size_t kMaximumRuntimeSnippetProfileBytes =
    1024 * 1024;
inline constexpr std::size_t kMaximumRuntimeSnippetTriggerCodepoints = 64;
inline constexpr std::size_t kMaximumRuntimeSnippetExpansionUtf8Bytes =
    16 * 1024;
inline constexpr std::size_t kMaximumRuntimeSnippetEntries = 10'005;


enum class RuntimeSnippetCommand : std::uint8_t {
  None = 0,
  ToggleVietnamese = 1,
  ToggleDictation = 2,
  ExternalOutput = 3,
};

struct RuntimeSnippetDefinition {
  std::u32string trigger;
  std::u16string expansion;
  std::u32string delimiters;
  std::vector<std::uint64_t> allowed_application_hashes;
  std::vector<std::uint64_t> excluded_application_hashes;
  RuntimeSnippetCommand command{RuntimeSnippetCommand::None};
  bool case_sensitive{true};
  bool preserve_delimiter{false};
};

struct RuntimeSnippetProfile {
  std::vector<RuntimeSnippetDefinition> entries;
};

enum class RuntimeSnippetProfileError : std::uint8_t {
  None = 0,
  InvalidLength,
  InvalidMagic,
  UnsupportedVersion,
  InvalidHeader,
  InvalidChecksum,
  TooManyEntries,
  TruncatedEntry,
  InvalidUtf8,
  InvalidEntry,
  DuplicateTrigger,
};

struct RuntimeSnippetProfileResult {
  RuntimeSnippetProfile profile;
  RuntimeSnippetProfileError error{RuntimeSnippetProfileError::None};

  explicit operator bool() const noexcept {
    return error == RuntimeSnippetProfileError::None;
  }
};

[[nodiscard]] RuntimeSnippetProfileResult DecodeRuntimeSnippetProfile(
    std::span<const std::byte> bytes) noexcept;

}  // namespace keyina::windows
