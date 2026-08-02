#ifndef NETBOX_XENIA_ADAPTER_TESTS_MINI_TEST_H_
#define NETBOX_XENIA_ADAPTER_TESTS_MINI_TEST_H_

// Same tiny, self-contained test harness pattern used by netbox-server and
// netbox-api (see their tests/mini_test.h) - kept independent/duplicated
// rather than shared, since each is a separate, independently buildable
// project.

#include <functional>
#include <string>
#include <vector>

namespace netbox_xenia_adapter {
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
}  // namespace netbox_xenia_adapter

#define NETBOX_XENIA_ADAPTER_CONCAT_INNER(a, b) a##b
#define NETBOX_XENIA_ADAPTER_CONCAT(a, b) NETBOX_XENIA_ADAPTER_CONCAT_INNER(a, b)

#define TEST_CASE(name)                                                     \
  static void NETBOX_XENIA_ADAPTER_CONCAT(TestBody_, __LINE__)();            \
  static ::netbox_xenia_adapter::test::TestRegistrar                         \
      NETBOX_XENIA_ADAPTER_CONCAT(TestRegistrar_, __LINE__)(                 \
          name, NETBOX_XENIA_ADAPTER_CONCAT(TestBody_, __LINE__));           \
  static void NETBOX_XENIA_ADAPTER_CONCAT(TestBody_, __LINE__)()

#define REQUIRE(condition)                                                   \
  do {                                                                      \
    if (!(condition)) {                                                    \
      throw ::netbox_xenia_adapter::test::AssertionFailure{                 \
          std::string("REQUIRE failed: ") + #condition + " at " +          \
          __FILE__ + ":" + std::to_string(__LINE__)};                      \
    }                                                                       \
  } while (0)

#define REQUIRE_FALSE(condition) REQUIRE(!(condition))

#endif  // NETBOX_XENIA_ADAPTER_TESTS_MINI_TEST_H_
