#include <cstddef>
#include <cstdint>
#include <fstream>
#include <optional>
#include <stdexcept>
#include <string>
#include <string_view>
#include <unordered_set>
#include <vector>

#include <keyina/context_guard.h>
#include <keyina/engine.h>

#include "test_support.h"

#ifndef KEYINA_TEST_DATA_DIR
#error "KEYINA_TEST_DATA_DIR must point to the checked-in test data directory"
#endif

namespace {

struct GoldenVector {
  std::u32string raw;
  std::u32string expected;
  std::u32string rollback;
  keyina::GuardReason guard_reason;
};

std::u32string DecodeUtf8Strict(std::string_view bytes) {
  std::u32string result;
  result.reserve(bytes.size());

  for (std::size_t index = 0; index < bytes.size();) {
    const auto first = static_cast<std::uint8_t>(bytes[index]);
    char32_t scalar = 0;
    std::size_t length = 0;
    if (first <= 0x7F) {
      scalar = first;
      length = 1;
    } else if ((first & 0xE0U) == 0xC0U) {
      scalar = first & 0x1FU;
      length = 2;
    } else if ((first & 0xF0U) == 0xE0U) {
      scalar = first & 0x0FU;
      length = 3;
    } else if ((first & 0xF8U) == 0xF0U) {
      scalar = first & 0x07U;
      length = 4;
    } else {
      throw std::runtime_error("invalid UTF-8 leading byte");
    }

    if (index + length > bytes.size()) {
      throw std::runtime_error("truncated UTF-8 sequence");
    }
    for (std::size_t offset = 1; offset < length; ++offset) {
      const auto continuation =
          static_cast<std::uint8_t>(bytes[index + offset]);
      if ((continuation & 0xC0U) != 0x80U) {
        throw std::runtime_error("invalid UTF-8 continuation byte");
      }
      scalar = (scalar << 6U) | (continuation & 0x3FU);
    }

    const bool overlong = (length == 2 && scalar < 0x80) ||
                          (length == 3 && scalar < 0x800) ||
                          (length == 4 && scalar < 0x10000);
    const bool surrogate = scalar >= 0xD800 && scalar <= 0xDFFF;
    if (overlong || surrogate || scalar > 0x10FFFF) {
      throw std::runtime_error("invalid UTF-8 scalar value");
    }
    result.push_back(scalar);
    index += length;
  }
  return result;
}

keyina::GuardReason ParseGuardReason(std::string_view value) {
  if (value == "None") return keyina::GuardReason::None;
  if (value == "Url") return keyina::GuardReason::Url;
  if (value == "Email") return keyina::GuardReason::Email;
  if (value == "FilePath") return keyina::GuardReason::FilePath;
  if (value == "Identifier") return keyina::GuardReason::Identifier;
  if (value == "VersionOrHash") return keyina::GuardReason::VersionOrHash;
  if (value == "ShellToken") return keyina::GuardReason::ShellToken;
  throw std::runtime_error("unknown guard reason");
}

std::optional<GoldenVector> ParseVectorLine(std::string_view line) {
  if (line.empty() || line.front() == '#') {
    return std::nullopt;
  }

  std::vector<std::string_view> fields;
  std::size_t start = 0;
  while (true) {
    const std::size_t tab = line.find('\t', start);
    fields.push_back(line.substr(start, tab - start));
    if (tab == std::string_view::npos) {
      break;
    }
    start = tab + 1;
  }
  if (fields.size() != 4 || fields[0].empty()) {
    throw std::runtime_error("golden vector requires four non-empty columns");
  }

  return GoldenVector{
      DecodeUtf8Strict(fields[0]),
      DecodeUtf8Strict(fields[1]),
      DecodeUtf8Strict(fields[2]),
      ParseGuardReason(fields[3]),
  };
}

std::string DescribeRaw(std::u32string_view raw) {
  std::string result;
  result.reserve(raw.size());
  for (const char32_t value : raw) {
    result.push_back(value <= 0x7F ? static_cast<char>(value) : '?');
  }
  return result;
}

std::u32string TypeVector(keyina::Engine& engine, std::u32string_view raw) {
  std::u32string external;
  for (const char32_t character : raw) {
    const auto edit = engine.Process(
        {keyina::KeyKind::Character, character, false, false, false});
    if (edit.commit_before) {
      throw std::runtime_error("golden vector exceeds active-token limit");
    }
    if (!edit.consumed || edit.erase_codepoints > external.size()) {
      throw std::runtime_error("invalid edit returned for golden vector");
    }
    external.erase(external.size() - edit.erase_codepoints);
    external.append(edit.insert);
  }
  return external;
}

}  // namespace

KEYINA_TEST(malformed_vector_rows_are_rejected) {
  bool too_few_fields = false;
  bool unknown_reason = false;
  try {
    static_cast<void>(ParseVectorLine("as\tá\tas"));
  } catch (const std::runtime_error&) {
    too_few_fields = true;
  }
  try {
    static_cast<void>(ParseVectorLine("as\tá\tas\tMystery"));
  } catch (const std::runtime_error&) {
    unknown_reason = true;
  }
  KEYINA_EXPECT_TRUE(too_few_fields);
  KEYINA_EXPECT_TRUE(unknown_reason);
}

KEYINA_TEST(checked_in_golden_vectors_match_engine_and_rollback) {
  const std::string path =
      std::string{KEYINA_TEST_DATA_DIR} + "/telex_vectors.tsv";
  std::ifstream input(path, std::ios::binary);
  KEYINA_EXPECT_TRUE(input.is_open());

  std::size_t vector_count = 0;
  std::size_t technical_guard_count = 0;
  std::unordered_set<std::u32string> seen_raw;
  std::string line;
  while (std::getline(input, line)) {
    if (!line.empty() && line.back() == '\r') {
      line.pop_back();
    }
    const auto vector = ParseVectorLine(line);
    if (!vector.has_value()) {
      continue;
    }
    ++vector_count;
    if (!seen_raw.insert(vector->raw).second) {
      throw std::runtime_error(
          "duplicate golden raw vector=" + DescribeRaw(vector->raw));
    }
    if (vector->guard_reason != keyina::GuardReason::None) {
      ++technical_guard_count;
    }

    keyina::Engine engine;
    const auto output = TypeVector(engine, vector->raw);
    if (output != vector->expected) {
      throw std::runtime_error(
          "golden output mismatch for raw=" + DescribeRaw(vector->raw));
    }
    if (engine.RawKeys() != std::u32string_view{vector->rollback}) {
      throw std::runtime_error(
          "golden rollback mismatch for raw=" + DescribeRaw(vector->raw));
    }
    if (keyina::ClassifyToken(vector->raw, {}).reason !=
        vector->guard_reason) {
      throw std::runtime_error(
          "golden guard mismatch for raw=" + DescribeRaw(vector->raw));
    }

    std::u32string rollback_output = output;
    while (!engine.RawKeys().empty()) {
      const auto edit = engine.Process(
          {keyina::KeyKind::Backspace, U'\0', false, false, false});
      KEYINA_EXPECT_TRUE(edit.consumed);
      KEYINA_EXPECT_TRUE(edit.erase_codepoints <= rollback_output.size());
      rollback_output.erase(rollback_output.size() - edit.erase_codepoints);
      rollback_output.append(edit.insert);
    }
    KEYINA_EXPECT_TRUE(rollback_output.empty());
  }

  KEYINA_EXPECT_TRUE(vector_count >= 220);
  KEYINA_EXPECT_TRUE(technical_guard_count >= 30);
}
