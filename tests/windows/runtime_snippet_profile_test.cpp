#include <keyina/windows/runtime_snippet_profile.h>

#include "../test_support.h"

#include <cstddef>
#include <cstdint>
#include <span>
#include <string>
#include <vector>

namespace {

using keyina::windows::RuntimeSnippetCommand;
using keyina::windows::RuntimeSnippetProfileError;

void Append16(std::vector<std::uint8_t>& bytes, std::uint16_t value) {
  bytes.push_back(static_cast<std::uint8_t>(value));
  bytes.push_back(static_cast<std::uint8_t>(value >> 8u));
}

void Append32(std::vector<std::uint8_t>& bytes, std::uint32_t value) {
  for (int shift = 0; shift < 32; shift += 8) {
    bytes.push_back(static_cast<std::uint8_t>(value >> shift));
  }
}

void AppendText(std::vector<std::uint8_t>& bytes, std::string_view value) {
  bytes.insert(bytes.end(), value.begin(), value.end());
}

void AppendEntry(std::vector<std::uint8_t>& bytes, std::uint8_t flags,
                 RuntimeSnippetCommand command, std::string_view delimiters,
                 std::string_view trigger, std::string_view expansion) {
  bytes.push_back(flags);
  bytes.push_back(static_cast<std::uint8_t>(command));
  bytes.push_back(0);
  bytes.push_back(0);
  Append16(bytes, static_cast<std::uint16_t>(delimiters.size()));
  Append16(bytes, static_cast<std::uint16_t>(trigger.size()));
  Append16(bytes, 0);
  Append16(bytes, 0);
  Append32(bytes, static_cast<std::uint32_t>(expansion.size()));
  AppendText(bytes, delimiters);
  AppendText(bytes, trigger);
  AppendText(bytes, expansion);
}

std::uint32_t ComputeChecksum(std::span<const std::uint8_t> bytes) {
  std::uint32_t hash = 2166136261u;
  for (std::size_t index = 0; index < bytes.size(); ++index) {
    if (index >= 16 && index < 20) {
      continue;
    }
    hash ^= bytes[index];
    hash *= 16777619u;
  }
  return hash;
}

void Write32(std::vector<std::uint8_t>& bytes, std::size_t offset,
             std::uint32_t value) {
  for (int shift = 0; shift < 32; shift += 8) {
    bytes[offset++] = static_cast<std::uint8_t>(value >> shift);
  }
}

std::vector<std::uint8_t> BuildProfile() {
  std::vector<std::uint8_t> bytes(20);
  AppendEntry(bytes, 0x01, RuntimeSnippetCommand::ToggleVietnamese,
              " \t\r\n", ";kvi", "");
  AppendEntry(bytes, 0x03, RuntimeSnippetCommand::None,
              " ", ";kdate", "${date}");
  bytes[0] = 'K';
  bytes[1] = 'Y';
  bytes[2] = 'S';
  bytes[3] = 'N';
  bytes[4] = 1;
  bytes[5] = 20;
  Write32(bytes, 8, 2);
  Write32(bytes, 12, static_cast<std::uint32_t>(bytes.size()));
  Write32(bytes, 16, ComputeChecksum(bytes));
  return bytes;
}

std::span<const std::byte> AsBytes(const std::vector<std::uint8_t>& bytes) {
  return std::as_bytes(std::span{bytes});
}

}  // namespace

KEYINA_TEST(runtime_snippet_profile_decodes_commands_variables_and_delimiters) {
  const auto bytes = BuildProfile();
  const auto result = keyina::windows::DecodeRuntimeSnippetProfile(AsBytes(bytes));

  KEYINA_EXPECT_EQ(result.error, RuntimeSnippetProfileError::None);
  KEYINA_EXPECT_EQ(result.profile.entries.size(), std::size_t{2});
  KEYINA_EXPECT_EQ(result.profile.entries[0].trigger, std::u32string{U";kvi"});
  KEYINA_EXPECT_EQ(result.profile.entries[0].command,
                   RuntimeSnippetCommand::ToggleVietnamese);
  KEYINA_EXPECT_TRUE(!result.profile.entries[0].preserve_delimiter);
  KEYINA_EXPECT_EQ(result.profile.entries[1].expansion,
                   std::u16string{u"${date}"});
  KEYINA_EXPECT_TRUE(result.profile.entries[1].preserve_delimiter);
}

KEYINA_TEST(runtime_snippet_profile_rejects_corruption) {
  auto bytes = BuildProfile();
  bytes.back() ^= 0x40;
  KEYINA_EXPECT_EQ(
      keyina::windows::DecodeRuntimeSnippetProfile(AsBytes(bytes)).error,
      RuntimeSnippetProfileError::InvalidChecksum);

  bytes = BuildProfile();
  bytes[4] = 2;
  Write32(bytes, 16, ComputeChecksum(bytes));
  KEYINA_EXPECT_EQ(
      keyina::windows::DecodeRuntimeSnippetProfile(AsBytes(bytes)).error,
      RuntimeSnippetProfileError::UnsupportedVersion);
}
