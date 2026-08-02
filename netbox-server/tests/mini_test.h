#ifndef NETBOX_SERVER_TESTS_MINI_TEST_H_
#define NETBOX_SERVER_TESTS_MINI_TEST_H_

// Same tiny, self-contained test harness pattern used by netbox-streaming
// (see netbox-streaming/tests/mini_test.h) - kept independent/duplicated
// rather than shared, since these are two separate, independently
// buildable projects (Phase 7's "separate project" and Phase 8's "separate
// project" requirements both apply).

#include <functional>
#include <string>
#include <vector>

namespace netbox_server {
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
}  // namespace netbox_server

#define NETBOX_SERVER_CONCAT_INNER(a, b) a##b
#define NETBOX_SERVER_CONCAT(a, b) NETBOX_SERVER_CONCAT_INNER(a, b)

#define TEST_CASE(name)                                                     \
  static void NETBOX_SERVER_CONCAT(TestBody_, __LINE__)();                  \
  static ::netbox_server::test::TestRegistrar NETBOX_SERVER_CONCAT(          \
      TestRegistrar_, __LINE__)(name,                                       \
                                NETBOX_SERVER_CONCAT(TestBody_, __LINE__));  \
  static void NETBOX_SERVER_CONCAT(TestBody_, __LINE__)()

#define REQUIRE(condition)                                                   \
  do {                                                                      \
    if (!(condition)) {                                                    \
      throw ::netbox_server::test::AssertionFailure{                        \
          std::string("REQUIRE failed: ") + #condition + " at " +          \
          __FILE__ + ":" + std::to_string(__LINE__)};                      \
    }                                                                       \
  } while (0)

#define REQUIRE_FALSE(condition) REQUIRE(!(condition))

#endif  // NETBOX_SERVER_TESTS_MINI_TEST_H_
