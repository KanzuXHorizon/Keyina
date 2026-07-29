#pragma once

#include <cstddef>
#include <optional>
#include <string>
#include <string_view>

#include <keyina/engine.h>

namespace keyina::tsf {

struct Utf16Edit {
  std::size_t erase_utf16_units{};
  std::wstring insert;
  bool consumed{false};
  bool commit_before{false};

  friend bool operator==(const Utf16Edit&, const Utf16Edit&) = default;
};

[[nodiscard]] std::optional<Utf16Edit> TranslateEdit(
    const TextEdit& edit,
    std::u32string_view owned_composition) noexcept;

}  // namespace keyina::tsf
