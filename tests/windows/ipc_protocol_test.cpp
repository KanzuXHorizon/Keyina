#include <array>
#include <cstddef>
#include <cstdint>
#include <fstream>
#include <string>
#include <string_view>
#include <vector>

#include <keyina/ipc_protocol.h>

#include "../test_support.h"

namespace {

#ifndef KEYINA_TEST_DATA_DIR
#error "KEYINA_TEST_DATA_DIR must point to the checked-in test data directory"
#endif

std::string LoadGoldenHex() {
  const std::string path =
      std::string{KEYINA_TEST_DATA_DIR} + "/ipc_frame_v1.hex";
  std::ifstream input(path, std::ios::binary);
  if (!input.is_open()) {
    throw std::runtime_error("Could not open shared IPC golden vector.");
  }
  std::string value;
  std::getline(input, value);
  if (!value.empty() && value.back() == '\r') {
    value.pop_back();
  }
  return value;
}

std::uint8_t HexDigit(char value) {
  if (value >= '0' && value <= '9') {
    return static_cast<std::uint8_t>(value - '0');
  }
  if (value >= 'A' && value <= 'F') {
    return static_cast<std::uint8_t>(10 + value - 'A');
  }
  throw std::invalid_argument("Invalid hexadecimal digit.");
}

std::vector<std::uint8_t> DecodeHex(std::string_view value) {
  if (value.size() % 2 != 0) {
    throw std::invalid_argument("Hexadecimal input must have even length.");
  }
  std::vector<std::uint8_t> output;
  output.reserve(value.size() / 2);
  for (std::size_t index = 0; index < value.size(); index += 2) {
    output.push_back(static_cast<std::uint8_t>(
        (HexDigit(value[index]) << 4U) | HexDigit(value[index + 1])));
  }
  return output;
}

keyina::ipc::Envelope GoldenEnvelope() {
  keyina::ipc::Envelope envelope;
  envelope.message_type = keyina::ipc::MessageType::FinalTranscript;
  envelope.flags = 0x1234;
  for (std::size_t index = 0; index < envelope.session_id.bytes.size(); ++index) {
    envelope.session_id.bytes[index] = static_cast<std::uint8_t>(index);
  }
  envelope.focus_generation = 0x0102030405060708ULL;
  envelope.payload = std::string{"xin ch\xC3\xA0" "o"};
  return envelope;
}

}  // namespace

KEYINA_TEST(cpp_ipc_matches_the_csharp_golden_frame) {
  const auto envelope = GoldenEnvelope();
  const auto expected = DecodeHex(LoadGoldenHex());
  const auto encoded = keyina::ipc::Encode(envelope);
  KEYINA_EXPECT_EQ(encoded, expected);

  const auto decoded = keyina::ipc::Decode(encoded);
  KEYINA_EXPECT_EQ(decoded.status, keyina::ipc::DecodeStatus::Success);
  KEYINA_EXPECT_EQ(decoded.error, keyina::ipc::DecodeError::None);
  KEYINA_EXPECT_EQ(decoded.consumed, encoded.size());
  KEYINA_EXPECT_TRUE(decoded.envelope.has_value());
  KEYINA_EXPECT_EQ(*decoded.envelope, envelope);
}

KEYINA_TEST(cpp_ipc_reports_partial_frames_without_consuming_data) {
  const auto encoded = keyina::ipc::Encode(GoldenEnvelope());
  constexpr std::array<std::size_t, 4> lengths = {
      0,
      1,
      keyina::ipc::kHeaderSize - 1,
      46,
  };
  for (const std::size_t length : lengths) {
    const auto decoded = keyina::ipc::Decode(
        std::span<const std::uint8_t>{encoded}.first(length));
    KEYINA_EXPECT_EQ(decoded.status, keyina::ipc::DecodeStatus::NeedMoreData);
    KEYINA_EXPECT_EQ(decoded.error, keyina::ipc::DecodeError::None);
    KEYINA_EXPECT_EQ(decoded.consumed, std::size_t{0});
    KEYINA_EXPECT_EQ(decoded.envelope, std::nullopt);
  }
}

KEYINA_TEST(cpp_ipc_rejects_invalid_headers_and_utf8) {
  auto invalid_magic = keyina::ipc::Encode(GoldenEnvelope());
  invalid_magic[0] = 0;
  KEYINA_EXPECT_EQ(
      keyina::ipc::Decode(invalid_magic).error,
      keyina::ipc::DecodeError::InvalidMagic);

  auto invalid_version = keyina::ipc::Encode(GoldenEnvelope());
  invalid_version[4] = 2;
  KEYINA_EXPECT_EQ(
      keyina::ipc::Decode(invalid_version).error,
      keyina::ipc::DecodeError::UnsupportedVersion);

  auto invalid_type = keyina::ipc::Encode(GoldenEnvelope());
  invalid_type[6] = 0xFF;
  KEYINA_EXPECT_EQ(
      keyina::ipc::Decode(invalid_type).error,
      keyina::ipc::DecodeError::UnknownMessageType);

  auto invalid_utf8 = keyina::ipc::Encode(GoldenEnvelope());
  invalid_utf8[invalid_utf8.size() - 2] = 0xC0;
  invalid_utf8.back() = 0xAF;
  KEYINA_EXPECT_EQ(
      keyina::ipc::Decode(invalid_utf8).error,
      keyina::ipc::DecodeError::InvalidUtf8);
}

KEYINA_TEST(cpp_ipc_rejects_oversized_payload_length) {
  auto oversized = keyina::ipc::Encode(GoldenEnvelope());
  oversized[10] = 0xFF;
  oversized[11] = 0xFF;
  oversized[12] = 0;
  oversized[13] = 0;
  const auto decoded = keyina::ipc::Decode(oversized);
  KEYINA_EXPECT_EQ(decoded.status, keyina::ipc::DecodeStatus::Invalid);
  KEYINA_EXPECT_EQ(decoded.error, keyina::ipc::DecodeError::FrameTooLarge);
}
