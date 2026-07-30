#include <keyina/engine.h>

#include <algorithm>
#include <array>
#include <optional>
#include <utility>

#include <keyina/context_guard.h>
#include <keyina/vietnamese.h>
#include <keyina/vietnamese_syllable.h>

namespace keyina {
namespace {

constexpr char32_t ToAsciiLower(char32_t value) noexcept {
  return value >= U'A' && value <= U'Z' ? value + (U'a' - U'A') : value;
}

TextEditView DifferenceView(std::u32string_view before,
                            std::u32string_view after,
                            bool consumed,
                            std::u32string& edit_buffer) {
  std::size_t common_prefix = 0;
  const std::size_t shared_size = std::min(before.size(), after.size());
  while (common_prefix < shared_size &&
         before[common_prefix] == after[common_prefix]) {
    ++common_prefix;
  }

  edit_buffer.assign(after.substr(common_prefix));
  return TextEditView{
      before.size() - common_prefix,
      edit_buffer,
      consumed,
  };
}

bool ApplyShape(VietnameseLetter& letter, VowelShape shape) noexcept {
  const VietnameseLetter candidate{letter.base, shape, letter.tone,
                                   letter.uppercase};
  if (!ComposeVietnamese(candidate).has_value()) {
    return false;
  }
  letter.shape = shape;
  return true;
}

bool HasVowelAfter(std::u32string_view visible, std::size_t index) noexcept;
std::optional<Tone> ToneFromKey(char32_t key) noexcept;

bool IsIrrecoverablyInvalid(SyllableError error) noexcept {
  return error == SyllableError::TooLong ||
         error == SyllableError::InvalidOnset ||
         error == SyllableError::InvalidCoda ||
         error == SyllableError::InvalidOrthography;
}

bool ReplaceLetter(std::u32string& visible, std::size_t index,
                   VietnameseLetter letter) {
  const auto composed = ComposeVietnamese(letter);
  if (!composed.has_value()) {
    return false;
  }
  visible[index] = *composed;
  return true;
}

bool AppendVietnameseLetter(std::u32string& visible, char32_t base,
                            VowelShape shape, bool uppercase) {
  const auto composed =
      ComposeVietnamese({base, shape, Tone::None, uppercase});
  if (!composed.has_value()) {
    return false;
  }
  visible.push_back(*composed);
  return true;
}

bool ApplyWModifier(std::u32string& visible, bool uppercase) {
  std::optional<std::size_t> last_vowel;
  std::optional<std::size_t> previous_vowel;
  for (std::size_t offset = visible.size(); offset > 0; --offset) {
    const std::size_t index = offset - 1;
    const auto letter = DecomposeVietnamese(visible[index]);
    if (!letter.has_value() || letter->base == U'đ') {
      continue;
    }
    if (!last_vowel.has_value()) {
      last_vowel = index;
    } else {
      previous_vowel = index;
      break;
    }
  }
  if (!last_vowel.has_value()) {
    return AppendVietnameseLetter(visible, U'u', VowelShape::Horn, uppercase);
  }

  if (previous_vowel.has_value()) {
    auto left = DecomposeVietnamese(visible[*previous_vowel]);
    auto right = DecomposeVietnamese(visible[*last_vowel]);
    const bool consonantal_u =
        left.has_value() && left->base == U'u' && *previous_vowel > 0 &&
        ToAsciiLower(visible[*previous_vowel - 1]) == U'q';
    if (left.has_value() && right.has_value() && !consonantal_u &&
        left->base == U'u' && left->shape == VowelShape::Plain &&
        right->shape == VowelShape::Plain &&
        (right->base == U'u' || right->base == U'o' ||
         right->base == U'a')) {
      const Tone carried_tone =
          right->tone != Tone::None ? right->tone : left->tone;
      left->tone = Tone::None;
      right->tone = Tone::None;

      if (right->base == U'u') {
        if (ApplyShape(*left, VowelShape::Horn)) {
          left->tone = carried_tone;
          return ReplaceLetter(visible, *previous_vowel, *left) &&
                 ReplaceLetter(visible, *last_vowel, *right);
        }
      } else if (right->base == U'o') {
        if (ApplyShape(*left, VowelShape::Horn) &&
            ApplyShape(*right, VowelShape::Horn)) {
          right->tone = carried_tone;
          return ReplaceLetter(visible, *previous_vowel, *left) &&
                 ReplaceLetter(visible, *last_vowel, *right);
        }
      } else if (ApplyShape(*left, VowelShape::Horn)) {
        left->tone = carried_tone;
        return ReplaceLetter(visible, *previous_vowel, *left) &&
               ReplaceLetter(visible, *last_vowel, *right);
      }
    }
  }

  for (std::size_t offset = visible.size(); offset > 0; --offset) {
    const std::size_t index = offset - 1;
    auto letter = DecomposeVietnamese(visible[index]);
    if (!letter.has_value() || letter->shape != VowelShape::Plain) {
      continue;
    }

    const bool consonantal_u =
        letter->base == U'u' && index > 0 &&
        ToAsciiLower(visible[index - 1]) == U'q' &&
        HasVowelAfter(visible, index);
    if (consonantal_u) {
      continue;
    }

    VowelShape shape = VowelShape::Plain;
    if (letter->base == U'a') {
      shape = VowelShape::Breve;
    } else if (letter->base == U'o' || letter->base == U'u') {
      shape = VowelShape::Horn;
    } else {
      continue;
    }

    return ApplyShape(*letter, shape) &&
           ReplaceLetter(visible, index, *letter);
  }
  return AppendVietnameseLetter(visible, U'u', VowelShape::Horn, uppercase);
}

bool ApplyRepeatedVowelModifier(std::u32string& visible,
                                char32_t modifier) {
  for (std::size_t offset = visible.size(); offset > 0; --offset) {
    const std::size_t index = offset - 1;
    auto letter = DecomposeVietnamese(visible[index]);
    if (!letter.has_value() || letter->base == U'đ') {
      continue;
    }
    if (letter->base != modifier || letter->shape != VowelShape::Plain) {
      continue;
    }
    if (!ApplyShape(*letter, VowelShape::Circumflex)) {
      return false;
    }
    const char32_t original = visible[index];
    if (!ReplaceLetter(visible, index, *letter)) {
      return false;
    }
    if (index + 1 < visible.size()) {
      const auto analysis = AnalyzeVietnameseSyllable(visible);
      if (analysis.status != SyllableStatus::Valid &&
          analysis.error != SyllableError::InvalidTone) {
        visible[index] = original;
        return false;
      }
    }
    return true;
  }
  return false;
}

bool HasAdjacentVowels(std::u32string_view visible) noexcept {
  for (std::size_t index = 1; index < visible.size(); ++index) {
    if (IsVietnameseVowel(visible[index - 1]) &&
        IsVietnameseVowel(visible[index])) {
      return true;
    }
  }
  return false;
}

bool ApplyDModifier(std::u32string& visible) {
  if (visible.empty()) {
    return false;
  }
  if (visible.back() == U'd') {
    visible.back() = U'đ';
    return true;
  }
  if (visible.back() == U'D') {
    visible.back() = U'Đ';
    return true;
  }

  // Accept delayed Telex order such as "duocd" and "dongd" only when
  // replacing the initial d produces a structurally plausible Vietnamese
  // syllable. This keeps ordinary Latin tokens such as "david" literal.
  if (visible.size() >= 2 &&
      (visible.front() == U'd' || visible.front() == U'D')) {
    const char32_t original = visible.front();
    visible.front() = original == U'd' ? U'đ' : U'Đ';
    const auto analysis = AnalyzeVietnameseSyllable(visible);
    if (analysis.status == SyllableStatus::Valid ||
        analysis.error == SyllableError::InvalidTone ||
        HasAdjacentVowels(visible)) {
      return true;
    }
    visible.front() = original;
  }
  return false;
}

bool ApplyQuickTelexLetter(std::u32string& visible, char32_t key) {
  if (key == U'[' || key == U'{') {
    return AppendVietnameseLetter(visible, U'u', VowelShape::Horn,
                                  key == U'{');
  }
  if (key == U']' || key == U'}') {
    return AppendVietnameseLetter(visible, U'o', VowelShape::Horn,
                                  key == U'}');
  }
  return false;
}

bool ClearTone(std::u32string& visible) {
  for (std::size_t offset = visible.size(); offset > 0; --offset) {
    const std::size_t index = offset - 1;
    auto letter = DecomposeVietnamese(visible[index]);
    if (!letter.has_value() || letter->tone == Tone::None) {
      continue;
    }
    letter->tone = Tone::None;
    return ReplaceLetter(visible, index, *letter);
  }
  return false;
}

bool ApplyLetterModifier(std::u32string& visible, char32_t key,
                         bool quick_telex_letters) {
  if (quick_telex_letters && ApplyQuickTelexLetter(visible, key)) {
    return true;
  }

  const char32_t modifier = ToAsciiLower(key);
  if (modifier == U'w') {
    return ApplyWModifier(visible, key >= U'A' && key <= U'Z');
  }
  if (modifier == U'a' || modifier == U'e' || modifier == U'o') {
    return ApplyRepeatedVowelModifier(visible, modifier);
  }
  if (modifier == U'd') {
    return ApplyDModifier(visible);
  }
  return false;
}

std::optional<Tone> ToneFromKey(char32_t key) noexcept {
  switch (ToAsciiLower(key)) {
    case U's':
      return Tone::Acute;
    case U'f':
      return Tone::Grave;
    case U'r':
      return Tone::Hook;
    case U'x':
      return Tone::Tilde;
    case U'j':
      return Tone::Dot;
    default:
      return std::nullopt;
  }
}

bool IsAsciiVowel(char32_t value) noexcept {
  const char32_t lower = ToAsciiLower(value);
  return lower == U'a' || lower == U'e' || lower == U'i' ||
         lower == U'o' || lower == U'u' || lower == U'y';
}

bool HasSuspiciousToneBeforeNewVowel(std::u32string_view raw) noexcept {
  for (std::size_t tone_index = 0; tone_index < raw.size(); ++tone_index) {
    if (!ToneFromKey(raw[tone_index]).has_value()) {
      continue;
    }

    bool has_vowel_before = false;
    for (std::size_t index = 0; index < tone_index; ++index) {
      if (IsAsciiVowel(raw[index])) {
        has_vowel_before = true;
        break;
      }
    }
    if (!has_vowel_before) {
      continue;
    }

    for (std::size_t index = tone_index + 1; index < raw.size(); ++index) {
      if (IsAsciiVowel(raw[index])) {
        return true;
      }
    }
  }
  return false;
}

bool HasVowelAfter(std::u32string_view visible, std::size_t index) noexcept {
  for (std::size_t next = index + 1; next < visible.size(); ++next) {
    if (IsVietnameseVowel(visible[next])) {
      return true;
    }
  }
  return false;
}

std::size_t CollectNucleus(std::u32string_view visible,
                           std::array<std::size_t, 64>& indices) noexcept {
  std::size_t count = 0;
  for (std::size_t index = 0; index < visible.size() && count < indices.size();
       ++index) {
    const auto letter = DecomposeVietnamese(visible[index]);
    if (!letter.has_value() || letter->base == U'đ') {
      continue;
    }

    const char32_t previous = index == 0 ? U'\0' : ToAsciiLower(visible[index - 1]);
    const bool consonantal_u = letter->base == U'u' && previous == U'q' &&
                               HasVowelAfter(visible, index);
    const bool consonantal_i = letter->base == U'i' && previous == U'g' &&
                               HasVowelAfter(visible, index);
    if (!consonantal_u && !consonantal_i) {
      indices[count++] = index;
    }
  }
  return count;
}

bool HasTrailingConsonant(std::u32string_view visible,
                          std::size_t last_vowel) noexcept {
  for (std::size_t index = last_vowel + 1; index < visible.size(); ++index) {
    const char32_t value = ToAsciiLower(visible[index]);
    if ((value >= U'a' && value <= U'z') || value == U'đ') {
      return true;
    }
  }
  return false;
}

std::size_t SelectToneTarget(std::u32string_view visible,
                             const std::array<std::size_t, 64>& indices,
                             std::size_t count,
                             TonePlacement placement) noexcept {
  for (std::size_t offset = count; offset > 0; --offset) {
    const auto letter = DecomposeVietnamese(visible[indices[offset - 1]]);
    if (letter.has_value() && letter->shape != VowelShape::Plain) {
      return indices[offset - 1];
    }
  }

  if (count == 1) {
    return indices[0];
  }
  if (count >= 3) {
    if (HasTrailingConsonant(visible, indices[count - 1])) {
      return indices[count - 1];
    }
    return indices[count - 2];
  }

  const std::size_t first = indices[0];
  const std::size_t second = indices[1];
  if (HasTrailingConsonant(visible, second)) {
    return second;
  }

  const auto first_letter = DecomposeVietnamese(visible[first]);
  const auto second_letter = DecomposeVietnamese(visible[second]);
  const bool modern_open_cluster =
      first_letter.has_value() && second_letter.has_value() &&
      ((first_letter->base == U'o' &&
        (second_letter->base == U'a' || second_letter->base == U'e')) ||
       (first_letter->base == U'u' && second_letter->base == U'y'));
  if (placement == TonePlacement::Modern && modern_open_cluster) {
    return second;
  }
  return first;
}

bool ApplyTone(std::u32string& visible, Tone tone,
               TonePlacement placement) {
  std::array<std::size_t, 64> indices{};
  const std::size_t count = CollectNucleus(visible, indices);
  if (count == 0) {
    return false;
  }

  for (std::size_t offset = 0; offset < count; ++offset) {
    const std::size_t index = indices[offset];
    auto letter = DecomposeVietnamese(visible[index]);
    if (letter.has_value() && letter->tone != Tone::None) {
      letter->tone = Tone::None;
      ReplaceLetter(visible, index, *letter);
    }
  }

  const std::size_t target = SelectToneTarget(visible, indices, count, placement);
  auto letter = DecomposeVietnamese(visible[target]);
  if (!letter.has_value()) {
    return false;
  }
  letter->tone = tone;
  return ReplaceLetter(visible, target, *letter);
}

}  // namespace

Engine::Engine(EngineConfig config) : config_(config) {
  constexpr std::size_t kBufferCapacity = kMaxActiveKeys + 1;
  raw_keys_.reserve(kBufferCapacity);
  visible_text_.reserve(kBufferCapacity);
  composition_buffer_.reserve(kBufferCapacity);
  previous_key_buffer_.reserve(kBufferCapacity);
  edit_buffer_.reserve(kBufferCapacity);
  literal_text_buffer_.reserve(kBufferCapacity);
}

TextEdit Engine::Process(const KeyEvent& event) {
  const auto edit = ProcessView(event);
  return TextEdit{
      edit.erase_codepoints,
      std::u32string{edit.insert},
      edit.consumed,
      edit.commit_before,
  };
}

TextEditView Engine::ProcessView(const KeyEvent& event) {
  edit_buffer_.clear();
  if (event.kind == KeyKind::Reset) {
    ResetCompositionState();
    return {};
  }
  if (event.kind == KeyKind::CommitBoundary) {
    // Match established Vietnamese IME behavior: separators only commit the
    // current composition. They must never rewrite the word that the user has
    // already seen, because that feels like autocorrect and makes Space
    // destructive.
    ResetCompositionState();
    return {};
  }
  if (event.control || event.alt) {
    ResetCompositionState();
    return {};
  }
  if (event.kind == KeyKind::Backspace) {
    if (raw_keys_.empty()) {
      return {};
    }
    raw_keys_.pop_back();
    if (raw_keys_.empty()) {
      composition_buffer_.clear();
    } else {
      BuildVisibleForRaw();
    }
    return ReplaceVisibleView(true);
  }

  if (raw_keys_.size() >= kMaxActiveKeys) {
    ResetCompositionState();
    raw_keys_.push_back(event.character);
    BuildVisibleForRaw();
    auto edit = ReplaceVisibleView(true);
    edit.commit_before = true;
    return edit;
  }

  raw_keys_.push_back(event.character);
  BuildVisibleForRaw();
  return ReplaceVisibleView(true);
}

void Engine::Reset() noexcept {
  ResetCompositionState();
  edit_buffer_.clear();
}

void Engine::ResetCompositionState() noexcept {
  raw_keys_.clear();
  visible_text_.clear();
  composition_buffer_.clear();
  previous_key_buffer_.clear();
  literal_text_buffer_.clear();
}

std::u32string_view Engine::VisibleText() const noexcept {
  return visible_text_;
}

std::u32string_view Engine::RawKeys() const noexcept { return raw_keys_; }

void Engine::BuildVisibleForRaw() {
  const GuardContext context{false, config_.application_bypass};
  const GuardResult guard = ClassifyToken(raw_keys_, context);
  if (guard.transform) {
    ComposeRaw(composition_buffer_);
    if (config_.restore_invalid_word &&
        composition_buffer_ != literal_text_buffer_) {
      const auto analysis = AnalyzeVietnameseSyllable(composition_buffer_);
      const bool impossible_structure =
          analysis.status == SyllableStatus::Impossible &&
          IsIrrecoverablyInvalid(analysis.error);
      const bool suspicious_tone_order =
          analysis.status != SyllableStatus::Valid &&
          HasSuspiciousToneBeforeNewVowel(raw_keys_);
      if (impossible_structure || suspicious_tone_order) {
        composition_buffer_.assign(literal_text_buffer_);
      }
    }
  } else {
    composition_buffer_.assign(raw_keys_);
  }
}

TextEditView Engine::ReplaceVisibleView(bool consumed) {
  auto edit = DifferenceView(
      visible_text_, composition_buffer_, consumed, edit_buffer_);
  visible_text_.swap(composition_buffer_);
  composition_buffer_.clear();
  return edit;
}

void Engine::ComposeRaw(std::u32string& visible) {
  visible.clear();
  previous_key_buffer_.clear();
  literal_text_buffer_.clear();

  char32_t previous_key = U'\0';
  bool previous_key_transformed = false;
  std::optional<Tone> pending_tone;
  char32_t pending_tone_key = U'\0';

  for (const char32_t key : raw_keys_) {
    const bool repeated_escape =
        ToAsciiLower(key) == ToAsciiLower(previous_key) &&
        previous_key_transformed;
    if (repeated_escape) {
      visible.assign(previous_key_buffer_);
      visible.push_back(key);
      if (pending_tone_key != U'\0' &&
          ToAsciiLower(key) == ToAsciiLower(pending_tone_key)) {
        pending_tone.reset();
        pending_tone_key = U'\0';
      }
      previous_key = key;
      previous_key_transformed = false;
      previous_key_buffer_.assign(visible);
      continue;
    }

    literal_text_buffer_.push_back(key);
    previous_key_buffer_.assign(visible);
    bool transformed = false;
    const char32_t lower = ToAsciiLower(key);
    if (lower == U'z') {
      if (pending_tone.has_value()) {
        pending_tone.reset();
        pending_tone_key = U'\0';
        transformed = true;
      } else {
        transformed = ClearTone(visible);
      }
    } else {
      const auto tone = ToneFromKey(key);
      if (tone.has_value()) {
        std::array<std::size_t, 64> indices{};
        if (CollectNucleus(visible, indices) != 0) {
          pending_tone = *tone;
          pending_tone_key = key;
          transformed = true;
        }
      } else {
        transformed =
            ApplyLetterModifier(visible, key, config_.quick_telex_letters);
      }
    }
    if (!transformed) {
      if (lower == U'o' && !visible.empty()) {
        const auto previous = DecomposeVietnamese(visible.back());
        if (previous.has_value() && previous->base == U'u' &&
            previous->shape == VowelShape::Horn) {
          const VietnameseLetter completed{
              U'o', VowelShape::Horn, Tone::None,
              key >= U'A' && key <= U'Z'};
          const auto composed = ComposeVietnamese(completed);
          if (composed.has_value()) {
            visible.push_back(*composed);
            transformed = true;
          }
        }
      }
      if (!transformed) {
        visible.push_back(key);
      }
    }

    previous_key = key;
    previous_key_transformed = transformed;
  }

  if (pending_tone.has_value()) {
    static_cast<void>(
        ApplyTone(visible, *pending_tone, config_.tone_placement));
  }
}

}  // namespace keyina
