#include <keyina/windows/keystroke_overlay_sound.h>

#include <windows.h>
#include <mmsystem.h>

#include <algorithm>
#include <cmath>
#include <cstring>
#include <limits>

namespace keyina::windows {
namespace {

constexpr std::uint32_t kSampleRate = 22050;
constexpr std::uint16_t kChannels = 1;
constexpr std::uint16_t kBitsPerSample = 16;
constexpr DWORD kPlaybackFlags =
    SND_ASYNC | SND_MEMORY | SND_NODEFAULT | SND_NOSTOP | SND_SYSTEM;

void WriteU16(std::vector<std::byte>& bytes, std::size_t offset,
              std::uint16_t value) noexcept {
  bytes[offset] = static_cast<std::byte>(value & 0xFFu);
  bytes[offset + 1] = static_cast<std::byte>((value >> 8u) & 0xFFu);
}

void WriteU32(std::vector<std::byte>& bytes, std::size_t offset,
              std::uint32_t value) noexcept {
  bytes[offset] = static_cast<std::byte>(value & 0xFFu);
  bytes[offset + 1] = static_cast<std::byte>((value >> 8u) & 0xFFu);
  bytes[offset + 2] = static_cast<std::byte>((value >> 16u) & 0xFFu);
  bytes[offset + 3] = static_cast<std::byte>((value >> 24u) & 0xFFu);
}

}  // namespace

void KeystrokeOverlaySoundPlayer::Configure(
    const KeystrokeOverlayPreferences& preferences) noexcept {
  const bool enable = preferences.enabled &&
      preferences.per_key_sound_enabled &&
      preferences.sound_volume_percent > 0;
  if (!enable) {
    Stop();
    enabled_ = false;
    token_wave_.clear();
    composition_wave_.clear();
    return;
  }
  if (enabled_ && volume_percent_ == preferences.sound_volume_percent &&
      !token_wave_.empty() && !composition_wave_.empty()) {
    return;
  }
  volume_percent_ = preferences.sound_volume_percent;
  token_wave_ = BuildWave(1040.0, 9, volume_percent_);
  composition_wave_ = BuildWave(760.0, 13, volume_percent_);
  enabled_ = !token_wave_.empty() && !composition_wave_.empty();
}

void KeystrokeOverlaySoundPlayer::Play(
    KeystrokeOverlayEventKind kind) noexcept {
  if (!enabled_) {
    return;
  }
  const std::vector<std::byte>* wave = nullptr;
  switch (kind) {
    case KeystrokeOverlayEventKind::Token:
      wave = &token_wave_;
      break;
    case KeystrokeOverlayEventKind::CompositionUpdated:
    case KeystrokeOverlayEventKind::CompositionCommitted:
      wave = &composition_wave_;
      break;
    case KeystrokeOverlayEventKind::Cleared:
    case KeystrokeOverlayEventKind::Suppressed:
      return;
  }
  if (wave == nullptr || wave->empty() ||
      PlaySoundW(reinterpret_cast<LPCWSTR>(wave->data()), nullptr,
                 kPlaybackFlags) == FALSE) {
    ++dropped_;
  }
}

void KeystrokeOverlaySoundPlayer::Stop() noexcept {
  PlaySoundW(nullptr, nullptr, 0);
}

std::vector<std::byte> KeystrokeOverlaySoundPlayer::BuildWave(
    double frequency_hz,
    std::uint32_t duration_milliseconds,
    std::uint8_t volume_percent) noexcept {
  try {
    const auto sample_count = std::max<std::uint32_t>(
        1, (kSampleRate * duration_milliseconds) / 1000u);
    const std::uint32_t data_bytes = sample_count * sizeof(std::int16_t);
    std::vector<std::byte> bytes(44u + data_bytes);
    std::memcpy(bytes.data(), "RIFF", 4);
    WriteU32(bytes, 4, 36u + data_bytes);
    std::memcpy(bytes.data() + 8, "WAVEfmt ", 8);
    WriteU32(bytes, 16, 16);
    WriteU16(bytes, 20, 1);
    WriteU16(bytes, 22, kChannels);
    WriteU32(bytes, 24, kSampleRate);
    WriteU32(bytes, 28, kSampleRate * sizeof(std::int16_t));
    WriteU16(bytes, 32, sizeof(std::int16_t));
    WriteU16(bytes, 34, kBitsPerSample);
    std::memcpy(bytes.data() + 36, "data", 4);
    WriteU32(bytes, 40, data_bytes);

    const double amplitude = 5200.0 *
        (static_cast<double>(volume_percent) / 100.0);
    constexpr double kTwoPi = 6.28318530717958647692;
    for (std::uint32_t index = 0; index < sample_count; ++index) {
      const double progress = static_cast<double>(index) / sample_count;
      const double envelope = (1.0 - progress) * (1.0 - progress);
      const double phase = kTwoPi * frequency_hz * index / kSampleRate;
      const auto sample = static_cast<std::int16_t>(std::clamp(
          std::sin(phase) * amplitude * envelope,
          static_cast<double>(std::numeric_limits<std::int16_t>::min()),
          static_cast<double>(std::numeric_limits<std::int16_t>::max())));
      WriteU16(bytes, 44u + index * sizeof(std::int16_t),
               static_cast<std::uint16_t>(sample));
    }
    return bytes;
  } catch (...) {
    return {};
  }
}

}  // namespace keyina::windows
