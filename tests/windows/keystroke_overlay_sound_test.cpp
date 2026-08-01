#include <keyina/windows/keystroke_overlay_sound.h>

#include "../test_support.h"

KEYINA_TEST(keystroke_overlay_sound_defaults_to_disabled) {
  keyina::windows::KeystrokeOverlaySoundPlayer player;
  player.Configure(keyina::windows::KeystrokeOverlayPreferences{});
  KEYINA_EXPECT_TRUE(!player.enabled_for_testing());
}

KEYINA_TEST(keystroke_overlay_sound_requires_overlay_and_positive_volume) {
  keyina::windows::KeystrokeOverlaySoundPlayer player;
  keyina::windows::KeystrokeOverlayPreferences preferences{};
  preferences.enabled = true;
  preferences.per_key_sound_enabled = true;
  preferences.sound_volume_percent = 30;
  player.Configure(preferences);
  KEYINA_EXPECT_TRUE(player.enabled_for_testing());

  preferences.sound_volume_percent = 0;
  player.Configure(preferences);
  KEYINA_EXPECT_TRUE(!player.enabled_for_testing());
}

KEYINA_TEST(keystroke_overlay_sound_stops_on_privacy_suppression) {
  keyina::windows::KeystrokeOverlaySoundPlayer player;
  keyina::windows::KeystrokeOverlayPreferences preferences{};
  preferences.enabled = true;
  preferences.per_key_sound_enabled = true;
  player.Configure(preferences);
  player.Stop();
  KEYINA_EXPECT_TRUE(player.enabled_for_testing());
}
