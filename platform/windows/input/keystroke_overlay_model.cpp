#include <keyina/windows/keystroke_overlay_model.h>

#include <algorithm>

namespace keyina::windows {
namespace {

std::u16string BoundedText(std::u16string_view value, bool& truncated) {
  truncated = value.size() > kMaximumOverlayCodeUnits;
  return std::u16string(value.substr(0, kMaximumOverlayCodeUnits));
}

}  // namespace

void KeystrokeOverlayEvent::SetText(std::u16string_view value) noexcept {
  text_length = std::min(value.size(), kMaximumOverlayCodeUnits);
  std::copy_n(value.begin(), text_length, text.begin());
  text_truncated = value.size() > kMaximumOverlayCodeUnits;
}

bool KeystrokeOverlayPreferences::IsValid() const noexcept {
  return size_percent >= 75 && size_percent <= 150 &&
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
    next.text.clear();
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
      std::move(next.tokens.begin() + 1, next.tokens.end(), next.tokens.begin());
      next.tokens.back() = event.token;
      next.truncated = true;
    }
    next.visible = true;
    return next;
  }

  next.text = BoundedText(event.Text(), next.truncated);
  next.truncated = next.truncated || event.text_truncated;
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
