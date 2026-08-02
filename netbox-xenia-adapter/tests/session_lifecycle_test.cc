#include "mini_test.h"
#include "mocks.h"
#include "netbox_server/session/session_manager.h"
#include "netbox_xenia_adapter/xenia_game_library_client.h"
#include "netbox_xenia_adapter/xenia_profile_adapter.h"
#include "netbox_xenia_adapter/xenia_runtime_handle.h"

using namespace netbox_xenia_adapter;
using namespace netbox_xenia_adapter::test;

namespace {

netbox_server::RuntimeFactory MakeXeniaRuntimeFactory(
    std::shared_ptr<MockXeniaManagerClient> client, XeniaLaunchRequest request) {
  return [client, request]() -> std::unique_ptr<netbox_server::IRuntimeHandle> {
    return std::make_unique<XeniaRuntimeHandle>(client, request);
  };
}

}  // namespace

TEST_CASE("Session lifecycle: create session, launch game, attach stream, shutdown") {
  auto client = std::make_shared<MockXeniaManagerClient>();
  client->status_sequence = {XeniaRuntimeState::kLaunching,
                            XeniaRuntimeState::kRunning};

  netbox_server::SessionManager manager;
  netbox_server::SessionId session_id = manager.CreateSession(
      MakeXeniaRuntimeFactory(client, {"C:/games/halo3.iso", "profile-1", "config-1"}),
      MakeStreamFactory(/*fail_to_start=*/false), "mock-stream");

  REQUIRE(session_id != netbox_server::kInvalidSessionId);
  REQUIRE(manager.StartSession(session_id));

  netbox_server::Session session = manager.GetSession(session_id);
  REQUIRE(session.state == netbox_server::SessionState::kRunning);

  netbox_server::PlayerId player = manager.AssignPlayer(session_id, /*controller_slot=*/0);
  REQUIRE(player != netbox_server::kInvalidPlayerId);

  REQUIRE(manager.StopSession(session_id));
  session = manager.GetSession(session_id);
  REQUIRE(session.state == netbox_server::SessionState::kStopped);
  REQUIRE(client->launch_calls == 1);
  REQUIRE(client->stop_calls == 1);

  REQUIRE(manager.DestroySession(session_id));
}

TEST_CASE("Session lifecycle: launcher failure surfaces as a failed session, no stream started") {
  auto client = std::make_shared<MockXeniaManagerClient>();
  client->fail_launch = true;

  netbox_server::SessionManager manager;
  netbox_server::SessionId session_id = manager.CreateSession(
      MakeXeniaRuntimeFactory(client, {"C:/games/bad.iso", "profile-1", "config-1"}),
      MakeStreamFactory(), "mock-stream");

  REQUIRE_FALSE(manager.StartSession(session_id));
  netbox_server::Session session = manager.GetSession(session_id);
  REQUIRE(session.state == netbox_server::SessionState::kFailed);
}

TEST_CASE("Session lifecycle: runtime crash mid-run is reflected via failed health on stop") {
  auto client = std::make_shared<MockXeniaManagerClient>();
  client->status_sequence = {XeniaRuntimeState::kRunning};

  auto factory_client = client;
  XeniaLaunchRequest request{"C:/games/halo3.iso", "profile-1", "config-1"};
  auto handle = std::make_unique<XeniaRuntimeHandle>(factory_client, request);
  REQUIRE(handle->Start());
  REQUIRE(handle->Health() == netbox_server::HealthState::kHealthy);

  // Xenia crashes: Xenia Manager now reports kFailed on the next poll.
  client->status_sequence = {XeniaRuntimeState::kFailed};
  handle->Stop();

  REQUIRE(handle->Health() == netbox_server::HealthState::kFailed);
}

TEST_CASE("Profile loading failure yields an empty profile, no exception") {
  auto client = std::make_shared<MockXeniaManagerClient>();
  client->fail_profile_load = true;

  XeniaProfileAdapter adapter(client);
  XeniaProfileInfo profile = adapter.ResolveProfileForAccount("netbox-account-1");

  REQUIRE(profile.profile_id.empty());
  REQUIRE_FALSE(adapter.HasSaveOwnership("netbox-account-1"));
}

TEST_CASE("Profile loading success resolves save ownership and achievements") {
  auto client = std::make_shared<MockXeniaManagerClient>();
  client->profile = {"profile-1", "Player One", true};
  client->achievements = {{"a1", "First Blood", true}, {"a2", "Completionist", false}};

  XeniaProfileAdapter adapter(client);
  XeniaProfileInfo profile = adapter.ResolveProfileForAccount("netbox-account-1");
  REQUIRE(profile.profile_id == "profile-1");
  REQUIRE(adapter.HasSaveOwnership("netbox-account-1"));

  auto achievements = adapter.GetAchievements("netbox-account-1");
  REQUIRE(achievements.size() == 2);
  REQUIRE(achievements[0].unlocked);
  REQUIRE_FALSE(achievements[1].unlocked);
}

TEST_CASE("Games Blade reads the Xenia Manager game library") {
  auto client = std::make_shared<MockXeniaManagerClient>();
  client->games = {{"Halo 3", "C:/games/halo3.iso", "C:/covers/halo3.jpg", "2026-07-01T00:00:00Z"}};

  XeniaGameLibraryClient library(client);
  auto games = library.ListAvailableGames();

  REQUIRE(games.size() == 1);
  REQUIRE(games[0].title == "Halo 3");
  REQUIRE(games[0].executable_path == "C:/games/halo3.iso");
}
