#include <keyina/windows/keystroke_overlay_model.h>

#include "../test_support.h"

#include <string>
#include <type_traits>

using namespace keyina::windows;

KEYINA_TEST(keystroke_overlay_model_bounds_composition_text) {
  KeystrokeOverlayReducer reducer;
  KeystrokeOverlayEvent event{};
  event.kind = KeystrokeOverlayEventKind::CompositionUpdated;
  event.SetText(std::u16string(80, u'x'));
  event.generation = 1;

  const auto state = reducer.Apply({}, event);

  KEYINA_EXPECT_EQ(state.text.size(), 64u);
  KEYINA_EXPECT_TRUE(state.truncated);
  KEYINA_EXPECT_TRUE(state.visible);
}

KEYINA_TEST(keystroke_overlay_model_retains_newest_sixteen_tokens) {
  KeystrokeOverlayReducer reducer;
  KeystrokeOverlayState state{};
  for (std::uint64_t index = 0; index < 20; ++index) {
    KeystrokeOverlayEvent event{};
    event.kind = KeystrokeOverlayEventKind::Token;
    event.token = static_cast<char16_t>(u'a' + index);
    event.generation = index + 1;
    state = reducer.Apply(state, event);
  }

  KEYINA_EXPECT_EQ(state.token_count, 16u);
  KEYINA_EXPECT_EQ(state.tokens.front(), u'e');
  KEYINA_EXPECT_EQ(state.tokens.back(), u't');
  KEYINA_EXPECT_TRUE(state.truncated);
}

KEYINA_TEST(keystroke_overlay_model_suppression_clears_displayable_state) {
  KeystrokeOverlayReducer reducer;
  KeystrokeOverlayEvent text{};
  text.kind = KeystrokeOverlayEventKind::CompositionUpdated;
  text.SetText(u"nguyen");
  text.generation = 1;
  const auto visible = reducer.Apply({}, text);

  KeystrokeOverlayEvent suppressed{};
  suppressed.kind = KeystrokeOverlayEventKind::Suppressed;
  suppressed.generation = 2;
  const auto state = reducer.Apply(visible, suppressed);

  KEYINA_EXPECT_TRUE(!state.visible);
  KEYINA_EXPECT_TRUE(state.text.empty());
  KEYINA_EXPECT_EQ(state.token_count, 0u);
}

KEYINA_TEST(keystroke_overlay_model_ignores_stale_generation) {
  KeystrokeOverlayReducer reducer;
  KeystrokeOverlayEvent newest{};
  newest.kind = KeystrokeOverlayEventKind::CompositionUpdated;
  newest.SetText(u"nguyễn");
  newest.generation = 7;
  const auto current = reducer.Apply({}, newest);

  KeystrokeOverlayEvent stale{};
  stale.kind = KeystrokeOverlayEventKind::CompositionUpdated;
  stale.SetText(u"old");
  stale.generation = 6;
  const auto state = reducer.Apply(current, stale);

  KEYINA_EXPECT_EQ(state.text.View(), std::u16string_view(u"nguyễn"));
  KEYINA_EXPECT_EQ(state.generation, 7u);
}

KEYINA_TEST(keystroke_overlay_state_is_fixed_capacity_and_trivially_copyable) {
  KEYINA_EXPECT_TRUE(
      std::is_trivially_copyable_v<BoundedKeystrokeOverlayText>);
  KEYINA_EXPECT_TRUE(std::is_trivially_copyable_v<KeystrokeOverlayEvent>);
  KEYINA_EXPECT_TRUE(std::is_trivially_copyable_v<KeystrokeOverlayState>);
}

KEYINA_TEST(keystroke_overlay_text_never_splits_a_surrogate_pair) {
  std::u16string value(63, u'x');
  value.push_back(static_cast<char16_t>(0xD83D));
  value.push_back(static_cast<char16_t>(0xDE00));
  BoundedKeystrokeOverlayText text{};

  text.Assign(value);

  KEYINA_EXPECT_EQ(text.size(), std::size_t{63});
  KEYINA_EXPECT_TRUE(text.truncated());
  KEYINA_EXPECT_EQ(text.View().back(), u'x');
}

KEYINA_TEST(keystroke_overlay_append_preserves_surrogate_pairs_at_capacity) {
  BoundedKeystrokeOverlayText text{};
  text.Assign(std::u16string(63, u'x'));

  KEYINA_EXPECT_TRUE(
      !text.Append(static_cast<char16_t>(0xD83D)));
  KEYINA_EXPECT_EQ(text.size(), std::size_t{63});
  KEYINA_EXPECT_TRUE(text.truncated());

  text.Assign(std::u16string(62, u'x'));
  KEYINA_EXPECT_TRUE(text.Append(static_cast<char16_t>(0xD83D)));
  KEYINA_EXPECT_TRUE(text.Append(static_cast<char16_t>(0xDE00)));
  KEYINA_EXPECT_EQ(text.size(), std::size_t{64});
  KEYINA_EXPECT_EQ(text.View()[62], static_cast<char16_t>(0xD83D));
  KEYINA_EXPECT_EQ(text.View()[63], static_cast<char16_t>(0xDE00));

  text.Clear();
  KEYINA_EXPECT_TRUE(!text.Append(static_cast<char16_t>(0xDE00)));
  KEYINA_EXPECT_TRUE(text.empty());
  KEYINA_EXPECT_TRUE(text.truncated());
}

KEYINA_TEST(keystroke_overlay_privacy_fails_closed) {
  KEYINA_EXPECT_EQ(
      EvaluateKeystrokeOverlayPrivacy({true, true, false, false, false, false}),
      KeystrokeOverlayPrivacyDecision::Allow);
  KEYINA_EXPECT_EQ(
      EvaluateKeystrokeOverlayPrivacy({false, true, false, false, false, false}),
      KeystrokeOverlayPrivacyDecision::Suppress);
  KEYINA_EXPECT_EQ(
      EvaluateKeystrokeOverlayPrivacy({true, true, true, false, false, false}),
      KeystrokeOverlayPrivacyDecision::Suppress);
  KEYINA_EXPECT_EQ(
      EvaluateKeystrokeOverlayPrivacy({true, true, false, true, false, false}),
      KeystrokeOverlayPrivacyDecision::Suppress);
  KEYINA_EXPECT_EQ(
      EvaluateKeystrokeOverlayPrivacy({true, true, false, false, true, false}),
      KeystrokeOverlayPrivacyDecision::Suppress);
  KEYINA_EXPECT_EQ(
      EvaluateKeystrokeOverlayPrivacy({true, true, false, false, false, true}),
      KeystrokeOverlayPrivacyDecision::Suppress);
}

KEYINA_TEST(keystroke_overlay_motion_adapts_without_queueing) {
  const auto normal = ResolveKeystrokeOverlayMotion({});
  KEYINA_EXPECT_TRUE(normal.duration.count() > 0);
  KEYINA_EXPECT_TRUE(normal.translate);
  KEYINA_EXPECT_TRUE(normal.emphasize_changed_glyphs);

  const auto rapid = ResolveKeystrokeOverlayMotion(
      {KeystrokeOverlayMotionLevel::Adaptive, true, false, false});
  KEYINA_EXPECT_TRUE(rapid.duration < normal.duration);
  KEYINA_EXPECT_TRUE(!rapid.emphasize_changed_glyphs);

  const auto reduced = ResolveKeystrokeOverlayMotion(
      {KeystrokeOverlayMotionLevel::Full, false, false, true});
  KEYINA_EXPECT_TRUE(!reduced.translate);

  const auto off = ResolveKeystrokeOverlayMotion(
      {KeystrokeOverlayMotionLevel::Off, false, false, false});
  KEYINA_EXPECT_EQ(off.duration.count(), 0);
  KEYINA_EXPECT_TRUE(!off.translate);
}

KEYINA_TEST(keystroke_overlay_preferences_validate_budgets) {
  KEYINA_EXPECT_TRUE(KeystrokeOverlayPreferences{}.IsValid());
  auto invalid = KeystrokeOverlayPreferences{};
  invalid.opacity_percent = 24;
  KEYINA_EXPECT_TRUE(!invalid.IsValid());
  invalid = KeystrokeOverlayPreferences{};
  invalid.hide_delay_milliseconds = 2001;
  KEYINA_EXPECT_TRUE(!invalid.IsValid());
}
