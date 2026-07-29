#include <keyina/ipc_protocol.h>

#include <algorithm>
#include <array>
#include <stdexcept>

namespace keyina::ipc {
namespace {

constexpr std::array<std::uint8_t, 4> kMagic = {
    static_cast<std::uint8_t>('K'),
    static_cast<std::uint8_t>('Y'),
    static_cast<std::uint8_t>('N'),
    static_cast<std::uint8_t>('A'),
};

constexpr void WriteU16(std::span<std::uint8_t> output,
                        std::uint16_t value) noexcept {
  output[0] = static_cast<std::uint8_t>(value & 0xFFU);
  output[1] = static_cast<std::uint8_t>((value >> 8U) & 0xFFU);
}

constexpr void WriteU32(std::span<std::uint8_t> output,
                        std::uint32_t value) noexcept {
  for (std::size_t index = 0; index < 4; ++index) {
    output[index] = static_cast<std::uint8_t>(
        (value >> static_cast<unsigned>(index * 8U)) & 0xFFU);
  }
}

constexpr void WriteU64(std::span<std::uint8_t> output,
                        std::uint64_t value) noexcept {
  for (std::size_t index = 0; index < 8; ++index) {
    output[index] = static_cast<std::uint8_t>(
        (value >> static_cast<unsigned>(index * 8U)) & 0xFFU);
  }
}

constexpr std::uint16_t ReadU16(
    std::span<const std::uint8_t> input) noexcept {
  return static_cast<std::uint16_t>(input[0]) |
         (static_cast<std::uint16_t>(input[1]) << 8U);
}

constexpr std::uint32_t ReadU32(
    std::span<const std::uint8_t> input) noexcept {
  std::uint32_t value = 0;
  for (std::size_t index = 0; index < 4; ++index) {
    value |= static_cast<std::uint32_t>(input[index])
             << static_cast<unsigned>(index * 8U);
  }
  return value;
}

constexpr std::uint64_t ReadU64(
    std::span<const std::uint8_t> input) noexcept {
  std::uint64_t value = 0;
  for (std::size_t index = 0; index < 8; ++index) {
    value |= static_cast<std::uint64_t>(input[index])
             << static_cast<unsigned>(index * 8U);
  }
  return value;
}

constexpr bool IsKnownMessageType(std::uint16_t raw) noexcept {
  return raw >= static_cast<std::uint16_t>(MessageType::Hello) &&
         raw <= static_cast<std::uint16_t>(MessageType::SnippetExpansion);
}

bool IsValidUtf8(std::span<const std::uint8_t> bytes) noexcept {
  std::size_t index = 0;
  while (index < bytes.size()) {
    const std::uint8_t first = bytes[index];
    if (first <= 0x7FU) {
      ++index;
      continue;
    }

    std::size_t length = 0;
    std::uint32_t scalar = 0;
    if ((first & 0xE0U) == 0xC0U) {
      length = 2;
      scalar = first & 0x1FU;
    } else if ((first & 0xF0U) == 0xE0U) {
      length = 3;
      scalar = first & 0x0FU;
    } else if ((first & 0xF8U) == 0xF0U) {
      length = 4;
      scalar = first & 0x07U;
    } else {
      return false;
    }

    if (index + length > bytes.size()) {
      return false;
    }
    for (std::size_t offset = 1; offset < length; ++offset) {
      const std::uint8_t continuation = bytes[index + offset];
      if ((continuation & 0xC0U) != 0x80U) {
        return false;
      }
      scalar = (scalar << 6U) | (continuation & 0x3FU);
    }

    const bool overlong =
        (length == 2 && scalar < 0x80U) ||
        (length == 3 && scalar < 0x800U) ||
        (length == 4 && scalar < 0x10000U);
    const bool surrogate = scalar >= 0xD800U && scalar <= 0xDFFFU;
    if (overlong || surrogate || scalar > 0x10FFFFU) {
      return false;
    }
    index += length;
  }
  return true;
}

DecodeResult Invalid(DecodeError error) noexcept {
  return DecodeResult{DecodeStatus::Invalid, error, 0, std::nullopt};
}

}  // namespace

std::vector<std::uint8_t> Encode(const Envelope& envelope) {
  const auto raw_type = static_cast<std::uint16_t>(envelope.message_type);
  if (!IsKnownMessageType(raw_type)) {
    throw std::invalid_argument("Unknown IPC message type.");
  }
  if (!IsValidUtf8(std::span<const std::uint8_t>{
          reinterpret_cast<const std::uint8_t*>(envelope.payload.data()),
          envelope.payload.size()})) {
    throw std::invalid_argument("IPC payload is not valid UTF-8.");
  }
  if (envelope.payload.size() > kMaximumPayloadBytes) {
    throw std::invalid_argument("IPC payload exceeds maximum frame size.");
  }

  std::vector<std::uint8_t> output(kHeaderSize + envelope.payload.size());
  std::copy(kMagic.begin(), kMagic.end(), output.begin());
  WriteU16(std::span<std::uint8_t>{output}.subspan(4, 2), kProtocolVersion);
  WriteU16(std::span<std::uint8_t>{output}.subspan(6, 2), raw_type);
  WriteU16(std::span<std::uint8_t>{output}.subspan(8, 2), envelope.flags);
  WriteU32(
      std::span<std::uint8_t>{output}.subspan(10, 4),
      static_cast<std::uint32_t>(envelope.payload.size()));
  std::copy(
      envelope.session_id.bytes.begin(),
      envelope.session_id.bytes.end(),
      output.begin() + 14);
  WriteU64(
      std::span<std::uint8_t>{output}.subspan(30, 8),
      envelope.focus_generation);
  std::copy(
      envelope.payload.begin(),
      envelope.payload.end(),
      output.begin() + static_cast<std::ptrdiff_t>(kHeaderSize));
  return output;
}

DecodeResult Decode(std::span<const std::uint8_t> buffer) noexcept {
  if (buffer.size() < kHeaderSize) {
    return {};
  }
  if (!std::equal(kMagic.begin(), kMagic.end(), buffer.begin())) {
    return Invalid(DecodeError::InvalidMagic);
  }
  if (ReadU16(buffer.subspan(4, 2)) != kProtocolVersion) {
    return Invalid(DecodeError::UnsupportedVersion);
  }

  const std::uint16_t raw_type = ReadU16(buffer.subspan(6, 2));
  if (!IsKnownMessageType(raw_type)) {
    return Invalid(DecodeError::UnknownMessageType);
  }

  const std::uint32_t payload_length = ReadU32(buffer.subspan(10, 4));
  if (payload_length > kMaximumPayloadBytes) {
    return Invalid(DecodeError::FrameTooLarge);
  }
  const std::size_t total_length =
      kHeaderSize + static_cast<std::size_t>(payload_length);
  if (buffer.size() < total_length) {
    return {};
  }

  const auto payload_bytes = buffer.subspan(kHeaderSize, payload_length);
  if (!IsValidUtf8(payload_bytes)) {
    return Invalid(DecodeError::InvalidUtf8);
  }

  Envelope envelope;
  envelope.message_type = static_cast<MessageType>(raw_type);
  envelope.flags = ReadU16(buffer.subspan(8, 2));
  std::copy_n(
      buffer.begin() + 14,
      envelope.session_id.bytes.size(),
      envelope.session_id.bytes.begin());
  envelope.focus_generation = ReadU64(buffer.subspan(30, 8));
  envelope.payload.assign(
      reinterpret_cast<const char*>(payload_bytes.data()),
      payload_bytes.size());
  return DecodeResult{
      DecodeStatus::Success,
      DecodeError::None,
      total_length,
      std::move(envelope),
  };
}

}  // namespace keyina::ipc
