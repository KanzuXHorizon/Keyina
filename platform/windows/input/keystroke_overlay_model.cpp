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
    std::u16string_view text,
    bool force_truncated) noexcept {
  const std::size_t bounded =
      std::min(text.size(), storage_.size());
  if (bounded != 0) {
    std::copy_n(text.begin(), bounded, storage_.begin());
  }
  size_ = static_cast<std::uint8_t>(bounded);
  truncated_ = force_truncated || text.size() > storage_.size();
}

void BoundedKeystrokeOverlayText::clear() noexcept {
  size_ = 0;
  truncated_ = false;
}

void AssignKeystrokeOverlayText(
    std::u32string_view text,
    BoundedKeystrokeOverlayText& output) noexcept {
  std::array<char16_t, kMaximumOverlayCodeUnits> units{};
  std::size_t count = 0;
  bool truncated = false;
  for (const char32_t codepoint : text) {
    if (codepoint <= 0xD7FF ||
        (codepoint >= 0xE000 && codepoint <= 0xFFFF)) {
      if (count == units.size()) {
        truncated = true;
        break;
      }
      units[count++] = static_cast<char16_t>(codepoint);
      continue;
    }
    if (codepoint < 0x10000 || codepoint > 0x10FFFF) {
      continue;
    }
    if (count + 2 > units.size()) {
      truncated = true;
      break;
    }
    const char32_t adjusted = codepoint - 0x10000;
    units[count++] = static_cast<char16_t>(0xD800 + (adjusted >> 10));
    units[count++] = static_cast<char16_t>(0xDC00 + (adjusted & 0x3FF));
  }
  output.assign(
      std::u16string_view(units.data(), count),
      truncated);
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

bool KeystrokeOverlayLatestSlot::Publish(
    const KeystrokeOverlayDelivery& delivery) noexcept {
  const bool replaced = pending_;
  latest_ = delivery;
  if (latest_.event.kind == KeystrokeOverlayEventKind::Suppressed) {
    latest_.event.text.clear();
  }
  pending_ = true;
  return replaced;
}

bool KeystrokeOverlayLatestSlot::Consume(
    KeystrokeOverlayDelivery& delivery) noexcept {
  if (!pending_) {
    return false;
  }
  delivery = latest_;
  latest_ = {};
  pending_ = false;
  return true;
}

void KeystrokeOverlayLatestSlot::Reset() noexcept {
  latest_ = {};
  pending_ = false;
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
