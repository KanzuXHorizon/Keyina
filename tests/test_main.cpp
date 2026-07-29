#include <keyina/version.h>

#include "test_support.h"

KEYINA_TEST(version_starts_at_zero_major) {
  KEYINA_EXPECT_EQ(keyina::kVersionMajor, 0);
}

int main() { return keyina::test::RunAll(); }
