#ifndef NETBOX_STREAMING_TESTS_MINI_TEST_H_
#define NETBOX_STREAMING_TESTS_MINI_TEST_H_

// Tiny, self-contained test harness so netbox-streaming's tests don't need
// to depend on Xenia's third_party/catch (a separate project/tree per the
// Phase 7 requirement) or any other external test framework. Each TEST_CASE
// registers itself into a static list; main() (see test_main.cc) runs them
// all and reports pass/fail counts, exiting non-zero on any failure.

#include <cstdio>
#include <functional>
#include <string>
#include <vector>

namespace netbox_streaming {
namespace test {

struct TestCase {
  std::string name;
  std::function<void()> body;
};

inline std::vector<TestCase>& AllTests() {
  static std::vector<TestCase> tests;
  return tests;
}

struct TestRegistrar {
  TestRegistrar(const std::string& name, std::function<void()> body) {
    AllTests().push_back({name, std::move(body)});
  }
};

// Thrown by REQUIRE on failure; caught by the test runner in test_main.cc.
struct AssertionFailure {
  std::string message;
};

}  // namespace test
}  // namespace netbox_streaming

#define NETBOX_STREAMING_CONCAT_INNER(a, b) a##b
#define NETBOX_STREAMING_CONCAT(a, b) NETBOX_STREAMING_CONCAT_INNER(a, b)

#define TEST_CASE(name)                                                     \
  static void NETBOX_STREAMING_CONCAT(TestBody_, __LINE__)();               \
  static ::netbox_streaming::test::TestRegistrar NETBOX_STREAMING_CONCAT(    \
      TestRegistrar_, __LINE__)(name,                                       \
                                NETBOX_STREAMING_CONCAT(TestBody_, __LINE__)); \
  static void NETBOX_STREAMING_CONCAT(TestBody_, __LINE__)()

#define REQUIRE(condition)                                                   \
  do {                                                                      \
    if (!(condition)) {                                                    \
      throw ::netbox_streaming::test::AssertionFailure{                     \
          std::string("REQUIRE failed: ") + #condition + " at " +          \
          __FILE__ + ":" + std::to_string(__LINE__)};                      \
    }                                                                       \
  } while (0)

#define REQUIRE_FALSE(condition) REQUIRE(!(condition))

#endif  // NETBOX_STREAMING_TESTS_MINI_TEST_H_
