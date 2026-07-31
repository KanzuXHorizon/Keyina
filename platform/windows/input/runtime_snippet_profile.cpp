#include <keyina/windows/runtime_snippet_profile.h>

#include <algorithm>
#include <cstddef>
#include <cstdint>
#include <limits>
#include <string_view>
#include <unordered_set>
#include <utility>

namespace keyina::windows {
namespace {

constexpr std::size_t kHeaderLength = 20;
constexpr std::size_t kEntryHeaderLength = 16;
constexpr std::size_t kChecksumOffset = 16;
constexpr std::uint8_t kFormatVersion = 1;
constexpr std::uint8_t kCaseSensitiveFlag = 1u << 0u;
constexpr std::uint8_t kPreserveDelimiterFlag = 1u << 1u;
constexpr std::uint8_t kKnownFlags =
    kCaseSensitiveFlag | kPreserveDelimiterFlag;

std::uint8_t ByteAt(std::span<const std::byte> bytes,
                    std::size_t index) noexcept {
  return std::to_integer<std::uint8_t>(bytes[index]);
}

std::uint16_t Read16(std::span<const std::byte> bytes,
                     std::size_t offset) noexcept {
  return static_cast<std::uint16_t>(ByteAt(bytes, offset)) |
         static_cast<std::uint16_t>(ByteAt(bytes, offset + 1)) << 8u;
}

std::uint32_t Read32(std::span<const std::byte> bytes,
                     std::size_t offset) noexcept {
  std::uint32_t value = 0;
  for (std::size_t index = 0; index < 4; ++index) {
    value |= static_cast<std::uint32_t>(ByteAt(bytes, offset + index))
             << (index * 8u);
  }
  return value;
}

std::uint64_t Read64(std::span<const std::byte> bytes,
                     std::size_t offset) noexcept {
  std::uint64_t value = 0;
  for (std::size_t index = 0; index < 8; ++index) {
    value |= static_cast<std::uint64_t>(ByteAt(bytes, offset + index))
             << (index * 8u);
  }
  return value;
}

std::uint32_t ComputeChecksum(std::span<const std::byte> bytes) noexcept {
  std::uint32_t hash = 2166136261u;
  for (std::size_t index = 0; index < bytes.size(); ++index) {
    if (index >= kChecksumOffset &&
        index < kChecksumOffset + sizeof(std::uint32_t)) {
      continue;
    }
    hash ^= ByteAt(bytes, index);
    hash *= 16777619u;
  }
  return hash;
}

bool AppendCodepoint(char32_t codepoint, std::u32string* utf32,
                     std::u16string* utf16) {
  if (utf32 != nullptr) {
    utf32->push_back(codepoint);
  }
  if (utf16 == nullptr) {
    return true;
  }
  if (codepoint <= 0xD7FF ||
      (codepoint >= 0xE000 && codepoint <= 0xFFFF)) {
    utf16->push_back(static_cast<char16_t>(codepoint));
    return true;
  }
  if (codepoint < 0x10000 || codepoint > 0x10FFFF) {
    return false;
  }
  const char32_t adjusted = codepoint - 0x10000;
  utf16->push_back(static_cast<char16_t>(0xD800 + (adjusted >> 10u)));
  utf16->push_back(static_cast<char16_t>(0xDC00 + (adjusted & 0x3FFu)));
  return true;
}

bool DecodeUtf8(std::span<const std::byte> bytes, std::u32string* utf32,
                std::u16string* utf16) {
  if (utf32 != nullptr) {
    utf32->clear();
    utf32->reserve(bytes.size());
  }
  if (utf16 != nullptr) {
    utf16->clear();
    utf16->reserve(bytes.size());
  }

  std::size_t index = 0;
  while (index < bytes.size()) {
    const std::uint8_t first = ByteAt(bytes, index++);
    char32_t codepoint = 0;
    std::size_t continuation_count = 0;
    char32_t minimum = 0;
    if (first <= 0x7F) {
      codepoint = first;
    } else if ((first & 0xE0u) == 0xC0u) {
      codepoint = first & 0x1Fu;
      continuation_count = 1;
      minimum = 0x80;
    } else if ((first & 0xF0u) == 0xE0u) {
      codepoint = first & 0x0Fu;
      continuation_count = 2;
      minimum = 0x800;
    } else if ((first & 0xF8u) == 0xF0u) {
      codepoint = first & 0x07u;
      continuation_count = 3;
      minimum = 0x10000;
    } else {
      return false;
    }

    if (bytes.size() - index < continuation_count) {
      return false;
    }
    for (std::size_t continuation = 0;
         continuation < continuation_count; ++continuation) {
      const std::uint8_t value = ByteAt(bytes, index++);
      if ((value & 0xC0u) != 0x80u) {
        return false;
      }
      codepoint = (codepoint << 6u) | (value & 0x3Fu);
    }
    if (codepoint < minimum || codepoint > 0x10FFFF ||
        (codepoint >= 0xD800 && codepoint <= 0xDFFF)) {
      return false;
    }
    if (!AppendCodepoint(codepoint, utf32, utf16)) {
      return false;
    }
  }
  return true;
}

bool IsAsciiAlphanumeric(char32_t value) noexcept {
  return (value >= U'A' && value <= U'Z') ||
         (value >= U'a' && value <= U'z') ||
         (value >= U'0' && value <= U'9');
}

bool ContainsAsciiWhitespace(std::u32string_view value) noexcept {
  return std::any_of(value.begin(), value.end(), [](char32_t character) {
    return character == U' ' || character == U'\t' ||
           character == U'\r' || character == U'\n' ||
           character == U'\f' || character == U'\v';
  });
}

bool IsSupportedVariable(std::u16string_view variable) noexcept {
  return variable == u"date" || variable == u"time" ||
         variable == u"datetime";
}

bool ValidateTemplate(std::u16string_view value) noexcept {
  std::size_t search = 0;
  while (search < value.size()) {
    const std::size_t start = value.find(u"${", search);
    if (start == std::u16string_view::npos) {
      return true;
    }
    const std::size_t end = value.find(u'}', start + 2);
    if (end == std::u16string_view::npos ||
        !IsSupportedVariable(value.substr(start + 2, end - start - 2))) {
      return false;
    }
    search = end + 1;
  }
  return true;
}

char32_t FoldAscii(char32_t value) noexcept {
  if (value >= U'A' && value <= U'Z') {
    return value - U'A' + U'a';
  }
  return value;
}

std::u32string NormalizeTrigger(std::u32string_view trigger) {
  std::u32string normalized;
  normalized.reserve(trigger.size());
  for (const char32_t character : trigger) {
    normalized.push_back(FoldAscii(character));
  }
  return normalized;
}

bool IsCommandValueValid(std::uint8_t raw) noexcept {
  return raw <= static_cast<std::uint8_t>(
                    RuntimeSnippetCommand::ToggleDictation);
}

RuntimeSnippetProfileResult Fail(RuntimeSnippetProfileError error) noexcept {
  RuntimeSnippetProfileResult result{};
  result.error = error;
  return result;
}

}  // namespace

RuntimeSnippetProfileResult DecodeRuntimeSnippetProfile(
    std::span<const std::byte> bytes) noexcept {
  try {
    if (bytes.size() < kHeaderLength ||
        bytes.size() > kMaximumRuntimeSnippetProfileBytes) {
      return Fail(RuntimeSnippetProfileError::InvalidLength);
    }
    if (ByteAt(bytes, 0) != 'K' || ByteAt(bytes, 1) != 'Y' ||
        ByteAt(bytes, 2) != 'S' || ByteAt(bytes, 3) != 'N') {
      return Fail(RuntimeSnippetProfileError::InvalidMagic);
    }
    if (ByteAt(bytes, 4) != kFormatVersion) {
      return Fail(RuntimeSnippetProfileError::UnsupportedVersion);
    }
    if (ByteAt(bytes, 5) != kHeaderLength || Read16(bytes, 6) != 0 ||
        Read32(bytes, 12) != bytes.size()) {
      return Fail(RuntimeSnippetProfileError::InvalidHeader);
    }
    if (Read32(bytes, kChecksumOffset) != ComputeChecksum(bytes)) {
      return Fail(RuntimeSnippetProfileError::InvalidChecksum);
    }

    const std::uint32_t entry_count = Read32(bytes, 8);
    if (entry_count > kMaximumRuntimeSnippetEntries) {
      return Fail(RuntimeSnippetProfileError::TooManyEntries);
    }

    RuntimeSnippetProfileResult result{};
    result.profile.entries.reserve(entry_count);
    std::unordered_set<std::u32string> normalized_triggers;
    normalized_triggers.reserve(entry_count);
    std::size_t offset = kHeaderLength;
    for (std::uint32_t entry_index = 0; entry_index < entry_count;
         ++entry_index) {
      if (bytes.size() - offset < kEntryHeaderLength) {
        return Fail(RuntimeSnippetProfileError::TruncatedEntry);
      }
      const auto header = bytes.subspan(offset, kEntryHeaderLength);
      offset += kEntryHeaderLength;
      const std::uint8_t flags = ByteAt(header, 0);
      const std::uint8_t command_raw = ByteAt(header, 1);
      if ((flags & ~kKnownFlags) != 0 || ByteAt(header, 2) != 0 ||
          ByteAt(header, 3) != 0 || !IsCommandValueValid(command_raw)) {
        return Fail(RuntimeSnippetProfileError::InvalidEntry);
      }

      const std::size_t delimiter_bytes = Read16(header, 4);
      const std::size_t trigger_bytes = Read16(header, 6);
      const std::size_t allowed_count = Read16(header, 8);
      const std::size_t excluded_count = Read16(header, 10);
      const std::size_t expansion_bytes = Read32(header, 12);
      if (delimiter_bytes == 0 || trigger_bytes == 0 ||
          expansion_bytes > kMaximumRuntimeSnippetExpansionUtf8Bytes) {
        return Fail(RuntimeSnippetProfileError::InvalidEntry);
      }
      if (allowed_count >
          (std::numeric_limits<std::size_t>::max() / sizeof(std::uint64_t)) -
              excluded_count) {
        return Fail(RuntimeSnippetProfileError::InvalidEntry);
      }
      const std::size_t hash_bytes =
          (allowed_count + excluded_count) * sizeof(std::uint64_t);
      if (delimiter_bytes > bytes.size() - offset ||
          trigger_bytes > bytes.size() - offset - delimiter_bytes ||
          expansion_bytes >
              bytes.size() - offset - delimiter_bytes - trigger_bytes ||
          hash_bytes > bytes.size() - offset - delimiter_bytes -
                           trigger_bytes - expansion_bytes) {
        return Fail(RuntimeSnippetProfileError::TruncatedEntry);
      }

      RuntimeSnippetDefinition definition{};
      if (!DecodeUtf8(bytes.subspan(offset, delimiter_bytes),
                      &definition.delimiters, nullptr)) {
        return Fail(RuntimeSnippetProfileError::InvalidUtf8);
      }
      offset += delimiter_bytes;
      if (!DecodeUtf8(bytes.subspan(offset, trigger_bytes),
                      &definition.trigger, nullptr)) {
        return Fail(RuntimeSnippetProfileError::InvalidUtf8);
      }
      offset += trigger_bytes;
      if (!DecodeUtf8(bytes.subspan(offset, expansion_bytes),
                      nullptr, &definition.expansion)) {
        return Fail(RuntimeSnippetProfileError::InvalidUtf8);
      }
      offset += expansion_bytes;

      definition.allowed_application_hashes.reserve(allowed_count);
      for (std::size_t index = 0; index < allowed_count; ++index) {
        definition.allowed_application_hashes.push_back(Read64(bytes, offset));
        offset += sizeof(std::uint64_t);
      }
      definition.excluded_application_hashes.reserve(excluded_count);
      for (std::size_t index = 0; index < excluded_count; ++index) {
        definition.excluded_application_hashes.push_back(Read64(bytes, offset));
        offset += sizeof(std::uint64_t);
      }

      definition.command = static_cast<RuntimeSnippetCommand>(command_raw);
      definition.case_sensitive = (flags & kCaseSensitiveFlag) != 0;
      definition.preserve_delimiter =
          (flags & kPreserveDelimiterFlag) != 0;
      if (definition.trigger.empty() ||
          definition.trigger.size() >
              kMaximumRuntimeSnippetTriggerCodepoints ||
          IsAsciiAlphanumeric(definition.trigger.front()) ||
          ContainsAsciiWhitespace(definition.trigger) ||
          definition.delimiters.empty() ||
          !ValidateTemplate(definition.expansion) ||
          (definition.command == RuntimeSnippetCommand::None &&
           definition.expansion.empty()) ||
          (definition.command != RuntimeSnippetCommand::None &&
           !definition.expansion.empty())) {
        return Fail(RuntimeSnippetProfileError::InvalidEntry);
      }

      if (!normalized_triggers.insert(
              NormalizeTrigger(definition.trigger)).second) {
        return Fail(RuntimeSnippetProfileError::DuplicateTrigger);
      }
      result.profile.entries.push_back(std::move(definition));
    }

    if (offset != bytes.size()) {
      return Fail(RuntimeSnippetProfileError::InvalidHeader);
    }
    return result;
  } catch (...) {
    return Fail(RuntimeSnippetProfileError::InvalidEntry);
  }
}

}  // namespace keyina::windows
