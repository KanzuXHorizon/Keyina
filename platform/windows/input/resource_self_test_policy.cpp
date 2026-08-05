#include <keyina/windows/resource_self_test_policy.h>

namespace keyina::windows {

ResourceSelfTestDisposition ClassifyResourceSelfTestAttempt(
    bool existing_resident,
    bool runtime_started,
    bool contaminated_by_input,
    bool budget_pass) noexcept {
  if (existing_resident) {
    return ResourceSelfTestDisposition::BlockedByExistingResident;
  }
  if (!runtime_started) {
    return ResourceSelfTestDisposition::FailRuntime;
  }
  if (budget_pass) {
    return ResourceSelfTestDisposition::Pass;
  }
  return contaminated_by_input
      ? ResourceSelfTestDisposition::RetryContaminated
      : ResourceSelfTestDisposition::FailBudget;
}

}  // namespace keyina::windows
