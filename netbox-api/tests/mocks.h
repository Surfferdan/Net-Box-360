#ifndef NETBOX_API_TESTS_MOCKS_H_
#define NETBOX_API_TESTS_MOCKS_H_

#include <memory>

#include "netbox_server/runtime/runtime_handle.h"
#include "netbox_server/stream/stream_handle.h"

// Shared mocks for netbox-api tests - mirrors the same MockRuntimeHandle/
// MockStreamHandle pattern used in netbox-server's own tests, so no real
// Xenia process or CloudMorph backend is required.
namespace netbox_api {
namespace test {

class MockRuntimeHandle : public netbox_server::IRuntimeHandle {
 public:
  explicit MockRuntimeHandle(bool fail_to_start = false)
      : fail_to_start_(fail_to_start) {}

  bool Start() override {
    if (fail_to_start_) return false;
    alive_ = true;
    return true;
  }
  void Stop() override { alive_ = false; }
  bool IsAlive() const override { return alive_; }
  netbox_server::HealthState Health() const override {
    return alive_ ? netbox_server::HealthState::kHealthy
                 : netbox_server::HealthState::kUnknown;
  }

 private:
  bool fail_to_start_;
  bool alive_ = false;
};

class MockStreamHandle : public netbox_server::IStreamHandle {
 public:
  explicit MockStreamHandle(bool fail_to_start = false)
      : fail_to_start_(fail_to_start) {}

  bool Start() override {
    if (fail_to_start_) return false;
    running_ = true;
    return true;
  }
  void Stop() override { running_ = false; }
  netbox_server::HealthState Health() const override {
    return running_ ? netbox_server::HealthState::kHealthy
                    : netbox_server::HealthState::kUnknown;
  }
  uint32_t ConnectedClientCount() const override { return running_ ? 1 : 0; }

 private:
  bool fail_to_start_;
  bool running_ = false;
};

inline netbox_server::RuntimeFactory MakeRuntimeFactory(
    bool fail_to_start = false) {
  return [fail_to_start]() -> std::unique_ptr<netbox_server::IRuntimeHandle> {
    return std::make_unique<MockRuntimeHandle>(fail_to_start);
  };
}

inline netbox_server::StreamFactory MakeStreamFactory(
    bool fail_to_start = false) {
  return [fail_to_start]() -> std::unique_ptr<netbox_server::IStreamHandle> {
    return std::make_unique<MockStreamHandle>(fail_to_start);
  };
}

}  // namespace test
}  // namespace netbox_api

#endif  // NETBOX_API_TESTS_MOCKS_H_
