#include "mini_test.h"
#include "mocks.h"
#include "netbox_xenia_adapter/xenia_runtime_handle.h"

using namespace netbox_xenia_adapter;
using namespace netbox_xenia_adapter::test;

TEST_CASE("XeniaRuntimeHandle Start() launches then waits for running") {
  auto client = std::make_shared<MockXeniaManagerClient>();
  client->status_sequence = {XeniaRuntimeState::kLaunching,
                            XeniaRuntimeState::kLaunching,
                            XeniaRuntimeState::kRunning};

  XeniaLaunchRequest request{"C:/games/halo3.iso", "profile-1", "config-1"};
  XeniaRuntimeHandle handle(client, request);

  REQUIRE(handle.Start());
  REQUIRE(handle.IsAlive());
  REQUIRE(handle.Health() == netbox_server::HealthState::kHealthy);
  REQUIRE(client->launch_calls == 1);
  REQUIRE(client->last_launched_game_path == "C:/games/halo3.iso");
}

TEST_CASE("XeniaRuntimeHandle Start() fails when launcher rejects") {
  auto client = std::make_shared<MockXeniaManagerClient>();
  client->fail_launch = true;

  XeniaRuntimeHandle handle(client, XeniaLaunchRequest{"C:/games/x.iso", "p", "c"});

  REQUIRE_FALSE(handle.Start());
  REQUIRE_FALSE(handle.IsAlive());
  REQUIRE(handle.Health() == netbox_server::HealthState::kFailed);
}

TEST_CASE("XeniaRuntimeHandle Start() fails when status reports failed mid-launch") {
  auto client = std::make_shared<MockXeniaManagerClient>();
  client->status_sequence = {XeniaRuntimeState::kLaunching,
                            XeniaRuntimeState::kFailed};

  XeniaRuntimeHandle handle(client, XeniaLaunchRequest{"C:/games/x.iso", "p", "c"});

  REQUIRE_FALSE(handle.Start());
  REQUIRE(handle.Health() == netbox_server::HealthState::kFailed);
}

TEST_CASE("XeniaRuntimeHandle Start() fails when poll budget exhausts without running") {
  auto client = std::make_shared<MockXeniaManagerClient>();
  client->status_sequence = {XeniaRuntimeState::kLaunching};  // never advances

  XeniaRuntimeHandle::Options options;
  options.ready_poll_attempts = 3;
  XeniaRuntimeHandle handle(client, XeniaLaunchRequest{"C:/games/x.iso", "p", "c"}, options);

  REQUIRE_FALSE(handle.Start());
}

TEST_CASE("XeniaRuntimeHandle Stop() requests shutdown and confirms stopped") {
  auto client = std::make_shared<MockXeniaManagerClient>();
  client->status_sequence = {XeniaRuntimeState::kRunning};

  XeniaRuntimeHandle handle(client, XeniaLaunchRequest{"C:/games/x.iso", "p", "c"});
  REQUIRE(handle.Start());

  client->status_sequence = {XeniaRuntimeState::kStopping,
                            XeniaRuntimeState::kStopped};
  handle.Stop();

  REQUIRE(client->stop_calls == 1);
  REQUIRE_FALSE(handle.IsAlive());
}

TEST_CASE("XeniaRuntimeHandle Stop() is a no-op when Start() was never called") {
  auto client = std::make_shared<MockXeniaManagerClient>();
  XeniaRuntimeHandle handle(client, XeniaLaunchRequest{"C:/games/x.iso", "p", "c"});

  handle.Stop();  // should not throw or call into the client

  REQUIRE(client->stop_calls == 0);
}

TEST_CASE("XeniaRuntimeHandle handles a runtime crash reported mid-run as failed health") {
  auto client = std::make_shared<MockXeniaManagerClient>();
  client->status_sequence = {XeniaRuntimeState::kRunning};

  XeniaRuntimeHandle handle(client, XeniaLaunchRequest{"C:/games/x.iso", "p", "c"});
  REQUIRE(handle.Start());
  REQUIRE(handle.IsAlive());

  // Simulate a crash: next Stop() poll observes kFailed instead of
  // kStopped (Xenia crashed rather than shutting down cleanly).
  client->status_sequence = {XeniaRuntimeState::kFailed};
  handle.Stop();

  REQUIRE(handle.Health() == netbox_server::HealthState::kFailed);
  REQUIRE_FALSE(handle.IsAlive());
}
