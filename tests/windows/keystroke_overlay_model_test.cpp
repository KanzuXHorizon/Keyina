#include <keyina/windows/keystroke_overlay_model.h>

#include "../test_support.h"

#include <chrono>
#include <string>
#include <type_traits>

namespace {

keyina::windows::KeystrokeOverlayEvent MakeTextEvent(
    keyina::windows::KeystrokeOverlayEventKind kind,
    std::uint64_t generation,
    std::u16string_view text) noexcept {
  keyina::windows::KeystrokeOverlayEvent event{};
  event.kind = kind;
  event.generation = generation;
  event.text.assign(text);
  return event;
}

}  // namespace

KEYINA_TEST(keystroke_overlay_composition_is_bounded_to_64_code_units) {
  keyina::windows::KeystrokeOverlayReducer reducer;
  std::u16string oversized(80, u'x');

  const auto state = reducer.Apply(
      {},
      MakeTextEvent(
          keyina::windows::KeystrokeOverlayEventKind::CompositionUpdated,
          1,
          oversized));

  KEYINA_EXPECT_EQ(state.text.size(), std::size_t{64});
  KEYINA_EXPECT_TRUE(state.text.truncated());
  KEYINA_EXPECT_TRUE(state.truncated);
  KEYINA_EXPECT_TRUE(state.visible);
}

KEYINA_TEST(keystroke_overlay_token_history_keeps_the_newest_16_tokens) {
  keyina::windows::KeystrokeOverlayReducer reducer;
  keyina::windows::KeystrokeOverlayState state{};

  for (std::uint64_t index = 0; index < 20; ++index) {
    const char16_t token = static_cast<char16_t>(u'a' + index);
    state = reducer.Apply(
        state,
        MakeTextEvent(
            keyina::windows::KeystrokeOverlayEventKind::Token,
            index + 1,
            std::u16string_view(&token, 1)));
  }

  KEYINA_EXPECT_EQ(
      state.token_count,
      keyina::windows::kMaximumOverlayTokens);
  KEYINA_EXPECT_EQ(state.tokens[0].view(), std::u16string_view(u"e"));
  KEYINA_EXPECT_EQ(state.tokens[15].view(), std::u16string_view(u"t"));
}

KEYINA_TEST(keystroke_overlay_suppression_clears_display_state_immediately) {
  keyina::windows::KeystrokeOverlayReducer reducer;
  auto state = reducer.Apply(
      {},
      MakeTextEvent(
          keyina::windows::KeystrokeOverlayEventKind::CompositionCommitted,
          10,
          u"nguyễn"));
  KEYINA_EXPECT_TRUE(state.visible);
  KEYINA_EXPECT_TRUE(state.token_count != 0);

  keyina::windows::KeystrokeOverlayEvent suppressed{};
  suppressed.kind =
      keyina::windows::KeystrokeOverlayEventKind::Suppressed;
  suppressed.generation = 1;
  state = reducer.Apply(state, suppressed);

  KEYINA_EXPECT_TRUE(!state.visible);
  KEYINA_EXPECT_TRUE(state.suppressed);
  KEYINA_EXPECT_TRUE(state.text.empty());
  KEYINA_EXPECT_EQ(state.token_count, std::size_t{0});
  KEYINA_EXPECT_EQ(state.generation, std::uint64_t{10});
}

KEYINA_TEST(keystroke_overlay_ignores_stale_generations_without_replay) {
  keyina::windows::KeystrokeOverlayReducer reducer;
  auto state = reducer.Apply(
      {},
      MakeTextEvent(
          keyina::windows::KeystrokeOverlayEventKind::CompositionUpdated,
          20,
          u"newest"));

  state = reducer.Apply(
      state,
      MakeTextEvent(
          keyina::windows::KeystrokeOverlayEventKind::CompositionUpdated,
          19,
          u"stale"));

  KEYINA_EXPECT_EQ(state.generation, std::uint64_t{20});
  KEYINA_EXPECT_EQ(state.text.view(), std::u16string_view(u"newest"));
}

KEYINA_TEST(keystroke_overlay_truncation_tracks_only_current_display_state) {
  keyina::windows::KeystrokeOverlayReducer reducer;
  std::u16string oversized(80, u'x');
  auto state = reducer.Apply(
      {},
      MakeTextEvent(
          keyina::windows::KeystrokeOverlayEventKind::CompositionUpdated,
          1,
          oversized));
  KEYINA_EXPECT_TRUE(state.truncated);

  state = reducer.Apply(
      state,
      MakeTextEvent(
          keyina::windows::KeystrokeOverlayEventKind::CompositionUpdated,
          2,
          u"short"));

  KEYINA_EXPECT_TRUE(!state.truncated);
  KEYINA_EXPECT_EQ(state.text.view(), std::u16string_view(u"short"));
}

KEYINA_TEST(keystroke_overlay_values_are_fixed_capacity_and_trivially_copyable) {
  KEYINA_EXPECT_TRUE(std::is_trivially_copyable_v<
      keyina::windows::BoundedKeystrokeOverlayText>);
  KEYINA_EXPECT_TRUE(std::is_trivially_copyable_v<
      keyina::windows::KeystrokeOverlayEvent>);
  KEYINA_EXPECT_TRUE(std::is_trivially_copyable_v<
      keyina::windows::KeystrokeOverlayState>);
}

KEYINA_TEST(keystroke_overlay_preferences_default_to_private_disabled_state) {
  const keyina::windows::KeystrokeOverlayPreferences preferences{};
  KEYINA_EXPECT_TRUE(!preferences.enabled);
  KEYINA_EXPECT_EQ(
      preferences.motion,
      keyina::windows::KeystrokeOverlayMotionLevel::Adaptive);
  KEYINA_EXPECT_EQ(preferences.size_percent, std::uint16_t{100});
  KEYINA_EXPECT_EQ(preferences.opacity_percent, std::uint16_t{100});
  KEYINA_EXPECT_EQ(
      preferences.hide_delay_milliseconds,
      std::uint16_t{900});
  KEYINA_EXPECT_EQ(
      preferences.fallback_corner,
      keyina::windows::KeystrokeOverlayFallbackCorner::BottomRight);
  KEYINA_EXPECT_TRUE(!preferences.presentation_mode);
}

KEYINA_TEST(keystroke_overlay_privacy_allows_only_known_editable_context) {
  keyina::windows::KeystrokeOverlayPrivacyContext context{};
  context.overlay_enabled = true;
  context.context_known = true;
  context.editable = true;

  KEYINA_EXPECT_EQ(
      keyina::windows::EvaluateKeystrokeOverlayPrivacy(context),
      keyina::windows::KeystrokeOverlayPrivacyDecision::Allow);
}

KEYINA_TEST(keystroke_overlay_privacy_suppresses_every_sensitive_context) {
  using keyina::windows::EvaluateKeystrokeOverlayPrivacy;
  using keyina::windows::KeystrokeOverlayPrivacyContext;
  using keyina::windows::KeystrokeOverlayPrivacyDecision;

  KeystrokeOverlayPrivacyContext safe{};
  safe.overlay_enabled = true;
  safe.context_known = true;
  safe.editable = true;

  auto expect_suppressed = [&](KeystrokeOverlayPrivacyContext context) {
    KEYINA_EXPECT_EQ(
        EvaluateKeystrokeOverlayPrivacy(context),
        KeystrokeOverlayPrivacyDecision::Suppress);
  };

  auto disabled = safe;
  disabled.overlay_enabled = false;
  expect_suppressed(disabled);

  auto unknown = safe;
  unknown.context_known = false;
  expect_suppressed(unknown);

  auto non_editable = safe;
  non_editable.editable = false;
  expect_suppressed(non_editable);

  auto password = safe;
  password.password = true;
  expect_suppressed(password);

  auto protected_input = safe;
  protected_input.protected_input = true;
  expect_suppressed(protected_input);

  auto secure_desktop = safe;
  secure_desktop.secure_desktop = true;
  expect_suppressed(secure_desktop);

  auto excluded = safe;
  excluded.excluded_application = true;
  expect_suppressed(excluded);
}

KEYINA_TEST(keystroke_overlay_motion_off_is_immediate_and_static) {
  keyina::windows::KeystrokeOverlayMotionContext context{};
  context.level = keyina::windows::KeystrokeOverlayMotionLevel::Off;

  const auto decision =
      keyina::windows::ResolveKeystrokeOverlayMotion(context);

  KEYINA_EXPECT_EQ(decision.duration, std::chrono::milliseconds{0});
  KEYINA_EXPECT_TRUE(!decision.translate);
  KEYINA_EXPECT_TRUE(!decision.emphasize_changed_glyphs);
}

KEYINA_TEST(keystroke_overlay_reduced_motion_is_crossfade_only) {
  keyina::windows::KeystrokeOverlayMotionContext context{};
  context.level = keyina::windows::KeystrokeOverlayMotionLevel::Full;
  context.system_reduced_motion = true;

  const auto decision =
      keyina::windows::ResolveKeystrokeOverlayMotion(context);

  KEYINA_EXPECT_TRUE(decision.duration > std::chrono::milliseconds{0});
  KEYINA_EXPECT_TRUE(!decision.translate);
  KEYINA_EXPECT_TRUE(!decision.emphasize_changed_glyphs);
}

KEYINA_TEST(keystroke_overlay_adaptive_motion_shortens_under_rapid_input) {
  keyina::windows::KeystrokeOverlayMotionContext normal{};
  normal.level = keyina::windows::KeystrokeOverlayMotionLevel::Adaptive;
  auto rapid = normal;
  rapid.rapid_input = true;

  const auto normal_decision =
      keyina::windows::ResolveKeystrokeOverlayMotion(normal);
  const auto rapid_decision =
      keyina::windows::ResolveKeystrokeOverlayMotion(rapid);

  KEYINA_EXPECT_TRUE(rapid_decision.duration < normal_decision.duration);
  KEYINA_EXPECT_TRUE(normal_decision.translate);
  KEYINA_EXPECT_TRUE(rapid_decision.translate);
}

KEYINA_TEST(keystroke_overlay_low_power_motion_avoids_translation) {
  keyina::windows::KeystrokeOverlayMotionContext context{};
  context.level = keyina::windows::KeystrokeOverlayMotionLevel::Adaptive;
  context.low_power_mode = true;

  const auto decision =
      keyina::windows::ResolveKeystrokeOverlayMotion(context);

  KEYINA_EXPECT_TRUE(decision.duration > std::chrono::milliseconds{0});
  KEYINA_EXPECT_TRUE(!decision.translate);
  KEYINA_EXPECT_TRUE(!decision.emphasize_changed_glyphs);
}

KEYINA_TEST(keystroke_overlay_clear_resets_tokens_and_composition) {
  keyina::windows::KeystrokeOverlayReducer reducer;
  auto state = reducer.Apply(
      {},
      MakeTextEvent(
          keyina::windows::KeystrokeOverlayEventKind::Token,
          1,
          u"a"));
  state = reducer.Apply(
      state,
      MakeTextEvent(
          keyina::windows::KeystrokeOverlayEventKind::CompositionUpdated,
          2,
          u"á"));

  keyina::windows::KeystrokeOverlayEvent clear{};
  clear.kind = keyina::windows::KeystrokeOverlayEventKind::Cleared;
  clear.generation = 3;
  state = reducer.Apply(state, clear);

  KEYINA_EXPECT_TRUE(!state.visible);
  KEYINA_EXPECT_TRUE(!state.suppressed);
  KEYINA_EXPECT_TRUE(state.text.empty());
  KEYINA_EXPECT_EQ(state.token_count, std::size_t{0});
  KEYINA_EXPECT_EQ(state.generation, std::uint64_t{3});
}
