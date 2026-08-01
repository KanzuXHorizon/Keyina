#include <keyina/windows/keystroke_overlay_model.h>

#include "../test_support.h"

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
