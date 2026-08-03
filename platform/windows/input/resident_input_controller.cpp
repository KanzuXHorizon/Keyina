#include <keyina/windows/resident_input_controller.h>

#include <keyina/input_character_classification.h>

#include <algorithm>
#include <cstddef>
#include <cstdint>
#include <limits>
#include <utility>

namespace keyina::windows {
namespace {

constexpr std::uint16_t kBackspace = 0x08;
constexpr std::uint16_t kTab = 0x09;
constexpr std::uint16_t kEnter = 0x0D;
constexpr std::uint16_t kSpace = 0x20;

char32_t DelimiterForEvent(
    const PhysicalKeyEvent& event,
    InputCharacterClass character_class) noexcept {
  switch (event.virtual_key) {
    case kSpace:
      return U' ';
    case kTab:
      return U'\t';
    case kEnter:
      return U'\n';
    default:
      return character_class == InputCharacterClass::CommitBoundary
                 ? event.character
                 : U'\0';
  }
}

EngineConfig ToEngineConfig(const RuntimeInputProfile& profile) noexcept {
  return EngineConfig{
      .tone_placement = profile.traditional_tone_placement
                            ? TonePlacement::Traditional
                            : TonePlacement::Modern,
      .application_bypass = false,
      .restore_invalid_word = profile.restore_invalid_word,
      .quick_telex_letters = profile.quick_telex_letters,
      .standalone_w_to_u_horn = profile.standalone_w_to_u_horn,
  };
}

bool IsResetBoundary(std::uint16_t virtual_key) noexcept {
  switch (virtual_key) {
    case 0x09:  // Tab
    case 0x0D:  // Enter
    case 0x1B:  // Escape
    case 0x21:  // Page Up
    case 0x22:  // Page Down
    case 0x23:  // End
    case 0x24:  // Home
    case 0x25:  // Left
    case 0x26:  // Up
    case 0x27:  // Right
    case 0x28:  // Down
    case 0x2D:  // Insert
    case 0x2E:  // Delete
      return true;
    default:
      return false;
  }
}

bool IsLiteralPassThrough(const TextEditView& edit,
                          char32_t physical_character) noexcept {
  return edit.erase_codepoints == 0 && edit.insert.size() == 1 &&
         edit.insert.front() == physical_character;
}

bool AppendUtf16(char32_t codepoint, InputDecision& decision) noexcept {
  if (codepoint <= 0xD7FF ||
      (codepoint >= 0xE000 && codepoint <= 0xFFFF)) {
    if (decision.insert_units >= decision.insert.size()) {
      return false;
    }
    decision.insert[decision.insert_units++] = static_cast<wchar_t>(codepoint);
    return true;
  }
  if (codepoint < 0x10000 || codepoint > 0x10FFFF ||
      decision.insert_units + 2 > decision.insert.size()) {
    return false;
  }

  const auto adjusted = codepoint - 0x10000;
  decision.insert[decision.insert_units++] =
      static_cast<wchar_t>(0xD800 + (adjusted >> 10));
  decision.insert[decision.insert_units++] =
      static_cast<wchar_t>(0xDC00 + (adjusted & 0x3FF));
  return true;
}

}  // namespace

ResidentInputController::ResidentInputController(
    RuntimeInputProfile profile,
    RuntimeSnippetProfile snippets)
    : profile_(profile),
      engine_(ToEngineConfig(profile)),
      snippet_profile_(std::move(snippets)),
      snippet_matcher_(snippet_profile_) {
  snippet_insert_buffer_.reserve(
      kMaximumRuntimeSnippetExpansionUtf8Bytes + 2);
}

void ResidentInputController::ApplyProfile(const RuntimeInputProfile& profile) {
  profile_ = profile;
  engine_ = Engine(ToEngineConfig(profile_));
  has_context_ = false;
  pointer_observation_required_ = false;
  suppressed_keys_.Clear();
  snippet_matcher_.Reset();
}

void ResidentInputController::ApplySnippets(RuntimeSnippetProfile snippets) {
  snippet_profile_ = std::move(snippets);
  snippet_matcher_.ApplyProfile(snippet_profile_);
  snippet_insert_buffer_.clear();
}

InputDecision ResidentInputController::Process(
    const PhysicalKeyEvent& event,
    const TypingContext& context) noexcept {
  if (event.injected_by_keyina) {
    return {};
  }

  if (!event.key_down) {
    InputDecision decision{};
    if (suppressed_keys_.Get(event.virtual_key)) {
      suppressed_keys_.Set(event.virtual_key, false);
      decision.suppress = true;
    }
    return decision;
  }

  try {
    return ProcessKeyDown(event, context);
  } catch (...) {
    ResetEngineState();
    return {};
  }
}

void ResidentInputController::OnPointerReset() noexcept {
  ResetEngineState();
}

void ResidentInputController::Reset() noexcept {
  ResetEngineState();
  has_context_ = false;
}

bool ResidentInputController::KeyStateSet::Get(
    std::uint16_t virtual_key) const noexcept {
  if (virtual_key >= 256) {
    return false;
  }
  const auto segment = static_cast<std::size_t>(virtual_key >> 6);
  const auto mask = std::uint64_t{1} << (virtual_key & 63u);
  return (segments_[segment] & mask) != 0;
}

void ResidentInputController::KeyStateSet::Set(
    std::uint16_t virtual_key,
    bool value) noexcept {
  if (virtual_key >= 256) {
    return;
  }
  const auto segment = static_cast<std::size_t>(virtual_key >> 6);
  const auto mask = std::uint64_t{1} << (virtual_key & 63u);
  if (value) {
    segments_[segment] |= mask;
  } else {
    segments_[segment] &= ~mask;
  }
}

void ResidentInputController::KeyStateSet::Clear() noexcept {
  segments_.fill(0);
}

InputDecision ResidentInputController::ProcessKeyDown(
    const PhysicalKeyEvent& event,
    const TypingContext& context) {
  if (!has_context_) {
    context_ = context;
    has_context_ = true;
  } else if (context_ != context) {
    ResetEngineState();
    context_ = context;
  }

  if (event.control || event.alt || event.windows || context.bypass_typing) {
    ResetEngineState();
    return {};
  }

  if (event.key_repeat && event.virtual_key != kBackspace &&
      suppressed_keys_.Get(event.virtual_key)) {
    InputDecision decision{};
    decision.suppress = true;
    return decision;
  }

  if (event.virtual_key == kBackspace) {
    if (snippet_matcher_.active()) {
      snippet_matcher_.ProcessBackspace();
    }
    engine_.Reset();
    pointer_observation_required_ = false;
    return {};
  }

  const InputCharacterClass character_class = ClassifyInputCharacter(
      event.character,
      profile_.quick_telex_letters);
  const char32_t delimiter = DelimiterForEvent(event, character_class);
  if (delimiter != U'\0' && snippet_matcher_.active()) {
    const auto match = snippet_matcher_.ProcessDelimiter(
        delimiter,
        context.application_hash);
    engine_.Reset();
    pointer_observation_required_ = false;
    if (match.status == RuntimeSnippetMatchStatus::Match) {
      auto decision = BuildSnippetDecision(match, delimiter);
      if (decision.suppress) {
        suppressed_keys_.Set(event.virtual_key, true);
      }
      return decision;
    }
    return {};
  }

  if (event.character != U'\0' &&
      (!snippet_matcher_.active() || delimiter == U'\0')) {
    const auto snippet = snippet_matcher_.ProcessCharacter(event.character);
    if (snippet.status == RuntimeSnippetMatchStatus::Prefix) {
      engine_.Reset();
      pointer_observation_required_ = false;
      return {};
    }
    if (snippet.status == RuntimeSnippetMatchStatus::FailedCandidate) {
      engine_.Reset();
      pointer_observation_required_ = false;
      return {};
    }
  }

  if (!profile_.vietnamese_enabled) {
    return {};
  }

  TextEditView edit{};
  if (character_class == InputCharacterClass::Composition) {
    edit = engine_.ProcessView(KeyEvent{
        KeyKind::Character,
        event.character,
        event.shift,
        event.control,
        event.alt,
    });
    pointer_observation_required_ = true;
  } else if (event.virtual_key == kSpace ||
             character_class == InputCharacterClass::CommitBoundary) {
    edit = engine_.ProcessView(KeyEvent{
        KeyKind::CommitBoundary,
        event.virtual_key == kSpace ? U' ' : event.character,
        event.shift,
        event.control,
        event.alt,
    });
    pointer_observation_required_ = false;
  } else {
    if (IsResetBoundary(event.virtual_key)) {
      ResetEngineState();
    }
    return {};
  }

  auto decision = BuildDecision(edit, event.character);
  if (decision.suppress) {
    suppressed_keys_.Set(event.virtual_key, true);
  }
  return decision;
}

InputDecision ResidentInputController::BuildSnippetDecision(
    const RuntimeSnippetMatch& match,
    char32_t delimiter) {
  if (match.definition == nullptr) {
    return {};
  }

  snippet_insert_buffer_.clear();
  if (match.definition->command == RuntimeSnippetCommand::None &&
      !ExpandRuntimeSnippetTemplate(
          match.definition->expansion,
          CurrentRuntimeSnippetDateTime(),
          snippet_insert_buffer_)) {
    return {};
  }
  if (match.definition->command == RuntimeSnippetCommand::ExternalOutput &&
      match.definition->preserve_delimiter) {
    return {};
  }
  if (match.definition->preserve_delimiter) {
    if (delimiter > 0xFFFF) {
      return {};
    }
    snippet_insert_buffer_.push_back(static_cast<char16_t>(delimiter));
  }

  InputDecision decision{};
  decision.suppress = true;
  decision.backspace_count = match.erase_codepoints;
  decision.extended_insert = snippet_insert_buffer_;
  decision.snippet_command_payload =
      match.definition->command == RuntimeSnippetCommand::ExternalOutput
          ? std::u16string_view(match.definition->expansion)
          : std::u16string_view{};
  decision.snippet_command = match.definition->command;
  decision.snippet_target_process_id = context_.foreground_process_id;
  decision.snippet_target_focus_window = context_.focus_window;
  return decision;
}

InputDecision ResidentInputController::BuildDecision(
    const TextEditView& edit,
    char32_t physical_character) noexcept {
  if (!edit.consumed || IsLiteralPassThrough(edit, physical_character) ||
      edit.erase_codepoints > std::numeric_limits<std::uint16_t>::max()) {
    return {};
  }

  InputDecision decision{};
  decision.suppress = true;
  decision.backspace_count =
      static_cast<std::uint16_t>(edit.erase_codepoints);
  for (const auto codepoint : edit.insert) {
    if (!AppendUtf16(codepoint, decision)) {
      ResetEngineState();
      return {};
    }
  }
  return decision;
}

void ResidentInputController::ResetEngineState() noexcept {
  engine_.Reset();
  snippet_matcher_.Reset();
  snippet_insert_buffer_.clear();
  suppressed_keys_.Clear();
  pointer_observation_required_ = false;
}

}  // namespace keyina::windows
