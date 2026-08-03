#pragma once

#include <cstdint>

namespace keyina::tsf {

enum class KeyRouteKind {
  PassThrough,
  Character,
  Boundary,
  Reset,
};

struct KeyRoutingInput {
  std::uint32_t virtual_key{};
  bool shift{false};
  bool caps_lock{false};
  bool control{false};
  bool alt{false};
  bool windows{false};
  bool active_composition{false};
};

struct KeyRoute {
  KeyRouteKind kind{KeyRouteKind::PassThrough};
  char32_t character{};

  friend constexpr bool operator==(const KeyRoute&, const KeyRoute&) = default;
};

[[nodiscard]] KeyRoute RouteKey(const KeyRoutingInput& input) noexcept;

}  // namespace keyina::tsf
