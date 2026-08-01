#include <keyina/windows/keystroke_overlay_model.h>

#include <algorithm>

namespace keyina::windows {

void BoundedKeystrokeOverlayText::Assign(
    std::u16string_view value,
    bool force_truncated) noexcept {
  std::size_t bounded = std::min(value.size(), storage_.size());
  if (bounded != 0 && value[bounded - 1] >= 0xD800 &&
      value[bounded - 1] <= 0xDBFF) {
    --bounded;
  }
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
  const bool high_surrogate = value >= 0xD800 && value <= 0xDBFF;
  const bool low_surrogate = value >= 0xDC00 && value <= 0xDFFF;
  if (size_ != 0 && storage_[size_ - 1] >= 0xD800 &&
      storage_[size_ - 1] <= 0xDBFF) {
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
    if (size_ + 2 > storage_.size()) {
      truncated_ = true;
      return false;
    }
    storage_[size_++] = value;
    return true;
  }
  if (size_ >= storage_.size()) {
    truncated_ = true;
    return false;
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
  if (size_ != 0 && storage_[size_ - 1] >= 0xD800 &&
      storage_[size_ - 1] <= 0xDBFF) {
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
    return {std::chrono::milliseconds(80), false, false};
  }

  if (context.low_power) {
    return {std::chrono::milliseconds(70), false, false};
  }

  if (context.level == KeystrokeOverlayMotionLevel::Adaptive &&
      context.rapid_input) {
    return {std::chrono::milliseconds(65), true, false};
  }

  return {std::chrono::milliseconds(120), true, true};
}

}  // namespace keyina::windows
