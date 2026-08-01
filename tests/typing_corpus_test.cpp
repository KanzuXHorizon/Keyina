#include <algorithm>
#include <cstddef>
#include <cstdint>
#include <filesystem>
#include <fstream>
#include <functional>
#include <optional>
#include <stdexcept>
#include <string>
#include <string_view>
#include <unordered_set>
#include <utility>
#include <vector>

#include <keyina/engine.h>

#include "test_support.h"

#ifndef KEYINA_TEST_DATA_DIR
#error "KEYINA_TEST_DATA_DIR must point to the checked-in test data directory"
#endif

namespace {

struct CorpusEvent {
  keyina::KeyEvent event;
  std::optional<char32_t> literal_if_unconsumed;
};

struct CorpusCase {
  std::string name;
  keyina::TonePlacement placement;
  bool restore_invalid_word;
  std::u32string script;
  std::u32string expected;
  std::u32string expected_active_raw;
};

std::u32string DecodeUtf8Strict(std::string_view bytes) {
  std::u32string result;
  result.reserve(bytes.size());

  for (std::size_t index = 0; index < bytes.size();) {
    const auto first = static_cast<std::uint8_t>(bytes[index]);
    char32_t scalar = 0;
    std::size_t length = 0;
    if (first <= 0x7FU) {
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

std::optional<std::uint32_t> HexDigit(char32_t value) noexcept {
  if (value >= U'0' && value <= U'9') {
    return static_cast<std::uint32_t>(value - U'0');
  }
  if (value >= U'a' && value <= U'f') {
    return static_cast<std::uint32_t>(value - U'a' + 10);
  }
  if (value >= U'A' && value <= U'F') {
    return static_cast<std::uint32_t>(value - U'A' + 10);
  }
  return std::nullopt;
}

char32_t ParseHexScalar(std::u32string_view value) {
  if (value.empty() || value.size() > 6) {
    throw std::runtime_error("invalid hexadecimal Unicode scalar");
  }

  std::uint32_t scalar = 0;
  for (const char32_t digit : value) {
    const auto decoded = HexDigit(digit);
    if (!decoded.has_value()) {
      throw std::runtime_error("invalid hexadecimal Unicode scalar");
    }
    scalar = (scalar << 4U) | *decoded;
  }
  if (scalar == 0 || scalar > 0x10FFFFU ||
      (scalar >= 0xD800U && scalar <= 0xDFFFU)) {
    throw std::runtime_error("invalid Unicode scalar value");
  }
  return static_cast<char32_t>(scalar);
}

std::vector<std::string_view> SplitFields(std::string_view line) {
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
  return fields;
}

std::optional<CorpusCase> ParseCorpusLine(std::string_view line) {
  if (line.empty() || line.front() == '#') {
    return std::nullopt;
  }

  auto fields = SplitFields(line);
  if (fields.size() == 5) {
    fields.emplace_back();
  }
  if (fields.size() != 6 || fields[0].empty()) {
    throw std::runtime_error(
        "typing corpus row requires five or six fields and a non-empty name");
  }

  keyina::TonePlacement placement;
  if (fields[1] == "Modern") {
    placement = keyina::TonePlacement::Modern;
  } else if (fields[1] == "Traditional") {
    placement = keyina::TonePlacement::Traditional;
  } else {
    throw std::runtime_error("unknown tone placement");
  }

  bool restore_invalid_word = false;
  if (fields[2] == "true") {
    restore_invalid_word = true;
  } else if (fields[2] != "false") {
    throw std::runtime_error("invalid restore_invalid_word value");
  }

  return CorpusCase{
      std::string{fields[0]},
      placement,
      restore_invalid_word,
      DecodeUtf8Strict(fields[3]),
      DecodeUtf8Strict(fields[4]),
      DecodeUtf8Strict(fields[5]),
  };
}

CorpusEvent BoundaryEvent(char32_t character) {
  return CorpusEvent{
      keyina::KeyEvent{
          keyina::KeyKind::CommitBoundary,
          character,
          false,
          false,
          false,
      },
      character,
  };
}

std::vector<CorpusEvent> DecodeEventScript(std::u32string_view script) {
  std::vector<CorpusEvent> events;
  events.reserve(script.size());

  for (std::size_t index = 0; index < script.size();) {
    const char32_t character = script[index];
    if (character == U' ' || character == U'\t' || character == U'\n') {
      throw std::runtime_error(
          "typing script whitespace must use an explicit boundary token");
    }
    if (character == U'}') {
      if (index + 1 < script.size() && script[index + 1] == U'}') {
        events.push_back(CorpusEvent{
            keyina::KeyEvent{
                keyina::KeyKind::Character,
                U'}',
                false,
                false,
                false,
            },
            std::nullopt,
        });
        index += 2;
        continue;
      }
      throw std::runtime_error("unmatched closing brace in typing script");
    }
    if (character != U'{') {
      events.push_back(CorpusEvent{
          keyina::KeyEvent{
              keyina::KeyKind::Character,
              character,
              false,
              false,
              false,
          },
          std::nullopt,
      });
      ++index;
      continue;
    }

    if (index + 1 < script.size() && script[index + 1] == U'{') {
      events.push_back(CorpusEvent{
          keyina::KeyEvent{
              keyina::KeyKind::Character,
              U'{',
              false,
              false,
              false,
          },
          std::nullopt,
      });
      index += 2;
      continue;
    }

    const std::size_t close = script.find(U'}', index + 1);
    if (close == std::u32string_view::npos) {
      throw std::runtime_error("unterminated control token in typing script");
    }
    const auto token = script.substr(index + 1, close - index - 1);
    if (token.empty()) {
      throw std::runtime_error("empty control token in typing script");
    }

    if (token == U"SPACE") {
      events.push_back(BoundaryEvent(U' '));
    } else if (token == U"TAB") {
      events.push_back(BoundaryEvent(U'\t'));
    } else if (token == U"ENTER") {
      events.push_back(BoundaryEvent(U'\n'));
    } else if (token == U"BS") {
      events.push_back(CorpusEvent{
          keyina::KeyEvent{
              keyina::KeyKind::Backspace,
              U'\0',
              false,
              false,
              false,
          },
          std::nullopt,
      });
    } else if (token == U"RESET") {
      events.push_back(CorpusEvent{
          keyina::KeyEvent{
              keyina::KeyKind::Reset,
              U'\0',
              false,
              false,
              false,
          },
          std::nullopt,
      });
    } else if (token.starts_with(U"B:")) {
      events.push_back(BoundaryEvent(ParseHexScalar(token.substr(2))));
    } else if (token.starts_with(U"L:")) {
      events.push_back(CorpusEvent{
          keyina::KeyEvent{
              keyina::KeyKind::Reset,
              U'\0',
              false,
              false,
              false,
          },
          ParseHexScalar(token.substr(2)),
      });
    } else {
      throw std::runtime_error("unknown control token in typing script");
    }
    index = close + 1;
  }

  return events;
}

std::vector<std::u32string> LoadGoldenRawVectors(
    const std::filesystem::path& path) {
  std::ifstream input(path, std::ios::binary);
  if (!input.is_open()) {
    throw std::runtime_error("could not open golden vector corpus");
  }

  std::vector<std::u32string> raw_vectors;
  std::string line;
  std::size_t line_number = 0;
  while (std::getline(input, line)) {
    ++line_number;
    if (!line.empty() && line.back() == '\r') {
      line.pop_back();
    }
    if (line.empty() || line.front() == '#') {
      continue;
    }
    const auto fields = SplitFields(line);
    if (fields.size() != 4 || fields[0].empty()) {
      throw std::runtime_error(
          "invalid golden vector at line " + std::to_string(line_number));
    }
    raw_vectors.push_back(DecodeUtf8Strict(fields[0]));
  }
  return raw_vectors;
}

std::vector<CorpusCase> LoadCorpusCases(const std::filesystem::path& path) {
  std::ifstream input(path, std::ios::binary);
  if (!input.is_open()) {
    throw std::runtime_error("could not open typing corpus");
  }

  std::vector<CorpusCase> cases;
  std::unordered_set<std::string> names;
  std::string line;
  std::size_t line_number = 0;
  while (std::getline(input, line)) {
    ++line_number;
    if (!line.empty() && line.back() == '\r') {
      line.pop_back();
    }
    try {
      auto parsed = ParseCorpusLine(line);
      if (!parsed.has_value()) {
        continue;
      }
      if (!names.insert(parsed->name).second) {
        throw std::runtime_error("duplicate typing corpus case name");
      }
      static_cast<void>(DecodeEventScript(parsed->script));
      cases.push_back(std::move(*parsed));
    } catch (const std::runtime_error& error) {
      throw std::runtime_error(
          "typing corpus line " + std::to_string(line_number) + ": " +
          error.what());
    }
  }
  return cases;
}

void ApplyEdit(std::u32string& external, const keyina::TextEdit& edit) {
  if (edit.erase_codepoints > external.size()) {
    throw std::runtime_error("typing corpus edit erases beyond external text");
  }
  external.erase(external.size() - edit.erase_codepoints);
  external.append(edit.insert);
}

std::u32string TypeRaw(keyina::Engine& engine, std::u32string_view raw) {
  std::u32string external;
  for (const char32_t character : raw) {
    const auto edit = engine.Process({
        keyina::KeyKind::Character,
        character,
        false,
        false,
        false,
    });
    if (edit.commit_before) {
      throw std::runtime_error("raw vector exceeded active composition");
    }
    ApplyEdit(external, edit);
  }
  return external;
}

bool EndsWith(std::u32string_view value, std::u32string_view suffix) {
  return suffix.size() <= value.size() &&
         value.substr(value.size() - suffix.size()) == suffix;
}

std::string DescribeScalar(std::optional<char32_t> value) {
  if (!value.has_value()) {
    return "<end>";
  }
  constexpr char kHex[] = "0123456789ABCDEF";
  std::uint32_t scalar = static_cast<std::uint32_t>(*value);
  std::string result = "U+";
  const int digits = scalar <= 0xFFFFU ? 4 : 6;
  for (int shift = (digits - 1) * 4; shift >= 0; shift -= 4) {
    result.push_back(kHex[(scalar >> shift) & 0xFU]);
  }
  return result;
}

std::string DescribeDifference(std::u32string_view expected,
                               std::u32string_view actual) {
  const std::size_t shared = std::min(expected.size(), actual.size());
  std::size_t index = 0;
  while (index < shared && expected[index] == actual[index]) {
    ++index;
  }
  const auto expected_scalar = index < expected.size()
      ? std::optional<char32_t>{expected[index]}
      : std::nullopt;
  const auto actual_scalar = index < actual.size()
      ? std::optional<char32_t>{actual[index]}
      : std::nullopt;
  return "scalar " + std::to_string(index) + ", expected " +
         DescribeScalar(expected_scalar) + ", actual " +
         DescribeScalar(actual_scalar);
}

void ReplayCorpusCase(const CorpusCase& test) {
  keyina::Engine engine({
      .tone_placement = test.placement,
      .application_bypass = false,
      .restore_invalid_word = test.restore_invalid_word,
  });
  std::u32string external;
  const auto events = DecodeEventScript(test.script);

  std::size_t event_index = 0;
  for (const auto& item : events) {
    const auto edit = engine.Process(item.event);
    ApplyEdit(external, edit);
    if (!edit.consumed && item.literal_if_unconsumed.has_value()) {
      external.push_back(*item.literal_if_unconsumed);
    }

    if (engine.RawKeys().size() > keyina::kMaxActiveKeys) {
      throw std::runtime_error(
          "typing corpus case exceeded the active-key bound: " + test.name);
    }
    if (!engine.RawKeys().empty() &&
        !EndsWith(external, engine.VisibleText())) {
      throw std::runtime_error(
          "external text diverged from active composition: " + test.name);
    }
    if (item.event.kind == keyina::KeyKind::CommitBoundary ||
        item.event.kind == keyina::KeyKind::Reset) {
      if (!engine.RawKeys().empty() || !engine.VisibleText().empty()) {
        throw std::runtime_error(
            "boundary did not reset composition: " + test.name);
      }
    }
    ++event_index;
  }

  if (external != test.expected) {
    throw std::runtime_error(
        "typing corpus output mismatch: " + test.name + ", " +
        DescribeDifference(test.expected, external) + ", events=" +
        std::to_string(event_index));
  }
  if (engine.RawKeys() !=
      std::u32string_view{test.expected_active_raw}) {
    throw std::runtime_error("typing corpus raw-state mismatch: " + test.name);
  }
  if (!engine.RawKeys().empty() &&
      !EndsWith(test.expected, engine.VisibleText())) {
    throw std::runtime_error(
        "typing corpus visible-state mismatch: " + test.name);
  }
}

bool Throws(const std::function<void()>& action) {
  try {
    action();
    return false;
  } catch (const std::exception&) {
    return true;
  }
}

}  // namespace

KEYINA_TEST(typing_corpus_rejects_malformed_rows_and_scripts) {
  KEYINA_EXPECT_TRUE(Throws([] {
    static_cast<void>(ParseCorpusLine("missing-columns"));
  }));
  KEYINA_EXPECT_TRUE(Throws([] {
    static_cast<void>(ParseCorpusLine(
        "bad-placement\tFuture\ttrue\ta\ta\ta"));
  }));
  KEYINA_EXPECT_TRUE(Throws([] {
    static_cast<void>(DecodeEventScript(U"abc{UNKNOWN}"));
  }));
  KEYINA_EXPECT_TRUE(Throws([] {
    static_cast<void>(DecodeEventScript(U"abc{SPACE"));
  }));
}

KEYINA_TEST(typing_corpus_decodes_generic_boundary_and_literal_tokens) {
  const auto events = DecodeEventScript(U"a{B:002C}{L:1F642}");
  KEYINA_EXPECT_EQ(events.size(), std::size_t{3});
  KEYINA_EXPECT_EQ(events[0].event.kind, keyina::KeyKind::Character);
  KEYINA_EXPECT_EQ(events[0].event.character, U'a');
  KEYINA_EXPECT_EQ(events[1].event.kind, keyina::KeyKind::CommitBoundary);
  KEYINA_EXPECT_EQ(events[1].event.character, U',');
  KEYINA_EXPECT_EQ(events[1].literal_if_unconsumed, std::optional<char32_t>{U','});
  KEYINA_EXPECT_EQ(events[2].event.kind, keyina::KeyKind::Reset);
  KEYINA_EXPECT_EQ(events[2].literal_if_unconsumed,
                   std::optional<char32_t>{U'🙂'});

  KEYINA_EXPECT_TRUE(Throws([] {
    static_cast<void>(DecodeEventScript(U"{B:D800}"));
  }));
  KEYINA_EXPECT_TRUE(Throws([] {
    static_cast<void>(DecodeEventScript(U"{L:110000}"));
  }));
  KEYINA_EXPECT_TRUE(Throws([] {
    static_cast<void>(DecodeEventScript(U"{B:xyz}"));
  }));
}

KEYINA_TEST(checked_in_typing_corpus_replays_exact_text_and_state) {
  const auto cases = LoadCorpusCases(
      std::filesystem::path{KEYINA_TEST_DATA_DIR} / "typing_sequences.tsv");
  KEYINA_EXPECT_TRUE(cases.size() >= 20);

  std::size_t modern_count = 0;
  std::size_t traditional_count = 0;
  bool has_modern_pair = false;
  bool has_traditional_pair = false;
  for (const auto& test : cases) {
    ReplayCorpusCase(test);
    modern_count += test.placement == keyina::TonePlacement::Modern ? 1 : 0;
    traditional_count +=
        test.placement == keyina::TonePlacement::Traditional ? 1 : 0;
    has_modern_pair = has_modern_pair || test.name == "modern_tone_placement";
    has_traditional_pair =
        has_traditional_pair || test.name == "traditional_tone_placement";
  }
  KEYINA_EXPECT_TRUE(modern_count >= 19);
  KEYINA_EXPECT_TRUE(traditional_count >= 1);
  KEYINA_EXPECT_TRUE(has_modern_pair);
  KEYINA_EXPECT_TRUE(has_traditional_pair);
}

KEYINA_TEST(tab_and_enter_are_explicit_non_destructive_boundaries) {
  ReplayCorpusCase(CorpusCase{
      "tab-and-enter-boundaries",
      keyina::TonePlacement::Modern,
      true,
      U"xin{TAB}chaof{ENTER}tieeps{SPACE}",
      U"xin\tchào\ntiếp ",
      {},
  });
}

KEYINA_TEST(backspace_matches_fresh_replay_for_the_expanded_corpus) {
  const auto raw_vectors = LoadGoldenRawVectors(
      std::filesystem::path{KEYINA_TEST_DATA_DIR} / "telex_vectors.tsv");
  std::size_t participating = 0;

  for (const auto& raw : raw_vectors) {
    if (raw.empty() || raw.size() > keyina::kMaxActiveKeys) {
      continue;
    }
    ++participating;

    for (const bool restore_invalid_word : {false, true}) {
      const keyina::EngineConfig config{
          .restore_invalid_word = restore_invalid_word,
      };
      keyina::Engine engine(config);
      std::u32string external = TypeRaw(engine, raw);

      for (std::size_t remaining = raw.size(); remaining > 0; --remaining) {
        const auto edit = engine.Process({
            keyina::KeyKind::Backspace,
            U'\0',
            false,
            false,
            false,
        });
        if (!edit.consumed) {
          throw std::runtime_error(
              "Backspace was not consumed for raw=" +
              DescribeScalar(raw.front()));
        }
        ApplyEdit(external, edit);

        keyina::Engine replay(config);
        const auto prefix = std::u32string_view{raw}.substr(0, remaining - 1);
        const std::u32string replay_external = TypeRaw(replay, prefix);
        if (external != replay_external ||
            engine.RawKeys() != replay.RawKeys() ||
            engine.VisibleText() != replay.VisibleText()) {
          throw std::runtime_error(
              "Backspace replay mismatch at remaining=" +
              std::to_string(remaining - 1) + ", " +
              DescribeDifference(replay_external, external));
        }
      }
    }
  }

  KEYINA_EXPECT_TRUE(participating >= 220);
}

KEYINA_TEST(long_mixed_stream_preserves_exact_output_and_state) {
  constexpr std::u32string_view phrase =
      U"tooi{SPACE}ddang{SPACE}research{SPACE}Keyina{SPACE}vaf{SPACE}"
      U"kieemr{SPACE}tra{SPACE}powershell{SPACE}";
  constexpr std::u32string_view expected_phrase =
      U"tôi đang research Keyina và kiểm tra powershell ";

  std::u32string script;
  std::u32string expected;
  for (std::size_t index = 0; index < 50; ++index) {
    script.append(phrase);
    expected.append(expected_phrase);
  }
  KEYINA_EXPECT_TRUE(DecodeEventScript(script).size() > 2'000);
  ReplayCorpusCase(CorpusCase{
      "generated-long-mixed-stream",
      keyina::TonePlacement::Modern,
      true,
      std::move(script),
      std::move(expected),
      {},
  });
}
