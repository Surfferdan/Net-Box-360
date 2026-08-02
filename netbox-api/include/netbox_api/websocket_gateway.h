#ifndef NETBOX_API_WEBSOCKET_GATEWAY_H_
#define NETBOX_API_WEBSOCKET_GATEWAY_H_

#include <memory>
#include <mutex>
#include <vector>

#include "netbox_api/websocket_connection.h"
#include "netbox_server/events.h"
#include "netbox_server/session/session_manager.h"

namespace netbox_api {

// Backs the /ws/events endpoint. Subscribes directly to
// SessionManager::events() (netbox_server::NetBoxEventBus) - the single,
// existing event model - and forwards every dispatched event as a JSON
// text frame to every currently registered connection. No second event
// model is introduced here; this class is purely a JSON-serialization +
// fan-out adapter over the existing event bus.
//
// Forwarded event types (verbatim from netbox_server::NetBoxEventType):
//   RuntimeStarted, RuntimeStopped, RuntimeFailed,
//   PlayerJoined, PlayerLeft,
//   StreamHealthy, StreamFailed
class WebSocketGateway {
 public:
  explicit WebSocketGateway(netbox_server::SessionManager& session_manager);
  ~WebSocketGateway();

  // Registers a connection to receive forwarded events. `connection` is not
  // owned - caller (the real WebSocket transport, or a test) retains
  // ownership and must call RemoveConnection() before destroying it.
  void AddConnection(IWebSocketConnection* connection);
  void RemoveConnection(IWebSocketConnection* connection);

 private:
  void OnEvent(const netbox_server::NetBoxEvent& event);
  static std::string EventToJson(const netbox_server::NetBoxEvent& event);

  netbox_server::SessionManager& session_manager_;
  netbox_server::NetBoxEventSubscriptionId subscription_id_;

  std::mutex mutex_;
  std::vector<IWebSocketConnection*> connections_;
};

}  // namespace netbox_api

#endif  // NETBOX_API_WEBSOCKET_GATEWAY_H_
