#include "netbox_api/websocket_gateway.h"

#include <algorithm>

#include "netbox_api/json.h"

namespace netbox_api {

WebSocketGateway::WebSocketGateway(netbox_server::SessionManager& session_manager)
    : session_manager_(session_manager) {
  subscription_id_ = session_manager_.events().Subscribe(
      [this](const netbox_server::NetBoxEvent& event) { OnEvent(event); });
}

WebSocketGateway::~WebSocketGateway() {
  session_manager_.events().Unsubscribe(subscription_id_);
}

void WebSocketGateway::AddConnection(IWebSocketConnection* connection) {
  if (!connection) {
    return;
  }
  std::lock_guard<std::mutex> lock(mutex_);
  connections_.push_back(connection);
}

void WebSocketGateway::RemoveConnection(IWebSocketConnection* connection) {
  std::lock_guard<std::mutex> lock(mutex_);
  connections_.erase(
      std::remove(connections_.begin(), connections_.end(), connection),
      connections_.end());
}

void WebSocketGateway::OnEvent(const netbox_server::NetBoxEvent& event) {
  std::string json = EventToJson(event);

  std::vector<IWebSocketConnection*> connections_copy;
  {
    std::lock_guard<std::mutex> lock(mutex_);
    connections_copy = connections_;
  }
  for (auto* connection : connections_copy) {
    if (connection->IsOpen()) {
      connection->Send(json);
    }
  }
}

std::string WebSocketGateway::EventToJson(
    const netbox_server::NetBoxEvent& event) {
  netbox_api::JsonObject obj;
  obj.Set("type", netbox_server::NetBoxEventTypeToString(event.type));
  obj.Set("session", static_cast<long long>(event.session));
  if (event.player != netbox_server::kInvalidPlayerId) {
    obj.Set("player", static_cast<long long>(event.player));
  }
  return obj.ToString();
}

}  // namespace netbox_api
