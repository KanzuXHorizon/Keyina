#include <keyina/version.h>

#include "test_support.h"

#ifdef _WIN32
#include <ole2.h>
#endif

KEYINA_TEST(version_starts_at_zero_major) {
  KEYINA_EXPECT_EQ(keyina::kVersionMajor, 0);
}

int main() {
#ifdef _WIN32
  const HRESULT ole_result = OleInitialize(nullptr);
  const bool ole_initialized = SUCCEEDED(ole_result);
#endif

  const int result = keyina::test::RunAll();

#ifdef _WIN32
  if (ole_initialized) {
    OleUninitialize();
  }
#endif
  return result;
}
