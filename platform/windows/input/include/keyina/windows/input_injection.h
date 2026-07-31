#pragma once

#include <keyina/windows/resident_input_controller.h>

#include <windows.h>

#include <cstddef>
#include <span>
#include <string_view>

namespace keyina::windows {

inline constexpr ULONG_PTR kKeyinaInjectionMarker =
    static_cast<ULONG_PTR>(0x4B4559494E41ULL);

[[nodiscard]] std::size_t BuildKeyboardInputSequence(
    const InputDecision& decision,
    std::span<INPUT> destination) noexcept;

[[nodiscard]] std::size_t BuildClipboardPasteSequence(
    const InputDecision& decision,
    std::span<INPUT> destination) noexcept;

[[nodiscard]] std::size_t BuildSelectionReplacementSequence(
    const InputDecision& decision,
    std::span<INPUT> destination) noexcept;

[[nodiscard]] bool ShouldRestoreClipboard(
    DWORD owned_sequence,
    DWORD current_sequence) noexcept;

[[nodiscard]] bool ShouldDeferInputForWindowClass(
    std::wstring_view class_name) noexcept;

}  // namespace keyina::windows
