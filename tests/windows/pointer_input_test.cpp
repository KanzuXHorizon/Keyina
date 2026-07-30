#include <keyina/windows/pointer_input.h>

#include "../test_support.h"

#include <array>
#include <cstdint>

KEYINA_TEST(native_pointer_movement_never_resets_composition) {
  std::uint64_t reset_count = 0;
  for (std::size_t index = 0; index < 1'000'000; ++index) {
    reset_count += static_cast<std::uint64_t>(
        keyina::windows::IsPointerResetButtonFlags(0));
  }
  KEYINA_EXPECT_EQ(reset_count, std::uint64_t{0});
  KEYINA_EXPECT_TRUE(
      !keyina::windows::IsPointerResetButtonFlags(RI_MOUSE_LEFT_BUTTON_UP));
  KEYINA_EXPECT_TRUE(
      !keyina::windows::IsPointerResetButtonFlags(RI_MOUSE_RIGHT_BUTTON_UP));
}

KEYINA_TEST(native_pointer_click_and_wheel_reset_composition) {
  constexpr std::array<USHORT, 7> reset_flags{
      RI_MOUSE_LEFT_BUTTON_DOWN,
      RI_MOUSE_RIGHT_BUTTON_DOWN,
      RI_MOUSE_MIDDLE_BUTTON_DOWN,
      RI_MOUSE_BUTTON_4_DOWN,
      RI_MOUSE_BUTTON_5_DOWN,
      RI_MOUSE_WHEEL,
      RI_MOUSE_HWHEEL,
  };
  for (const auto flags : reset_flags) {
    KEYINA_EXPECT_TRUE(
        keyina::windows::IsPointerResetButtonFlags(flags));
  }
}
