#pragma once

#include <keyina/windows/keystroke_overlay_model.h>

#include <cstddef>
#include <cstdint>
#include <vector>

namespace keyina::windows {

class KeystrokeOverlaySoundPlayer {
 public:
  KeystrokeOverlaySoundPlayer() = default;
  ~KeystrokeOverlaySoundPlayer() = default;

  KeystrokeOverlaySoundPlayer(const KeystrokeOverlaySoundPlayer&) = delete;
  KeystrokeOverlaySoundPlayer& operator=(const KeystrokeOverlaySoundPlayer&) = delete;

  void Configure(const KeystrokeOverlayPreferences& preferences) noexcept;
  void Play(KeystrokeOverlayEventKind kind) noexcept;
  void Stop() noexcept;

  [[nodiscard]] bool enabled_for_testing() const noexcept { return enabled_; }
  [[nodiscard]] std::size_t dropped_for_testing() const noexcept { return dropped_; }

 private:
  static std::vector<std::byte> BuildWave(
      double frequency_hz,
      std::uint32_t duration_milliseconds,
      std::uint8_t volume_percent) noexcept;

  bool enabled_{false};
  std::uint8_t volume_percent_{30};
  std::vector<std::byte> token_wave_{};
  std::vector<std::byte> composition_wave_{};
  std::size_t dropped_{};
};

}  // namespace keyina::windows
