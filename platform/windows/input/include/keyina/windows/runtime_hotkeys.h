#pragma once

#include <keyina/windows/resident_input_controller.h>
#include <keyina/windows/runtime_profile.h>

#include <array>
#include <cstdint>

namespace keyina::windows {

enum class RuntimeCommand : std::uint8_t {
  None = 0,
  SetVietnameseEnabled,
  SetVietnameseDisabled,
  PushToTalkPressed,
  PushToTalkReleased,
  ToggleDictation,
  TranslateSelection,
  UndoTranslation,
  CancelActiveCommand,
};

struct RuntimeHotkeyDecision {
  RuntimeCommand command{RuntimeCommand::None};
  std::uint16_t virtual_key{};
  bool suppress{false};
};

class RuntimeHotkeyRouter {
 public:
  [[nodiscard]] RuntimeHotkeyDecision Process(
      const PhysicalKeyEvent& event,
      const RuntimeInputProfile& profile,
      bool key_repeat,
      bool command_companion_active) noexcept;

  void CancelSuppression(std::uint16_t virtual_key) noexcept;
  void Reset() noexcept;

 private:
  class KeyStateSet {
   public:
    [[nodiscard]] bool Get(std::uint16_t key) const noexcept;
    void Set(std::uint16_t key, bool value) noexcept;
    void Clear() noexcept;

   private:
    std::array<std::uint64_t, 4> segments_{};
  };

  KeyStateSet suppressed_keys_{};
  std::uint16_t hold_key_{};
};

[[nodiscard]] const wchar_t* RuntimeCommandArgument(
    RuntimeCommand command) noexcept;

}  // namespace keyina::windows
