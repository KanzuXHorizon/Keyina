#pragma once

#include <keyina/windows/resident_input_controller.h>

#include <windows.h>

#include <cstddef>
#include <span>

namespace keyina::windows {

inline constexpr ULONG_PTR kKeyinaInjectionMarker =
    static_cast<ULONG_PTR>(0x4B4559494E41ULL);

[[nodiscard]] std::size_t BuildKeyboardInputSequence(
    const InputDecision& decision,
    std::span<INPUT> destination) noexcept;

}  // namespace keyina::windows
