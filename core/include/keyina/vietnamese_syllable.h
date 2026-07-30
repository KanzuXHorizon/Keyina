#pragma once

#include <string_view>

namespace keyina {

[[nodiscard]] bool IsValidVietnameseSyllable(
    std::u32string_view syllable) noexcept;

}  // namespace keyina
