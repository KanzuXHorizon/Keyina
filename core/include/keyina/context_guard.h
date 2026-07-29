#pragma once

#include <string_view>

namespace keyina {

enum class GuardReason {
  None,
  ModifierChord,
  Url,
  Email,
  FilePath,
  Identifier,
  VersionOrHash,
  ShellToken,
  ApplicationBypass,
};

struct GuardContext {
  bool modifier_chord{false};
  bool application_bypass{false};
};

struct GuardResult {
  bool transform;
  GuardReason reason;

  friend constexpr bool operator==(const GuardResult&,
                                   const GuardResult&) = default;
};

[[nodiscard]] GuardResult ClassifyToken(
    std::u32string_view token,
    const GuardContext& context) noexcept;

}  // namespace keyina
