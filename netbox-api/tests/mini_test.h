#ifndef NETBOX_API_TESTS_MINI_TEST_H_
#define NETBOX_API_TESTS_MINI_TEST_H_

// Same tiny, self-contained test harness pattern used by netbox-streaming
// and netbox-server (kept independently duplicated since each is a
// separately buildable project).

#include <functional>
#include <string>
#include <vector>

namespace netbox_api {
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

struct AssertionFailure {
  std::string message;
};

}  // namespace test
}  // namespace netbox_api

#define NETBOX_API_CONCAT_INNER(a, b) a##b
#define NETBOX_API_CONCAT(a, b) NETBOX_API_CONCAT_INNER(a, b)

#define TEST_CASE(name)                                                     \
  static void NETBOX_API_CONCAT(TestBody_, __LINE__)();                     \
  static ::netbox_api::test::TestRegistrar NETBOX_API_CONCAT(               \
      TestRegistrar_, __LINE__)(name,                                      \
                                NETBOX_API_CONCAT(TestBody_, __LINE__));    \
  static void NETBOX_API_CONCAT(TestBody_, __LINE__)()

#define REQUIRE(condition)                                                  \
  do {                                                                     \
    if (!(condition)) {                                                   \
      throw ::netbox_api::test::AssertionFailure{                          \
          std::string("REQUIRE failed: ") + #condition + " at " +         \
          __FILE__ + ":" + std::to_string(__LINE__)};                     \
    }                                                                      \
  } while (0)

#define REQUIRE_FALSE(condition) REQUIRE(!(condition))

#endif  // NETBOX_API_TESTS_MINI_TEST_H_
