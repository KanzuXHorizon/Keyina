#pragma once

#include <keyina/windows/resident_input_controller.h>

#include <windows.h>

#include <cstddef>
#include <cstdint>
#include <span>
#include <string_view>

namespace keyina::windows {

inline constexpr ULONG_PTR kKeyinaInjectionMarker =
    static_cast<ULONG_PTR>(0x4B4559494E41ULL);

enum class TextDeliveryMode : std::uint8_t {
  Keyboard = 0,
  SelectionReplacement,
  Clipboard,
};

[[nodiscard]] TextDeliveryMode ChooseTextDeliveryMode(
    bool clipboard_compatibility_enabled,
    bool chromium_target) noexcept;

[[nodiscard]] bool ShouldOwnTextStream(
    bool vietnamese_enabled,
    bool bypass_typing,
    bool clipboard_delivery,
    bool selection_replacement_target) noexcept;

[[nodiscard]] std::size_t BuildLiteralUnicodeInputSequence(
    char32_t character,
    std::span<INPUT> destination) noexcept;

[[nodiscard]] std::size_t BuildKeyboardInputSequence(
    const InputDecision& decision,
    std::span<INPUT> destination) noexcept;

[[nodiscard]] std::size_t BuildClipboardPasteSequence(
    const InputDecision& decision,
    std::span<INPUT> destination) noexcept;

[[nodiscard]] std::size_t BuildSelectionReplacementSequence(
    const InputDecision& decision,
    std::span<INPUT> destination) noexcept;

// Builds best-effort key-up events for every keyboard key-down accepted by a
// partial SendInput call. The sequence is reversed so ordinary keys are
// released before their modifiers.
[[nodiscard]] std::size_t BuildPartialInputRecoverySequence(
    std::span<const INPUT> submitted,
    std::size_t inserted_count,
    std::span<INPUT> destination) noexcept;

[[nodiscard]] bool ShouldRestoreClipboard(
    DWORD owned_sequence,
    DWORD current_sequence) noexcept;

[[nodiscard]] bool IsStandardEditableWindowClass(
    std::wstring_view class_name) noexcept;

[[nodiscard]] bool RequiresSelectionReplacementForWindowClass(
    std::wstring_view class_name) noexcept;

}  // namespace keyina::windows
