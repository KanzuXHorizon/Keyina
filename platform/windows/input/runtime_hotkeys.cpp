#include <keyina/windows/runtime_hotkeys.h>

namespace keyina::windows {
namespace {

std::uint8_t EventModifiers(const PhysicalKeyEvent& event) noexcept {
  std::uint8_t modifiers = 0;
  if (event.control) {
    modifiers |= 0x01;
  }
  if (event.shift) {
    modifiers |= 0x02;
  }
  if (event.alt) {
    modifiers |= 0x04;
  }
  if (event.windows) {
    modifiers |= 0x08;
  }
  return modifiers;
}

RuntimeCommand CommandForBinding(std::size_t index) noexcept {
  switch (index) {
    case 1:
      return RuntimeCommand::PushToTalkPressed;
    case 2:
      return RuntimeCommand::ToggleDictation;
    case 3:
      return RuntimeCommand::TranslateSelection;
    case 4:
      return RuntimeCommand::UndoTranslation;
    case 5:
      return RuntimeCommand::CancelActiveCommand;
    default:
      return RuntimeCommand::None;
  }
}

}  // namespace

bool RuntimeHotkeyRouter::KeyStateSet::Get(std::uint16_t key) const noexcept {
  if (key >= 256) {
    return false;
  }
  const std::size_t segment = key >> 6;
  const std::uint64_t mask = std::uint64_t{1} << (key & 63u);
  return (segments_[segment] & mask) != 0;
}

void RuntimeHotkeyRouter::KeyStateSet::Set(
    std::uint16_t key, bool value) noexcept {
  if (key >= 256) {
    return;
  }
  const std::size_t segment = key >> 6;
  const std::uint64_t mask = std::uint64_t{1} << (key & 63u);
  if (value) {
    segments_[segment] |= mask;
  } else {
    segments_[segment] &= ~mask;
  }
}

void RuntimeHotkeyRouter::KeyStateSet::Clear() noexcept {
  segments_.fill(0);
}

RuntimeHotkeyDecision RuntimeHotkeyRouter::Process(
    const PhysicalKeyEvent& event,
    const RuntimeInputProfile& profile,
    bool key_repeat,
    bool command_companion_active) noexcept {
  const std::uint8_t modifiers = EventModifiers(event);
  if (hold_key_ != 0 && event.virtual_key != hold_key_ &&
      (modifiers & profile.hotkeys[1].modifiers) !=
          profile.hotkeys[1].modifiers) {
    hold_key_ = 0;
    return RuntimeHotkeyDecision{
        RuntimeCommand::PushToTalkReleased,
        event.virtual_key,
        false,
    };
  }

  if (!event.key_down) {
    if (!suppressed_keys_.Get(event.virtual_key)) {
      return {};
    }
    suppressed_keys_.Set(event.virtual_key, false);
    const bool release_hold = event.virtual_key == hold_key_;
    if (release_hold) {
      hold_key_ = 0;
    }
    return RuntimeHotkeyDecision{
        release_hold ? RuntimeCommand::PushToTalkReleased
                     : RuntimeCommand::None,
        event.virtual_key,
        true,
    };
  }

  if (key_repeat) {
    return RuntimeHotkeyDecision{
        RuntimeCommand::None,
        event.virtual_key,
        suppressed_keys_.Get(event.virtual_key),
    };
  }

  for (std::size_t index = 1; index < profile.hotkeys.size(); ++index) {
    const auto& binding = profile.hotkeys[index];
    if (binding.virtual_key != event.virtual_key ||
        binding.modifiers != modifiers) {
      continue;
    }
    if ((index == 4 || index == 5) && !command_companion_active) {
      return {};
    }

    const RuntimeCommand command = CommandForBinding(index);
    if (command == RuntimeCommand::None) {
      return {};
    }
    suppressed_keys_.Set(event.virtual_key, true);
    if (index == 1) {
      hold_key_ = event.virtual_key;
    }
    return RuntimeHotkeyDecision{
        command,
        event.virtual_key,
        true,
    };
  }
  return {};
}

void RuntimeHotkeyRouter::CancelSuppression(
    std::uint16_t virtual_key) noexcept {
  suppressed_keys_.Set(virtual_key, false);
  if (hold_key_ == virtual_key) {
    hold_key_ = 0;
  }
}

void RuntimeHotkeyRouter::Reset() noexcept {
  suppressed_keys_.Clear();
  hold_key_ = 0;
}

const wchar_t* RuntimeCommandArgument(RuntimeCommand command) noexcept {
  switch (command) {
    case RuntimeCommand::SetVietnameseEnabled:
      return L"--companion-command=set-vietnamese-enabled";
    case RuntimeCommand::SetVietnameseDisabled:
      return L"--companion-command=set-vietnamese-disabled";
    case RuntimeCommand::PushToTalkPressed:
      return L"--companion-command=push-to-talk-pressed";
    case RuntimeCommand::PushToTalkReleased:
      return L"--companion-command=push-to-talk-released";
    case RuntimeCommand::ToggleDictation:
      return L"--companion-command=toggle-dictation";
    case RuntimeCommand::TranslateSelection:
      return L"--companion-command=translate-selection";
    case RuntimeCommand::UndoTranslation:
      return L"--companion-command=undo-translation";
    case RuntimeCommand::CancelActiveCommand:
      return L"--companion-command=cancel-active-command";
    case RuntimeCommand::None:
    default:
      return nullptr;
  }
}

}  // namespace keyina::windows
