#include "netbox_api/websocket_gateway.h"

#include "mini_test.h"
#include "mocks.h"

namespace netbox_api {
namespace test {

namespace {

class MockConnection : public IWebSocketConnection {
 public:
  void Send(const std::string& text_message) override {
    messages.push_back(text_message);
  }
  bool IsOpen() const override { return open; }

  std::vector<std::string> messages;
  bool open = true;
};

bool AnyMessageContains(const MockConnection& connection,
                        const std::string& needle) {
  for (const auto& message : connection.messages) {
    if (message.find(needle) != std::string::npos) return true;
  }
  return false;
}

}  // namespace

TEST_CASE("WebSocketGateway forwards RuntimeStarted and StreamHealthy on session start") {
  netbox_server::SessionManager manager;
  WebSocketGateway gateway(manager);
  MockConnection connection;
  gateway.AddConnection(&connection);

  netbox_server::SessionId id =
      manager.CreateSession(MakeRuntimeFactory(), MakeStreamFactory());
  manager.StartSession(id);

  REQUIRE(AnyMessageContains(connection, "\"type\":\"RuntimeStarted\""));
  REQUIRE(AnyMessageContains(connection, "\"type\":\"StreamHealthy\""));

  gateway.RemoveConnection(&connection);
}

TEST_CASE("WebSocketGateway forwards RuntimeFailed when the runtime cannot start") {
  netbox_server::SessionManager manager;
  WebSocketGateway gateway(manager);
  MockConnection connection;
  gateway.AddConnection(&connection);

  netbox_server::SessionId id = manager.CreateSession(
      MakeRuntimeFactory(/*fail_to_start=*/true), MakeStreamFactory());
  manager.StartSession(id);

  REQUIRE(AnyMessageContains(connection, "\"type\":\"RuntimeFailed\""));

  gateway.RemoveConnection(&connection);
}

TEST_CASE("WebSocketGateway forwards RuntimeStopped on session stop") {
  netbox_server::SessionManager manager;
  WebSocketGateway gateway(manager);
  MockConnection connection;
  gateway.AddConnection(&connection);

  netbox_server::SessionId id =
      manager.CreateSession(MakeRuntimeFactory(), MakeStreamFactory());
  manager.StartSession(id);
  manager.StopSession(id);

  REQUIRE(AnyMessageContains(connection, "\"type\":\"RuntimeStopped\""));

  gateway.RemoveConnection(&connection);
}

TEST_CASE("WebSocketGateway forwards PlayerJoined and PlayerLeft") {
  netbox_server::SessionManager manager;
  WebSocketGateway gateway(manager);
  MockConnection connection;
  gateway.AddConnection(&connection);

  netbox_server::SessionId id =
      manager.CreateSession(MakeRuntimeFactory(), MakeStreamFactory());
  netbox_server::PlayerId player = manager.AssignPlayer(id, 0);
  REQUIRE(AnyMessageContains(connection, "\"type\":\"PlayerJoined\""));

  manager.ReleasePlayer(id, player);
  REQUIRE(AnyMessageContains(connection, "\"type\":\"PlayerLeft\""));

  gateway.RemoveConnection(&connection);
}

TEST_CASE("WebSocketGateway does not forward events after RemoveConnection") {
  netbox_server::SessionManager manager;
  WebSocketGateway gateway(manager);
  MockConnection connection;
  gateway.AddConnection(&connection);
  gateway.RemoveConnection(&connection);

  netbox_server::SessionId id =
      manager.CreateSession(MakeRuntimeFactory(), MakeStreamFactory());
  manager.StartSession(id);

  REQUIRE(connection.messages.empty());
}

TEST_CASE("WebSocketGateway skips closed connections") {
  netbox_server::SessionManager manager;
  WebSocketGateway gateway(manager);
  MockConnection connection;
  connection.open = false;
  gateway.AddConnection(&connection);

  netbox_server::SessionId id =
      manager.CreateSession(MakeRuntimeFactory(), MakeStreamFactory());
  manager.StartSession(id);

  REQUIRE(connection.messages.empty());

  gateway.RemoveConnection(&connection);
}

}  // namespace test
}  // namespace netbox_api
