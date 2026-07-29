#pragma once

#include <array>
#include <cstddef>
#include <cstdint>
#include <optional>
#include <span>
#include <string>
#include <vector>

namespace keyina::ipc {

inline constexpr std::uint16_t kProtocolVersion = 1;
inline constexpr std::size_t kHeaderSize = 38;
inline constexpr std::size_t kMaximumFrameBytes = 64U * 1024U;
inline constexpr std::size_t kMaximumPayloadBytes =
    kMaximumFrameBytes - kHeaderSize;

enum class MessageType : std::uint16_t {
  Hello = 1,
  BeginDictation = 2,
  PartialTranscript = 3,
  FinalTranscript = 4,
  EndDictation = 5,
  ToggleInput = 6,
  ConfigurationChanged = 7,
  SnippetExpansion = 8,
};

enum class DecodeStatus {
  Success,
  NeedMoreData,
  Invalid,
};

enum class DecodeError {
  None,
  InvalidMagic,
  UnsupportedVersion,
  UnknownMessageType,
  FrameTooLarge,
  InvalidUtf8,
};

struct SessionId {
  std::array<std::uint8_t, 16> bytes{};

  friend constexpr bool operator==(const SessionId&,
                                   const SessionId&) = default;
};

struct Envelope {
  MessageType message_type{MessageType::Hello};
  std::uint16_t flags{};
  SessionId session_id{};
  std::uint64_t focus_generation{};
  std::string payload;

  friend bool operator==(const Envelope&, const Envelope&) = default;
};

struct DecodeResult {
  DecodeStatus status{DecodeStatus::NeedMoreData};
  DecodeError error{DecodeError::None};
  std::size_t consumed{};
  std::optional<Envelope> envelope;
};

[[nodiscard]] std::vector<std::uint8_t> Encode(const Envelope& envelope);

[[nodiscard]] DecodeResult Decode(
    std::span<const std::uint8_t> buffer) noexcept;

}  // namespace keyina::ipc
