#include <keyina/windows/resource_self_test_policy.h>

#include "../test_support.h"

using keyina::windows::ClassifyResourceSelfTestAttempt;
using keyina::windows::ResourceSelfTestDisposition;

KEYINA_TEST(resource_self_test_blocks_when_production_resident_exists) {
  KEYINA_EXPECT_EQ(
      ClassifyResourceSelfTestAttempt(
          true,
          true,
          false,
          true),
      ResourceSelfTestDisposition::BlockedByExistingResident);
}

KEYINA_TEST(resource_self_test_retries_only_input_contamination) {
  KEYINA_EXPECT_EQ(
      ClassifyResourceSelfTestAttempt(
          false,
          true,
          true,
          false),
      ResourceSelfTestDisposition::RetryContaminated);
}

KEYINA_TEST(resource_self_test_fails_clean_budget_regression) {
  KEYINA_EXPECT_EQ(
      ClassifyResourceSelfTestAttempt(
          false,
          true,
          false,
          false),
      ResourceSelfTestDisposition::FailBudget);
}

KEYINA_TEST(resource_self_test_reports_runtime_start_failure) {
  KEYINA_EXPECT_EQ(
      ClassifyResourceSelfTestAttempt(
          false,
          false,
          false,
          false),
      ResourceSelfTestDisposition::FailRuntime);
}

KEYINA_TEST(resource_self_test_passes_only_a_clean_successful_attempt) {
  KEYINA_EXPECT_EQ(
      ClassifyResourceSelfTestAttempt(
          false,
          true,
          false,
          true),
      ResourceSelfTestDisposition::Pass);
}
