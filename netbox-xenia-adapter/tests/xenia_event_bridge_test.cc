#include "mini_test.h"
#include "netbox_xenia_adapter/xenia_event_bridge.h"

using namespace netbox_xenia_adapter;

TEST_CASE("XeniaEventBridge maps SessionStarted to RuntimeStarted") {
  netbox_server::NetBoxEventBus bus;
  std::vector<netbox_server::NetBoxEvent> received;
  bus.Subscribe([&](const netbox_server::NetBoxEvent& event) {
    received.push_back(event);
  });

  XeniaEventBridge bridge(bus);
  bridge.SetSessionIdResolver([](const std::string& xenia_id) -> netbox_server::SessionId {
    return xenia_id == "xenia-1" ? 42 : netbox_server::kInvalidSessionId;
  });

  bridge.HandleXeniaEvent({"SessionStarted", {{"sessionId", "xenia-1"}}});

  REQUIRE(received.size() == 1);
  REQUIRE(received[0].type == netbox_server::NetBoxEventType::kRuntimeStarted);
  REQUIRE(received[0].session == 42);
}

TEST_CASE("XeniaEventBridge maps XeniaError/SessionFailed to RuntimeFailed") {
  netbox_server::NetBoxEventBus bus;
  std::vector<netbox_server::NetBoxEventType> types;
  bus.Subscribe([&](const netbox_server::NetBoxEvent& event) {
    types.push_back(event.type);
  });

  XeniaEventBridge bridge(bus);
  bridge.SetSessionIdResolver([](const std::string&) -> netbox_server::SessionId { return 1; });

  bridge.HandleXeniaEvent({"XeniaError", {{"sessionId", "xenia-1"}}});
  bridge.HandleXeniaEvent({"SessionFailed", {{"sessionId", "xenia-1"}}});

  REQUIRE(types.size() == 2);
  REQUIRE(types[0] == netbox_server::NetBoxEventType::kRuntimeFailed);
  REQUIRE(types[1] == netbox_server::NetBoxEventType::kRuntimeFailed);
}

TEST_CASE("XeniaEventBridge maps PlayerJoined/PlayerLeft/StreamHealthy/StreamFailed") {
  netbox_server::NetBoxEventBus bus;
  std::vector<netbox_server::NetBoxEventType> types;
  bus.Subscribe([&](const netbox_server::NetBoxEvent& event) {
    types.push_back(event.type);
  });

  XeniaEventBridge bridge(bus);
  bridge.SetSessionIdResolver([](const std::string&) -> netbox_server::SessionId { return 1; });

  bridge.HandleXeniaEvent({"PlayerJoined", {{"sessionId", "xenia-1"}}});
  bridge.HandleXeniaEvent({"PlayerLeft", {{"sessionId", "xenia-1"}}});
  bridge.HandleXeniaEvent({"StreamHealthy", {{"sessionId", "xenia-1"}}});
  bridge.HandleXeniaEvent({"StreamFailed", {{"sessionId", "xenia-1"}}});

  REQUIRE(types.size() == 4);
  REQUIRE(types[0] == netbox_server::NetBoxEventType::kPlayerJoined);
  REQUIRE(types[1] == netbox_server::NetBoxEventType::kPlayerLeft);
  REQUIRE(types[2] == netbox_server::NetBoxEventType::kStreamHealthy);
  REQUIRE(types[3] == netbox_server::NetBoxEventType::kStreamFailed);
}

TEST_CASE("XeniaEventBridge routes AchievementUnlocked to the profile update listener, not the bus") {
  netbox_server::NetBoxEventBus bus;
  int bus_dispatch_count = 0;
  bus.Subscribe([&](const netbox_server::NetBoxEvent&) { ++bus_dispatch_count; });

  XeniaEventBridge bridge(bus);
  ProfileUpdateEvent captured;
  bool listener_called = false;
  bridge.SetProfileUpdateListener([&](const ProfileUpdateEvent& event) {
    listener_called = true;
    captured = event;
  });

  bridge.HandleXeniaEvent(
      {"AchievementUnlocked", {{"profileId", "p1"}, {"achievementId", "a1"}}});

  REQUIRE(listener_called);
  REQUIRE(captured.profile_id == "p1");
  REQUIRE(captured.achievement_id == "a1");
  REQUIRE(bus_dispatch_count == 0);
}

TEST_CASE("XeniaEventBridge drops events when session id cannot be resolved") {
  netbox_server::NetBoxEventBus bus;
  int dispatch_count = 0;
  bus.Subscribe([&](const netbox_server::NetBoxEvent&) { ++dispatch_count; });

  XeniaEventBridge bridge(bus);
  bridge.SetSessionIdResolver([](const std::string&) -> netbox_server::SessionId {
    return netbox_server::kInvalidSessionId;
  });

  bridge.HandleXeniaEvent({"SessionStarted", {{"sessionId", "unknown"}}});

  REQUIRE(dispatch_count == 0);
}

TEST_CASE("XeniaEventBridge ignores unrelated Xenia Manager event types") {
  netbox_server::NetBoxEventBus bus;
  int dispatch_count = 0;
  bus.Subscribe([&](const netbox_server::NetBoxEvent&) { ++dispatch_count; });

  XeniaEventBridge bridge(bus);
  bridge.SetSessionIdResolver([](const std::string&) -> netbox_server::SessionId { return 1; });

  bridge.HandleXeniaEvent({"ConfigSaved", {}});
  bridge.HandleXeniaEvent({"SaveUploaded", {}});

  REQUIRE(dispatch_count == 0);
}
