#include <keyina/engine.h>

#include <algorithm>
#include <array>
#include <optional>
#include <utility>

#include <keyina/context_guard.h>
#include <keyina/vietnamese.h>

namespace keyina {
namespace {

constexpr char32_t ToAsciiLower(char32_t value) noexcept {
  return value >= U'A' && value <= U'Z' ? value + (U'a' - U'A') : value;
}

TextEdit Difference(std::u32string_view before, std::u32string_view after,
                    bool consumed) {
  std::size_t common_prefix = 0;
  const std::size_t shared_size = std::min(before.size(), after.size());
  while (common_prefix < shared_size &&
         before[common_prefix] == after[common_prefix]) {
    ++common_prefix;
  }

  return TextEdit{
      before.size() - common_prefix,
      std::u32string{after.substr(common_prefix)},
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

bool ReplaceLetter(std::u32string& visible, std::size_t index,
                   VietnameseLetter letter) {
  const auto composed = ComposeVietnamese(letter);
  if (!composed.has_value()) {
    return false;
  }
  visible[index] = *composed;
  return true;
}

bool ApplyWModifier(std::u32string& visible) {
  if (visible.size() >= 2) {
    auto left = DecomposeVietnamese(visible[visible.size() - 2]);
    auto right = DecomposeVietnamese(visible.back());
    if (left.has_value() && right.has_value() && left->base == U'u' &&
        right->base == U'o' && left->shape == VowelShape::Plain &&
        right->shape == VowelShape::Plain) {
      const Tone left_tone = left->tone;
      const Tone right_tone = right->tone;
      left->tone = Tone::None;
      right->tone = Tone::None;
      if (ApplyShape(*left, VowelShape::Horn) &&
          ApplyShape(*right, VowelShape::Horn)) {
        right->tone = right_tone != Tone::None ? right_tone : left_tone;
        if (ReplaceLetter(visible, visible.size() - 2, *left) &&
            ReplaceLetter(visible, visible.size() - 1, *right)) {
          return true;
        }
      }
    }
  }

  if (visible.empty()) {
    return false;
  }
  auto letter = DecomposeVietnamese(visible.back());
  if (!letter.has_value() || letter->shape != VowelShape::Plain) {
    return false;
  }

  VowelShape shape = VowelShape::Plain;
  if (letter->base == U'a') {
    shape = VowelShape::Breve;
  } else if (letter->base == U'o' || letter->base == U'u') {
    shape = VowelShape::Horn;
  } else {
    return false;
  }

  return ApplyShape(*letter, shape) &&
         ReplaceLetter(visible, visible.size() - 1, *letter);
}

bool ApplyRepeatedVowelModifier(std::u32string& visible,
                                char32_t modifier) {
  if (visible.empty()) {
    return false;
  }
  auto letter = DecomposeVietnamese(visible.back());
  if (!letter.has_value() || letter->base != modifier ||
      letter->shape != VowelShape::Plain) {
    return false;
  }
  return ApplyShape(*letter, VowelShape::Circumflex) &&
         ReplaceLetter(visible, visible.size() - 1, *letter);
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
  return false;
}

bool ApplyLetterModifier(std::u32string& visible, char32_t key) {
  const char32_t modifier = ToAsciiLower(key);
  if (modifier == U'w') {
    return ApplyWModifier(visible);
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
  raw_keys_.reserve(kMaxActiveKeys);
  visible_text_.reserve(kMaxActiveKeys);
}

TextEdit Engine::Process(const KeyEvent& event) {
  if (event.kind == KeyKind::Reset) {
    Reset();
    return {};
  }
  if (event.kind == KeyKind::CommitBoundary) {
    Reset();
    return {};
  }
  if (event.control || event.alt) {
    Reset();
    return {};
  }
  if (event.kind == KeyKind::Backspace) {
    if (raw_keys_.empty()) {
      return {};
    }
    const std::u32string before = visible_text_;
    raw_keys_.pop_back();
    if (raw_keys_.empty()) {
      visible_text_.clear();
    } else {
      const GuardContext context{false, config_.application_bypass};
      const GuardResult guard = ClassifyToken(raw_keys_, context);
      visible_text_ = guard.transform ? ComposeRaw() : raw_keys_;
    }
    return Difference(before, visible_text_, true);
  }

  if (raw_keys_.size() >= kMaxActiveKeys) {
    Reset();
    raw_keys_.push_back(event.character);
    const GuardContext context{false, config_.application_bypass};
    const GuardResult guard = ClassifyToken(raw_keys_, context);
    visible_text_ = guard.transform ? ComposeRaw() : raw_keys_;
    auto edit = Difference({}, visible_text_, true);
    edit.commit_before = true;
    return edit;
  }

  const std::u32string before = visible_text_;
  raw_keys_.push_back(event.character);

  const GuardContext context{false, config_.application_bypass};
  const GuardResult guard = ClassifyToken(raw_keys_, context);
  visible_text_ = guard.transform ? ComposeRaw() : raw_keys_;
  return Difference(before, visible_text_, true);
}

void Engine::Reset() noexcept {
  raw_keys_.clear();
  visible_text_.clear();
}

std::u32string_view Engine::VisibleText() const noexcept {
  return visible_text_;
}

std::u32string_view Engine::RawKeys() const noexcept { return raw_keys_; }

std::u32string Engine::ComposeRaw() const {
  std::u32string visible;
  visible.reserve(raw_keys_.size());
  for (const char32_t key : raw_keys_) {
    const auto tone = ToneFromKey(key);
    if (tone.has_value() && ApplyTone(visible, *tone, config_.tone_placement)) {
      continue;
    }
    if (!ApplyLetterModifier(visible, key)) {
      visible.push_back(key);
    }
  }
  return visible;
}

}  // namespace keyina
