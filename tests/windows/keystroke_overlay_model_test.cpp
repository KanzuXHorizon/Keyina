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

KEYINA_TEST(keystroke_overlay_token_snapshot_survives_overwritten_updates) {
  KeystrokeOverlayReducer reducer;
  KeystrokeOverlayState state{};

  KeystrokeOverlayEvent old_word{};
  old_word.kind = KeystrokeOverlayEventKind::Token;
  old_word.SetText(u"old");
  old_word.token = u'd';
  old_word.generation = 1;
  state = reducer.Apply(state, old_word);

  KeystrokeOverlayEvent new_word{};
  new_word.kind = KeystrokeOverlayEventKind::Token;
  new_word.SetText(u"new");
  new_word.token = u'w';
  new_word.generation = 4;
  state = reducer.Apply(state, new_word);

  KEYINA_EXPECT_EQ(state.token_count, std::size_t{3});
  KEYINA_EXPECT_EQ(state.tokens[0], u'n');
  KEYINA_EXPECT_EQ(state.tokens[1], u'e');
  KEYINA_EXPECT_EQ(state.tokens[2], u'w');
  KEYINA_EXPECT_TRUE(state.text.empty());
}

KEYINA_TEST(keystroke_overlay_token_snapshot_keeps_the_newest_sixteen_units) {
  KeystrokeOverlayReducer reducer;
  KeystrokeOverlayEvent event{};
  event.kind = KeystrokeOverlayEventKind::Token;
  event.SetText(u"abcdefghijklmnopqrst");
  event.token = u't';
  event.generation = 1;

  const auto state = reducer.Apply({}, event);

  KEYINA_EXPECT_EQ(state.token_count, std::size_t{16});
  KEYINA_EXPECT_EQ(state.tokens.front(), u'e');
  KEYINA_EXPECT_EQ(state.tokens[15], u't');
  KEYINA_EXPECT_TRUE(state.truncated);
}

KEYINA_TEST(keystroke_overlay_composition_replaces_raw_token_history) {
  KeystrokeOverlayReducer reducer;
  KeystrokeOverlayState state{};
  for (std::uint64_t index = 0; index < 2; ++index) {
    KeystrokeOverlayEvent token{};
    token.kind = KeystrokeOverlayEventKind::Token;
    token.token = index == 0 ? u'n' : u'g';
    token.generation = index + 1;
    state = reducer.Apply(state, token);
  }

  KeystrokeOverlayEvent composition{};
  composition.kind = KeystrokeOverlayEventKind::CompositionUpdated;
  composition.SetText(u"ng");
  composition.generation = 3;
  state = reducer.Apply(state, composition);

  KEYINA_EXPECT_EQ(state.token_count, std::size_t{0});
  KEYINA_EXPECT_EQ(state.text.View(), std::u16string_view(u"ng"));
}

KEYINA_TEST(keystroke_overlay_token_after_commit_starts_a_fresh_stream) {
  KeystrokeOverlayReducer reducer;
  KeystrokeOverlayEvent committed{};
  committed.kind = KeystrokeOverlayEventKind::CompositionCommitted;
  committed.SetText(u"nguyễn");
  committed.generation = 1;
  const auto previous = reducer.Apply({}, committed);

  KeystrokeOverlayEvent token{};
  token.kind = KeystrokeOverlayEventKind::Token;
  token.token = u'm';
  token.generation = 2;
  const auto state = reducer.Apply(previous, token);

  KEYINA_EXPECT_TRUE(state.text.empty());
  KEYINA_EXPECT_EQ(state.token_count, std::size_t{1});
  KEYINA_EXPECT_EQ(state.tokens[0], u'm');
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

KEYINA_TEST(keystroke_overlay_assign_does_not_split_combining_sequence) {
  std::u16string value(63, u'x');
  value.push_back(u'a');
  value.push_back(static_cast<char16_t>(0x0301));
  BoundedKeystrokeOverlayText text{};

  text.Assign(value);

  KEYINA_EXPECT_EQ(text.size(), std::size_t{63});
  KEYINA_EXPECT_EQ(text.View().back(), u'x');
  KEYINA_EXPECT_TRUE(text.truncated());
}

KEYINA_TEST(keystroke_overlay_append_keeps_the_newest_text_at_capacity) {
  BoundedKeystrokeOverlayText text{};
  text.Assign(std::u16string(64, u'x'));

  KEYINA_EXPECT_TRUE(text.Append(u'y'));
  KEYINA_EXPECT_EQ(text.size(), std::size_t{64});
  KEYINA_EXPECT_EQ(text.View().front(), u'x');
  KEYINA_EXPECT_EQ(text.View().back(), u'y');
  KEYINA_EXPECT_TRUE(text.truncated());
}

KEYINA_TEST(keystroke_overlay_append_discards_a_complete_combining_sequence) {
  std::u16string value{u'a', static_cast<char16_t>(0x0301)};
  value.append(62, u'x');
  BoundedKeystrokeOverlayText text{};
  text.Assign(value);

  KEYINA_EXPECT_TRUE(text.Append(u'y'));
  KEYINA_EXPECT_EQ(text.View().front(), u'x');
  KEYINA_EXPECT_EQ(text.View().back(), u'y');
  KEYINA_EXPECT_EQ(text.size(), std::size_t{63});
  KEYINA_EXPECT_TRUE(text.truncated());
}

KEYINA_TEST(keystroke_overlay_append_preserves_surrogate_pairs_at_capacity) {
  BoundedKeystrokeOverlayText text{};
  text.Assign(std::u16string(63, u'x'));

  KEYINA_EXPECT_TRUE(text.Append(static_cast<char16_t>(0xD83D)));
  KEYINA_EXPECT_EQ(text.size(), std::size_t{63});
  KEYINA_EXPECT_EQ(text.View().front(), u'x');
  KEYINA_EXPECT_TRUE(text.Append(static_cast<char16_t>(0xDE00)));
  KEYINA_EXPECT_EQ(text.size(), std::size_t{64});
  KEYINA_EXPECT_EQ(text.View()[62], static_cast<char16_t>(0xD83D));
  KEYINA_EXPECT_EQ(text.View()[63], static_cast<char16_t>(0xDE00));
  KEYINA_EXPECT_TRUE(text.truncated());

  text.Clear();
  KEYINA_EXPECT_TRUE(!text.Append(static_cast<char16_t>(0xDE00)));
  KEYINA_EXPECT_TRUE(text.empty());
  KEYINA_EXPECT_TRUE(text.truncated());
}

KEYINA_TEST(keystroke_overlay_assign_does_not_split_flag_emoji) {
  std::u16string value(61, u'x');
  value.append({
      static_cast<char16_t>(0xD83C), static_cast<char16_t>(0xDDFB),
      static_cast<char16_t>(0xD83C), static_cast<char16_t>(0xDDF3)});
  BoundedKeystrokeOverlayText text{};

  text.Assign(value);

  KEYINA_EXPECT_EQ(text.size(), std::size_t{61});
  KEYINA_EXPECT_EQ(text.View().back(), u'x');
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

KEYINA_TEST(keystroke_overlay_composition_stays_in_text_mode_after_transform) {
  KEYINA_EXPECT_TRUE(
      ShouldShowKeystrokeOverlayCompositionText(u"nguyễn", false));
  KEYINA_EXPECT_TRUE(
      ShouldShowKeystrokeOverlayCompositionText(u"nguyen", true));
  KEYINA_EXPECT_TRUE(
      !ShouldShowKeystrokeOverlayCompositionText(u"nguyen", false));
}

KEYINA_TEST(keystroke_overlay_clears_on_shortcuts_and_navigation) {
  KEYINA_EXPECT_TRUE(
      ShouldClearKeystrokeOverlayComposition(0x2E, true, false, false));
  KEYINA_EXPECT_TRUE(
      ShouldClearKeystrokeOverlayComposition(0x2E, false, false, false));
  KEYINA_EXPECT_TRUE(
      ShouldClearKeystrokeOverlayComposition(0x25, false, false, false));
  KEYINA_EXPECT_TRUE(
      !ShouldClearKeystrokeOverlayComposition(0x08, false, false, false));
  KEYINA_EXPECT_TRUE(
      !ShouldClearKeystrokeOverlayComposition(0x41, false, false, false));
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
