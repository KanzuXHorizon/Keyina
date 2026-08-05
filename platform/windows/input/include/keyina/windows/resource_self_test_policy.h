#pragma once

namespace keyina::windows {

enum class ResourceSelfTestDisposition {
  Pass,
  RetryContaminated,
  BlockedByExistingResident,
  FailBudget,
  FailRuntime,
};

[[nodiscard]] ResourceSelfTestDisposition ClassifyResourceSelfTestAttempt(
    bool existing_resident,
    bool runtime_started,
    bool contaminated_by_input,
    bool budget_pass) noexcept;

}  // namespace keyina::windows
