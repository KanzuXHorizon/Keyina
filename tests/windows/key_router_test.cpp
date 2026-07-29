#include <windows.h>

#include <array>

#include <keyina/tsf/key_router.h>

#include "../test_support.h"

using keyina::tsf::KeyRouteKind;
using keyina::tsf::KeyRoutingInput;

KEYINA_TEST(routes_ascii_letters_with_shift_and_caps_lock) {
  const auto lower = keyina::tsf::RouteKey({.virtual_key = 'A'});
  KEYINA_EXPECT_EQ(lower.kind, KeyRouteKind::Character);
  KEYINA_EXPECT_EQ(lower.character, U'a');

  const auto shifted =
      keyina::tsf::RouteKey({.virtual_key = 'A', .shift = true});
  KEYINA_EXPECT_EQ(shifted.character, U'A');

  const auto caps =
      keyina::tsf::RouteKey({.virtual_key = 'A', .caps_lock = true});
  KEYINA_EXPECT_EQ(caps.character, U'A');

  const auto shifted_caps = keyina::tsf::RouteKey(
      {.virtual_key = 'A', .shift = true, .caps_lock = true});
  KEYINA_EXPECT_EQ(shifted_caps.character, U'a');
}

KEYINA_TEST(routes_backspace_only_for_owned_composition) {
  KEYINA_EXPECT_EQ(
      keyina::tsf::RouteKey({.virtual_key = VK_BACK}).kind,
      KeyRouteKind::PassThrough);
  KEYINA_EXPECT_EQ(keyina::tsf::RouteKey(
                       {.virtual_key = VK_BACK, .active_composition = true})
                       .kind,
                   KeyRouteKind::Backspace);
}

KEYINA_TEST(routes_whitespace_and_sentence_punctuation_as_boundaries) {
  struct BoundaryCase {
    unsigned int virtual_key;
    bool shift;
  };
  constexpr std::array<BoundaryCase, 6> cases = {{
      {VK_SPACE, false},
      {VK_TAB, false},
      {VK_RETURN, false},
      {VK_OEM_COMMA, false},
      {VK_OEM_1, false},
      {VK_OEM_2, true},
  }};
  for (const auto& test : cases) {
    const auto inactive = keyina::tsf::RouteKey(
        {.virtual_key = test.virtual_key, .shift = test.shift});
    KEYINA_EXPECT_EQ(inactive.kind, KeyRouteKind::PassThrough);

    const auto active = keyina::tsf::RouteKey(
        {.virtual_key = test.virtual_key,
         .shift = test.shift,
         .active_composition = true});
    KEYINA_EXPECT_EQ(active.kind, KeyRouteKind::Boundary);
    KEYINA_EXPECT_TRUE(active.character != U'\0');
  }
}

KEYINA_TEST(routes_technical_token_characters_while_composing) {
  struct TechnicalCase {
    unsigned int virtual_key;
    bool shift;
    char32_t expected;
  };
  constexpr std::array<TechnicalCase, 9> cases = {{
      {'2', false, U'2'},
      {VK_OEM_MINUS, true, U'_'},
      {'2', true, U'@'},
      {VK_OEM_2, false, U'/'},
      {VK_OEM_5, false, U'\\'},
      {VK_OEM_1, true, U':'},
      {VK_OEM_PERIOD, false, U'.'},
      {VK_OEM_MINUS, false, U'-'},
      {VK_OEM_PLUS, false, U'='},
  }};

  for (const auto& test : cases) {
    const auto route = keyina::tsf::RouteKey(
        {.virtual_key = test.virtual_key,
         .shift = test.shift,
         .active_composition = true});
    KEYINA_EXPECT_EQ(route.kind, KeyRouteKind::Character);
    KEYINA_EXPECT_EQ(route.character, test.expected);
  }
}

KEYINA_TEST(modifier_chords_reset_owned_state_without_eating_the_chord) {
  const auto inactive = keyina::tsf::RouteKey(
      {.virtual_key = 'C', .control = true});
  KEYINA_EXPECT_EQ(inactive.kind, KeyRouteKind::PassThrough);

  const auto active = keyina::tsf::RouteKey(
      {.virtual_key = 'C', .control = true, .active_composition = true});
  KEYINA_EXPECT_EQ(active.kind, KeyRouteKind::Reset);
}

KEYINA_TEST(navigation_and_escape_reset_only_active_compositions) {
  constexpr std::array<unsigned int, 5> keys = {
      VK_ESCAPE, VK_LEFT, VK_RIGHT, VK_HOME, VK_END,
  };
  for (const unsigned int key : keys) {
    KEYINA_EXPECT_EQ(keyina::tsf::RouteKey({.virtual_key = key}).kind,
                     KeyRouteKind::PassThrough);
    KEYINA_EXPECT_EQ(keyina::tsf::RouteKey(
                         {.virtual_key = key, .active_composition = true})
                         .kind,
                     KeyRouteKind::Reset);
  }
}

KEYINA_TEST(unsupported_keys_pass_through) {
  const auto route = keyina::tsf::RouteKey({.virtual_key = VK_F7});
  KEYINA_EXPECT_EQ(route.kind, KeyRouteKind::PassThrough);
  KEYINA_EXPECT_EQ(route.character, U'\0');
}
