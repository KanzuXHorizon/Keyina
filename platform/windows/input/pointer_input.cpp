#include <keyina/windows/pointer_input.h>

namespace keyina::windows {

bool IsPointerResetButtonFlags(USHORT flags) noexcept {
  constexpr USHORT kResetFlags =
      RI_MOUSE_LEFT_BUTTON_DOWN |
      RI_MOUSE_RIGHT_BUTTON_DOWN |
      RI_MOUSE_MIDDLE_BUTTON_DOWN |
      RI_MOUSE_BUTTON_4_DOWN |
      RI_MOUSE_BUTTON_5_DOWN |
      RI_MOUSE_WHEEL |
      RI_MOUSE_HWHEEL;
  return (flags & kResetFlags) != 0;
}

}  // namespace keyina::windows
