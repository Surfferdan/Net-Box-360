#include "netbox_server/session/session_manager.h"

#include "mini_test.h"
#include "netbox_server/runtime/runtime_handle.h"
#include "netbox_server/stream/stream_handle.h"

// Mock-backed tests for SessionManager (Phase 8). MockRuntimeHandle/
// MockStreamHandle stand in for a real Xenia-process runtime and a real
// netbox-streaming CloudMorphBackend, respectively - no Xenia process or
// CloudMorph executable is required to run these tests.

namespace netbox_server {
namespace test {

namespace {

class MockRuntimeHandle : public IRuntimeHandle {
 public:
  explicit MockRuntimeHandle(bool fail_to_start = false)
      : fail_to_start_(fail_to_start) {}

  bool Start() override {
    if (fail_to_start_) {
      return false;
    }
    alive_ = true;
    start_count_++;
    return true;
  }

  void Stop() override {
    alive_ = false;
    stop_count_++;
  }

  bool IsAlive() const override { return alive_; }

  HealthState Health() const override {
    return alive_ ? HealthState::kHealthy : HealthState::kUnknown;
  }

  int start_count() const { return start_count_; }
  int stop_count() const { return stop_count_; }

 private:
  bool fail_to_start_;
  bool alive_ = false;
  int start_count_ = 0;
  int stop_count_ = 0;
};

class MockStreamHandle : public IStreamHandle {
 public:
  explicit MockStreamHandle(bool fail_to_start = false)
      : fail_to_start_(fail_to_start) {}

  bool Start() override {
    if (fail_to_start_) {
      return false;
    }
    running_ = true;
    start_count_++;
    return true;
  }

  void Stop() override {
    running_ = false;
    stop_count_++;
  }

  HealthState Health() const override {
    return running_ ? HealthState::kHealthy : HealthState::kUnknown;
  }

  uint32_t ConnectedClientCount() const override {
    return running_ ? 1 : 0;
  }

  int start_count() const { return start_count_; }
  int stop_count() const { return stop_count_; }

 private:
  bool fail_to_start_;
  bool running_ = false;
  int start_count_ = 0;
  int stop_count_ = 0;
};

RuntimeFactory MakeRuntimeFactory(bool fail_to_start = false) {
  return [fail_to_start]() -> std::unique_ptr<IRuntimeHandle> {
    return std::make_unique<MockRuntimeHandle>(fail_to_start);
  };
}

StreamFactory MakeStreamFactory(bool fail_to_start = false) {
  return [fail_to_start]() -> std::unique_ptr<IStreamHandle> {
    return std::make_unique<MockStreamHandle>(fail_to_start);
  };
}

}  // namespace

TEST_CASE("SessionManager creates a session in the Created state") {
  SessionManager manager;
  SessionId id = manager.CreateSession(MakeRuntimeFactory(), MakeStreamFactory());

  REQUIRE(id != kInvalidSessionId);
  Session session = manager.GetSession(id);
  REQUIRE(session.id == id);
  REQUIRE(session.state == SessionState::kCreated);
  REQUIRE(session.runtime != kInvalidRuntimeId);
  REQUIRE(session.stream != kInvalidStreamId);
}

TEST_CASE("SessionManager starts a session and transitions to Running") {
  SessionManager manager;
  SessionId id = manager.CreateSession(MakeRuntimeFactory(), MakeStreamFactory());

  REQUIRE(manager.StartSession(id));
  Session session = manager.GetSession(id);
  REQUIRE(session.state == SessionState::kRunning);

  auto* runtime_handle = manager.runtime_registry().GetHandle(session.runtime);
  auto* stream_handle = manager.stream_registry().GetHandle(session.stream);
  REQUIRE(runtime_handle->IsAlive());
  REQUIRE(stream_handle->Health() == HealthState::kHealthy);
}

TEST_CASE("SessionManager stops a running session") {
  SessionManager manager;
  SessionId id = manager.CreateSession(MakeRuntimeFactory(), MakeStreamFactory());
  manager.StartSession(id);

  REQUIRE(manager.StopSession(id));
  Session session = manager.GetSession(id);
  REQUIRE(session.state == SessionState::kStopped);

  auto* runtime_handle = manager.runtime_registry().GetHandle(session.runtime);
  REQUIRE_FALSE(runtime_handle->IsAlive());
}

TEST_CASE("SessionManager supports multiple concurrent sessions") {
  SessionManager manager;
  SessionId id_a = manager.CreateSession(MakeRuntimeFactory(), MakeStreamFactory());
  SessionId id_b = manager.CreateSession(MakeRuntimeFactory(), MakeStreamFactory());

  REQUIRE(id_a != id_b);
  REQUIRE(manager.StartSession(id_a));
  REQUIRE(manager.StartSession(id_b));

  std::vector<Session> sessions = manager.ListSessions();
  REQUIRE(sessions.size() == 2);
  for (const auto& session : sessions) {
    REQUIRE(session.state == SessionState::kRunning);
  }

  manager.StopSession(id_a);
  manager.StopSession(id_b);
}

TEST_CASE("SessionManager assigns players to controller slots") {
  SessionManager manager;
  SessionId id = manager.CreateSession(MakeRuntimeFactory(), MakeStreamFactory());

  PlayerId player_0 = manager.AssignPlayer(id, 0);
  PlayerId player_1 = manager.AssignPlayer(id, 1);
  REQUIRE(player_0 != kInvalidPlayerId);
  REQUIRE(player_1 != kInvalidPlayerId);
  REQUIRE(player_0 != player_1);

  // Slot 0 is already occupied for this session.
  PlayerId duplicate = manager.AssignPlayer(id, 0);
  REQUIRE(duplicate == kInvalidPlayerId);

  Session session = manager.GetSession(id);
  REQUIRE(session.players.size() == 2);

  auto players = manager.player_registry().ListPlayersForSession(id);
  REQUIRE(players.size() == 2);
}

TEST_CASE("SessionManager assigns a stream and reports its info") {
  SessionManager manager;
  SessionId id = manager.CreateSession(MakeRuntimeFactory(), MakeStreamFactory(),
                                       "mock-backend");
  Session session = manager.GetSession(id);

  StreamInfo info = manager.stream_registry().GetInfo(session.stream);
  REQUIRE(info.id == session.stream);
  REQUIRE(info.backend_name == "mock-backend");

  manager.StartSession(id);
  info = manager.stream_registry().GetInfo(session.stream);
  REQUIRE(info.health == HealthState::kHealthy);
  REQUIRE(info.connected_clients == 1);

  manager.StopSession(id);
}

TEST_CASE("SessionManager marks a session Failed when the runtime fails to start") {
  SessionManager manager;
  SessionId id = manager.CreateSession(MakeRuntimeFactory(/*fail_to_start=*/true),
                                       MakeStreamFactory());

  REQUIRE_FALSE(manager.StartSession(id));
  Session session = manager.GetSession(id);
  REQUIRE(session.state == SessionState::kFailed);

  // The stream must never have been started since the runtime failed.
  auto* stream_handle = manager.stream_registry().GetHandle(session.stream);
  REQUIRE(stream_handle->Health() == HealthState::kUnknown);
}

TEST_CASE("SessionManager rolls back the runtime when the stream fails to start") {
  SessionManager manager;
  SessionId id = manager.CreateSession(MakeRuntimeFactory(),
                                       MakeStreamFactory(/*fail_to_start=*/true));

  REQUIRE_FALSE(manager.StartSession(id));
  Session session = manager.GetSession(id);
  REQUIRE(session.state == SessionState::kFailed);

  auto* runtime_handle = manager.runtime_registry().GetHandle(session.runtime);
  REQUIRE_FALSE(runtime_handle->IsAlive());
}

TEST_CASE("SessionManager destroys a session and cleans up its registries") {
  SessionManager manager;
  SessionId id = manager.CreateSession(MakeRuntimeFactory(), MakeStreamFactory());
  manager.StartSession(id);
  PlayerId player = manager.AssignPlayer(id, 0);

  Session session = manager.GetSession(id);
  RuntimeId runtime_id = session.runtime;
  StreamId stream_id = session.stream;

  REQUIRE(manager.DestroySession(id));

  Session gone = manager.GetSession(id);
  REQUIRE(gone.id == kInvalidSessionId);
  REQUIRE(manager.runtime_registry().GetHandle(runtime_id) == nullptr);
  REQUIRE(manager.stream_registry().GetHandle(stream_id) == nullptr);
  REQUIRE(manager.player_registry().GetInfo(player).id == kInvalidPlayerId);
}

TEST_CASE("SessionManager StartSession/StopSession are idempotent") {
  SessionManager manager;
  SessionId id = manager.CreateSession(MakeRuntimeFactory(), MakeStreamFactory());

  REQUIRE(manager.StartSession(id));
  REQUIRE(manager.StartSession(id));  // Already running - no-op, still true.

  auto* runtime_handle =
      manager.runtime_registry().GetHandle(manager.GetSession(id).runtime);
  REQUIRE(static_cast<MockRuntimeHandle*>(runtime_handle)->start_count() == 1);

  manager.StopSession(id);
  manager.StopSession(id);  // Already stopped - safe no-op.
  REQUIRE(static_cast<MockRuntimeHandle*>(runtime_handle)->stop_count() == 1);
}

}  // namespace test
}  // namespace netbox_server
