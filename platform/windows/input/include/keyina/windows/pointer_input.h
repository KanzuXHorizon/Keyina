#pragma once

#include <windows.h>

namespace keyina::windows {

[[nodiscard]] bool IsPointerResetButtonFlags(USHORT flags) noexcept;

}  // namespace keyina::windows
