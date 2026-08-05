#pragma once

#include <exception>
#include <functional>
#include <iostream>
#include <sstream>
#include <stdexcept>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

namespace keyina::test {

using TestFunction = void (*)();

struct TestCase {
  std::string_view name;
  TestFunction function;
};

inline std::vector<TestCase>& Registry() {
  static std::vector<TestCase> tests;
  return tests;
}

class Registrar {
 public:
  Registrar(std::string_view name, TestFunction function) {
    Registry().push_back({name, function});
  }
};

template <typename Actual, typename Expected>
void ExpectEqual(const Actual& actual, const Expected& expected,
                 std::string_view expression, std::string_view file,
                 int line) {
  if (!(actual == expected)) {
    std::ostringstream message;
    message << file << ':' << line << ": expected " << expression;
    throw std::runtime_error(message.str());
  }
}

inline void ExpectTrue(bool value, std::string_view expression,
                       std::string_view file, int line) {
  if (!value) {
    std::ostringstream message;
    message << file << ':' << line << ": expected " << expression
            << " to be true";
    throw std::runtime_error(message.str());
  }
}

inline int RunAll() {
  int failures = 0;
  for (const auto& test : Registry()) {
    std::cout << "[RUN] " << test.name << std::endl;
    try {
      test.function();
      std::cout << "[PASS] " << test.name << std::endl;
    } catch (const std::exception& error) {
      ++failures;
      std::cerr << "[FAIL] " << test.name << ": " << error.what() << std::endl;
    } catch (...) {
      ++failures;
      std::cerr << "[FAIL] " << test.name << ": unknown exception" << std::endl;
    }
  }
  std::cout << Registry().size() - static_cast<std::size_t>(failures) << '/'
            << Registry().size() << " tests passed" << std::endl;
  return failures == 0 ? 0 : 1;
}

}  // namespace keyina::test

#define KEYINA_TEST(name)                                                     \
  static void name();                                                         \
  static ::keyina::test::Registrar name##_registrar{#name, &name};           \
  static void name()

#define KEYINA_EXPECT_EQ(actual, expected)                                    \
  ::keyina::test::ExpectEqual((actual), (expected), #actual " == " #expected, \
                              __FILE__, __LINE__)

#define KEYINA_EXPECT_TRUE(expression)                                        \
  ::keyina::test::ExpectTrue(static_cast<bool>(expression), #expression,      \
                             __FILE__, __LINE__)
