#include <keyina/windows/keystroke_overlay_model.h>

#include <algorithm>

namespace keyina::windows {
namespace {

void ClearDisplayState(
    KeystrokeOverlayState& state,
    bool suppressed) noexcept {
  state.text.clear();
  for (auto& token : state.tokens) {
    token.clear();
  }
  state.token_count = 0;
  state.visible = false;
  state.suppressed = suppressed;
  state.truncated = false;
}

void RecomputeTruncation(KeystrokeOverlayState& state) noexcept {
  state.truncated = state.text.truncated();
  for (std::size_t index = 0; index < state.token_count; ++index) {
    state.truncated = state.truncated || state.tokens[index].truncated();
  }
}

void AppendToken(
    KeystrokeOverlayState& state,
    const BoundedKeystrokeOverlayText& token) noexcept {
  if (token.empty()) {
    return;
  }
  if (state.token_count < state.tokens.size()) {
    state.tokens[state.token_count++] = token;
  } else {
    std::move(
        state.tokens.begin() + 1,
        state.tokens.end(),
        state.tokens.begin());
    state.tokens.back() = token;
  }
}

}  // namespace

void BoundedKeystrokeOverlayText::assign(
    std::u16string_view text) noexcept {
  const std::size_t bounded =
      std::min(text.size(), storage_.size());
  if (bounded != 0) {
    std::copy_n(text.begin(), bounded, storage_.begin());
  }
  size_ = static_cast<std::uint8_t>(bounded);
  truncated_ = text.size() > storage_.size();
}

void BoundedKeystrokeOverlayText::clear() noexcept {
  size_ = 0;
  truncated_ = false;
}

KeystrokeOverlayPrivacyDecision EvaluateKeystrokeOverlayPrivacy(
    const KeystrokeOverlayPrivacyContext& context) noexcept {
  const bool safe = context.overlay_enabled && context.context_known &&
      context.editable && !context.password && !context.protected_input &&
      !context.secure_desktop && !context.excluded_application;
  return safe ? KeystrokeOverlayPrivacyDecision::Allow
              : KeystrokeOverlayPrivacyDecision::Suppress;
}

KeystrokeOverlayMotionDecision ResolveKeystrokeOverlayMotion(
    const KeystrokeOverlayMotionContext& context) noexcept {
  using namespace std::chrono_literals;

  if (context.level == KeystrokeOverlayMotionLevel::Off) {
    return {0ms, false, false};
  }
  if (context.system_reduced_motion ||
      context.level == KeystrokeOverlayMotionLevel::Reduced) {
    return {90ms, false, false};
  }
  if (context.low_power_mode) {
    return {80ms, false, false};
  }
  if (context.level == KeystrokeOverlayMotionLevel::Full) {
    return {160ms, true, true};
  }
  if (context.rapid_input) {
    return {70ms, true, false};
  }
  return {140ms, true, true};
}

KeystrokeOverlayState KeystrokeOverlayReducer::Apply(
    const KeystrokeOverlayState& current,
    const KeystrokeOverlayEvent& event) const noexcept {
  KeystrokeOverlayState next = current;

  if (event.kind == KeystrokeOverlayEventKind::Suppressed) {
    next.generation = std::max(current.generation, event.generation);
    next.last_event = event.kind;
    ClearDisplayState(next, true);
    return next;
  }
  if (event.generation < current.generation) {
    return current;
  }

  next.generation = event.generation;
  next.last_event = event.kind;
  next.suppressed = false;
  switch (event.kind) {
    case KeystrokeOverlayEventKind::Token:
      AppendToken(next, event.text);
      next.visible = next.token_count != 0 || !next.text.empty();
      RecomputeTruncation(next);
      break;
    case KeystrokeOverlayEventKind::CompositionUpdated:
      next.text = event.text;
      next.visible = !next.text.empty() || next.token_count != 0;
      RecomputeTruncation(next);
      break;
    case KeystrokeOverlayEventKind::CompositionCommitted:
      next.text = event.text;
      AppendToken(next, event.text);
      next.visible = !next.text.empty() || next.token_count != 0;
      RecomputeTruncation(next);
      break;
    case KeystrokeOverlayEventKind::Cleared:
      ClearDisplayState(next, false);
      break;
    case KeystrokeOverlayEventKind::Suppressed:
      // Handled before stale-generation filtering so privacy always wins.
      break;
  }
  return next;
}

}  // namespace keyina::windows
