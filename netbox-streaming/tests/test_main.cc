#include "mini_test.h"

#include <cstdio>

int main() {
  auto& tests = netbox_streaming::test::AllTests();
  int passed = 0;
  int failed = 0;

  for (auto& test : tests) {
    try {
      test.body();
      ++passed;
      std::printf("[PASS] %s\n", test.name.c_str());
    } catch (const netbox_streaming::test::AssertionFailure& failure) {
      ++failed;
      std::printf("[FAIL] %s - %s\n", test.name.c_str(),
                 failure.message.c_str());
    } catch (const std::exception& ex) {
      ++failed;
      std::printf("[FAIL] %s - unexpected exception: %s\n", test.name.c_str(),
                 ex.what());
    }
  }

  std::printf("\n%d passed, %d failed, %d total\n", passed, failed,
             passed + failed);
  return failed == 0 ? 0 : 1;
}
