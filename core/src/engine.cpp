#include <keyina/engine.h>

#include <algorithm>
#include <array>
#include <optional>
#include <utility>

#include <keyina/context_guard.h>
#include <keyina/input_character_classification.h>
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
bool HasSeparatedVowelRuns(std::u32string_view visible) noexcept;
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

bool ApplyWModifier(std::u32string& visible, bool uppercase,
                    bool standalone_w_to_u_horn) {
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
    return standalone_w_to_u_horn &&
           AppendVietnameseLetter(visible, U'u', VowelShape::Horn, uppercase);
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
  return standalone_w_to_u_horn &&
         AppendVietnameseLetter(visible, U'u', VowelShape::Horn, uppercase);
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
    if (index + 1 < visible.size() || HasSeparatedVowelRuns(visible)) {
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
                         bool quick_telex_letters,
                         bool standalone_w_to_u_horn) {
  if (quick_telex_letters && ApplyQuickTelexLetter(visible, key)) {
    return true;
  }

  const char32_t modifier = ToAsciiLower(key);
  if (modifier == U'w') {
    return ApplyWModifier(visible, key >= U'A' && key <= U'Z',
                          standalone_w_to_u_horn);
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

bool HasShapedVietnameseVowel(std::u32string_view value) noexcept {
  for (const char32_t character : value) {
    const auto letter = DecomposeVietnamese(character);
    if (letter.has_value() && letter->base != U'đ' &&
        letter->shape != VowelShape::Plain) {
      return true;
    }
  }
  return false;
}

bool HasTrailingToneKey(std::u32string_view raw) noexcept {
  return !raw.empty() && ToneFromKey(raw.back()).has_value();
}

bool HasRepeatedTrailingLiteralS(std::u32string_view raw) noexcept {
  return raw.size() >= 2 &&
         ToAsciiLower(raw[raw.size() - 1]) == U's' &&
         ToAsciiLower(raw[raw.size() - 2]) == U's';
}

bool HasTrailingRepeatedLetterModifierEscape(
    std::u32string_view raw) noexcept {
  if (raw.size() < 3) {
    return false;
  }
  const char32_t repeated = ToAsciiLower(raw.back());
  if (repeated != U'a' && repeated != U'e' && repeated != U'o' &&
      repeated != U'd' && repeated != U'w') {
    return false;
  }
  return ToAsciiLower(raw[raw.size() - 2]) == repeated &&
         ToAsciiLower(raw[raw.size() - 3]) == repeated;
}

bool HasLiteralPrefixBeforeTrailingRepeatedModifierRun(
    std::u32string_view raw) noexcept {
  if (!HasTrailingRepeatedLetterModifierEscape(raw)) {
    return false;
  }

  const char32_t repeated = ToAsciiLower(raw.back());
  std::size_t run_start = raw.size();
  while (run_start > 0 &&
         ToAsciiLower(raw[run_start - 1]) == repeated) {
    --run_start;
  }
  return run_start > 0;
}

bool HasRepeatedAsciiVowelBeforeTrailingCharacter(
    std::u32string_view raw) noexcept {
  if (raw.size() < 3) {
    return false;
  }

  const std::size_t repeated_vowel_start = raw.size() - 3;
  const char32_t previous = ToAsciiLower(raw[raw.size() - 2]);
  const char32_t trailing = ToAsciiLower(raw.back());
  const bool follows_another_vowel =
      repeated_vowel_start > 0 &&
      IsAsciiVowel(ToAsciiLower(raw[repeated_vowel_start - 1]));
  return IsAsciiVowel(previous) &&
         ToAsciiLower(raw[repeated_vowel_start]) == previous &&
         !follows_another_vowel &&
         !ToneFromKey(trailing).has_value();
}

bool HasSeparatedVowelRuns(std::u32string_view visible) noexcept {
  bool saw_vowel = false;
  bool left_vowel_run = false;
  for (const char32_t value : visible) {
    if (IsVietnameseVowel(value)) {
      if (left_vowel_run) {
        return true;
      }
      saw_vowel = true;
    } else if (saw_vowel) {
      left_vowel_run = true;
    }
  }
  return false;
}

bool HasLetterModifierBeforeTrailingTone(std::u32string_view raw) noexcept {
  if (!HasTrailingToneKey(raw) || raw.size() < 3) {
    return false;
  }

  const auto prefix = raw.substr(0, raw.size() - 1);
  for (std::size_t index = 1; index < prefix.size(); ++index) {
    const char32_t previous = ToAsciiLower(prefix[index - 1]);
    const char32_t current = ToAsciiLower(prefix[index]);
    if ((previous == U'a' && current == U'a') ||
        (previous == U'e' && current == U'e') ||
        (previous == U'o' && current == U'o') ||
        (previous == U'd' && current == U'd')) {
      return true;
    }
  }
  return false;
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
  std::array<char32_t, 64> nucleus{};
  for (std::size_t offset = 0; offset < count; ++offset) {
    nucleus[offset] = visible[indices[offset]];
  }
  const bool has_coda =
      count != 0 && HasTrailingConsonant(visible, indices[count - 1]);
  const std::size_t tone_offset = SelectVietnameseToneOffset(
      std::u32string_view{nucleus.data(), count}, has_coda,
      placement == TonePlacement::Modern);
  return tone_offset < count ? indices[tone_offset] : indices[0];
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
    // A checked coda is only valid with an acute or dot tone. Keep the shaped
    // vowel visible while the user may still type that final tone key, but if
    // the token ends at a real separator, restore its canonical literal keys.
    // This lets `heet` progress through `hêt` to `hết` without turning an
    // unfinished English `meet` into committed Vietnamese-looking text.
    if (config_.restore_invalid_word && !raw_keys_.empty() &&
        event.character != U'\0' && visible_text_ != literal_text_buffer_) {
      const auto analysis = AnalyzeVietnameseSyllable(visible_text_);
      if (analysis.status == SyllableStatus::Impossible &&
          analysis.error == SyllableError::InvalidTone) {
        const std::size_t erase_codepoints = visible_text_.size();
        edit_buffer_.assign(literal_text_buffer_);
        edit_buffer_.push_back(event.character);
        ResetCompositionState();
        return {
            erase_codepoints,
            edit_buffer_,
            true,
            false,
        };
      }
    }

    // Ordinary separators only commit the current composition. Technical
    // separators are different: together with the raw keys they can reveal
    // that the token is an identifier, path, URL, email, version, or shell
    // token. Restore that raw token before committing so a partially composed
    // Vietnamese syllable never corrupts technical text.
    const bool contains_quick_telex_character =
        config_.quick_telex_letters &&
        std::any_of(
            raw_keys_.begin(),
            raw_keys_.end(),
            IsQuickTelexCompositionCharacter);
    if (!contains_quick_telex_character &&
        !raw_keys_.empty() && event.character != U'\0') {
      literal_text_buffer_.assign(raw_keys_);
      literal_text_buffer_.push_back(event.character);
      const GuardResult guard = ClassifyToken(literal_text_buffer_, {});
      if (!guard.transform && visible_text_ != raw_keys_) {
        const std::size_t erase_codepoints = visible_text_.size();
        edit_buffer_.assign(raw_keys_);
        edit_buffer_.push_back(event.character);
        ResetCompositionState();
        return {
            erase_codepoints,
            edit_buffer_,
            true,
            false,
        };
      }
    }
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
  has_tone_key_before_trailing_character_ = false;
}

std::u32string_view Engine::VisibleText() const noexcept {
  return visible_text_;
}

std::u32string_view Engine::RawKeys() const noexcept { return raw_keys_; }

void Engine::BuildVisibleForRaw() {
  const GuardContext context{false, config_.application_bypass};
  const GuardResult guard = ClassifyToken(raw_keys_, context);
  if (guard.transform) {
    const bool explicit_tone_clear = ComposeRaw(composition_buffer_);
    if (HasTrailingRepeatedLetterModifierEscape(raw_keys_)) {
      composition_buffer_.assign(raw_keys_);
      if (!HasLiteralPrefixBeforeTrailingRepeatedModifierRun(raw_keys_)) {
        composition_buffer_.pop_back();
      }
    } else if (config_.restore_invalid_word &&
        HasRepeatedTrailingLiteralS(raw_keys_) &&
        HasSeparatedVowelRuns(composition_buffer_)) {
      composition_buffer_.assign(raw_keys_);
    } else if (config_.restore_invalid_word &&
               composition_buffer_ != literal_text_buffer_) {
      const auto analysis = AnalyzeVietnameseSyllable(composition_buffer_);
      const bool preserve_explicit_shaped_tone_clear =
          explicit_tone_clear && HasShapedVietnameseVowel(composition_buffer_);
      const bool impossible_structure =
          analysis.status == SyllableStatus::Impossible &&
          IsIrrecoverablyInvalid(analysis.error);
      const bool invalid_tone_modified_nucleus =
          (analysis.status == SyllableStatus::Impossible &&
           analysis.error == SyllableError::InvalidNucleus &&
           has_tone_key_before_trailing_character_) ||
          (HasTrailingToneKey(raw_keys_) &&
           HasSeparatedVowelRuns(composition_buffer_) &&
           !HasLetterModifierBeforeTrailingTone(raw_keys_));
      const bool ambiguous_embedded_tone =
          analysis.status == SyllableStatus::Ambiguous &&
          analysis.error == SyllableError::InvalidCoda &&
          has_tone_key_before_trailing_character_ &&
          HasSeparatedVowelRuns(composition_buffer_);
      const bool suspicious_tone_order =
          analysis.status != SyllableStatus::Valid &&
          HasSuspiciousToneBeforeNewVowel(raw_keys_);
      const bool repeated_ascii_vowel_before_trailing_character =
          analysis.status != SyllableStatus::Valid &&
          analysis.error != SyllableError::InvalidTone &&
          HasRepeatedAsciiVowelBeforeTrailingCharacter(raw_keys_);
      const bool invalid_open_nucleus_after_tone =
          raw_keys_.size() >= 2 && HasTrailingToneKey(raw_keys_) &&
          IsAsciiVowel(raw_keys_[raw_keys_.size() - 2]) &&
          analysis.error == SyllableError::InvalidNucleus;
      if (!preserve_explicit_shaped_tone_clear &&
          (impossible_structure || invalid_tone_modified_nucleus ||
           ambiguous_embedded_tone || suspicious_tone_order ||
           repeated_ascii_vowel_before_trailing_character ||
           invalid_open_nucleus_after_tone)) {
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

bool Engine::ComposeRaw(std::u32string& visible) {
  visible.clear();
  previous_key_buffer_.clear();
  literal_text_buffer_.clear();
  has_tone_key_before_trailing_character_ = false;

  char32_t previous_key = U'\0';
  bool previous_key_transformed = false;
  bool saw_tone_key = false;
  bool explicit_tone_clear = false;
  std::optional<Tone> pending_tone;
  char32_t pending_tone_key = U'\0';

  for (const char32_t key : raw_keys_) {
    has_tone_key_before_trailing_character_ |= saw_tone_key;
    const auto tone = ToneFromKey(key);
    saw_tone_key |= tone.has_value();
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
      explicit_tone_clear = transformed;
    } else {
      if (tone.has_value()) {
        std::array<std::size_t, 64> indices{};
        if (CollectNucleus(visible, indices) != 0) {
          pending_tone = *tone;
          pending_tone_key = key;
          transformed = true;
        }
      } else {
        transformed =
            ApplyLetterModifier(visible, key, config_.quick_telex_letters,
                                config_.standalone_w_to_u_horn);
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
  return explicit_tone_clear;
}

}  // namespace keyina
