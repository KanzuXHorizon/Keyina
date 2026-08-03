#include <keyina/windows/keystroke_overlay_model.h>

#include <algorithm>

namespace keyina::windows {
namespace {

bool IsHighSurrogate(char16_t value) noexcept {
  return value >= 0xD800 && value <= 0xDBFF;
}

bool IsLowSurrogate(char16_t value) noexcept {
  return value >= 0xDC00 && value <= 0xDFFF;
}

char32_t DecodeCodePoint(
    std::u16string_view value,
    std::size_t offset,
    std::size_t& units) noexcept {
  units = 1;
  const char16_t first = value[offset];
  if (!IsHighSurrogate(first) || offset + 1 >= value.size() ||
      !IsLowSurrogate(value[offset + 1])) {
    return first;
  }
  units = 2;
  return 0x10000 +
      ((static_cast<char32_t>(first) - 0xD800) << 10) +
      (static_cast<char32_t>(value[offset + 1]) - 0xDC00);
}

bool IsRegionalIndicator(char32_t value) noexcept {
  return value >= 0x1F1E6 && value <= 0x1F1FF;
}

bool IsGraphemeExtension(char32_t value) noexcept {
  return (value >= 0x0300 && value <= 0x036F) ||
      (value >= 0x1AB0 && value <= 0x1AFF) ||
      (value >= 0x1DC0 && value <= 0x1DFF) ||
      (value >= 0x20D0 && value <= 0x20FF) ||
      (value >= 0xFE00 && value <= 0xFE0F) ||
      (value >= 0xFE20 && value <= 0xFE2F) ||
      (value >= 0x1F3FB && value <= 0x1F3FF) ||
      (value >= 0xE0020 && value <= 0xE007F) ||
      (value >= 0xE0100 && value <= 0xE01EF);
}

std::size_t LeadingGraphemeLength(std::u16string_view value) noexcept {
  if (value.empty()) {
    return 0;
  }

  std::size_t first_units = 0;
  const char32_t first = DecodeCodePoint(value, 0, first_units);
  std::size_t offset = first_units;
  if (IsRegionalIndicator(first) && offset < value.size()) {
    std::size_t next_units = 0;
    const char32_t next = DecodeCodePoint(value, offset, next_units);
    if (IsRegionalIndicator(next)) {
      offset += next_units;
    }
  }
  while (offset < value.size()) {
    std::size_t units = 0;
    const char32_t code_point = DecodeCodePoint(value, offset, units);
    if (IsGraphemeExtension(code_point)) {
      offset += units;
      continue;
    }
    if (code_point != 0x200D || offset + units >= value.size()) {
      break;
    }
    offset += units;
    std::size_t joined_units = 0;
    static_cast<void>(DecodeCodePoint(value, offset, joined_units));
    offset += joined_units;
  }
  return offset;
}

std::size_t CompletePrefixLength(
    std::u16string_view value,
    std::size_t capacity) noexcept {
  std::size_t offset = 0;
  while (offset < value.size()) {
    const std::size_t cluster = LeadingGraphemeLength(value.substr(offset));
    if (cluster == 0 || cluster > capacity - offset) {
      break;
    }
    offset += cluster;
    if (offset == capacity) {
      break;
    }
  }
  return offset;
}

}  // namespace

void BoundedKeystrokeOverlayText::Assign(
    std::u16string_view value,
    bool force_truncated) noexcept {
  const std::size_t bounded = CompletePrefixLength(value, storage_.size());
  if (bounded != 0) {
    std::copy_n(value.begin(), bounded, storage_.begin());
  }
  size_ = static_cast<std::uint8_t>(bounded);
  truncated_ = force_truncated || bounded < value.size();
}

void BoundedKeystrokeOverlayText::Clear() noexcept {
  size_ = 0;
  truncated_ = false;
}

bool BoundedKeystrokeOverlayText::Append(char16_t value) noexcept {
  const bool high_surrogate = IsHighSurrogate(value);
  const bool low_surrogate = IsLowSurrogate(value);
  if (size_ != 0 && IsHighSurrogate(storage_[size_ - 1])) {
    if (low_surrogate && size_ < storage_.size()) {
      storage_[size_++] = value;
      return true;
    }
    --size_;
    truncated_ = true;
  } else if (low_surrogate) {
    truncated_ = true;
    return false;
  }
  if (high_surrogate) {
    while (size_ + 2 > storage_.size()) {
      const std::size_t discard = LeadingGraphemeLength(View());
      if (discard == 0 || discard > size_) {
        truncated_ = true;
        return false;
      }
      std::move(storage_.begin() + discard, storage_.begin() + size_,
                storage_.begin());
      size_ = static_cast<std::uint8_t>(size_ - discard);
      truncated_ = true;
    }
    storage_[size_++] = value;
    return true;
  }
  if (size_ >= storage_.size()) {
    const std::size_t discard = LeadingGraphemeLength(View());
    std::move(storage_.begin() + discard, storage_.begin() + size_,
              storage_.begin());
    size_ = static_cast<std::uint8_t>(size_ - discard);
    truncated_ = true;
  }
  storage_[size_++] = value;
  return true;
}

void BoundedKeystrokeOverlayText::EraseLast(std::size_t count) noexcept {
  if (count >= size_) {
    Clear();
    return;
  }
  size_ = static_cast<std::uint8_t>(size_ - count);
  if (size_ != 0 && IsHighSurrogate(storage_[size_ - 1])) {
    --size_;
  }
}

bool KeystrokeOverlayPreferences::IsValid() const noexcept {
  return static_cast<std::uint8_t>(motion) <=
             static_cast<std::uint8_t>(KeystrokeOverlayMotionLevel::Off) &&
      static_cast<std::uint8_t>(fallback_corner) <=
          static_cast<std::uint8_t>(
              KeystrokeOverlayFallbackCorner::TopLeft) &&
      size_percent >= 75 && size_percent <= 150 &&
      opacity_percent >= 25 && opacity_percent <= 100 &&
      hide_delay_milliseconds >= 500 &&
      hide_delay_milliseconds <= 2000 &&
      sound_volume_percent <= 100;
}

KeystrokeOverlayState KeystrokeOverlayReducer::Apply(
    const KeystrokeOverlayState& current,
    const KeystrokeOverlayEvent& event) const noexcept {
  if (event.generation < current.generation) {
    return current;
  }

  KeystrokeOverlayState next = current;
  next.generation = event.generation;
  next.transition = event.kind;

  if (event.kind == KeystrokeOverlayEventKind::Suppressed ||
      event.kind == KeystrokeOverlayEventKind::Cleared) {
    next.tokens.fill(0);
    next.token_count = 0;
    next.text.Clear();
    next.visible = false;
    next.truncated = false;
    return next;
  }

  if (event.kind == KeystrokeOverlayEventKind::Token) {
    if (event.token == 0) {
      return next;
    }
    if (current.transition != KeystrokeOverlayEventKind::Token) {
      next.tokens.fill(0);
      next.token_count = 0;
      next.text.Clear();
      next.truncated = false;
    }
    if (next.token_count < kMaximumOverlayTokens) {
      next.tokens[next.token_count++] = event.token;
    } else {
      std::move(
          next.tokens.begin() + 1,
          next.tokens.end(),
          next.tokens.begin());
      next.tokens.back() = event.token;
      next.truncated = true;
    }
    next.visible = true;
    return next;
  }

  next.tokens.fill(0);
  next.token_count = 0;
  next.text = event.text;
  next.truncated = next.text.truncated();
  next.visible = !next.text.empty();
  return next;
}

KeystrokeOverlayPrivacyDecision EvaluateKeystrokeOverlayPrivacy(
    const KeystrokeOverlayPrivacyContext& context) noexcept {
  if (!context.classification_known || !context.editable_text ||
      context.password || context.protected_input || context.secure_desktop ||
      context.excluded_application) {
    return KeystrokeOverlayPrivacyDecision::Suppress;
  }
  return KeystrokeOverlayPrivacyDecision::Allow;
}

KeystrokeOverlayMotionDecision ResolveKeystrokeOverlayMotion(
    const KeystrokeOverlayMotionContext& context) noexcept {
  if (context.level == KeystrokeOverlayMotionLevel::Off) {
    return {};
  }

  if (context.level == KeystrokeOverlayMotionLevel::Reduced ||
      context.system_reduced_motion) {
    return {std::chrono::milliseconds(120), false, false};
  }

  if (context.low_power) {
    return {std::chrono::milliseconds(100), false, false};
  }

  if (context.level == KeystrokeOverlayMotionLevel::Adaptive &&
      context.rapid_input) {
    return {std::chrono::milliseconds(110), true, false};
  }

  return {std::chrono::milliseconds(200), true, true};
}

bool ShouldShowKeystrokeOverlayCompositionText(
    std::u16string_view composition,
    bool transformed) noexcept {
  return transformed ||
      std::any_of(composition.begin(), composition.end(), [](char16_t unit) {
        return unit > 0x7F;
      });
}

}  // namespace keyina::windows
