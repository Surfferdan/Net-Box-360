#ifndef NETBOX_API_WEBSOCKET_CONNECTION_H_
#define NETBOX_API_WEBSOCKET_CONNECTION_H_

#include <string>

namespace netbox_api {

// One connected /ws/events client. A real WebSocket server implementation
// (outside this project) would implement this on top of its own socket/
// framing code; tests use a simple in-memory mock that just records sent
// text frames.
class IWebSocketConnection {
 public:
  virtual ~IWebSocketConnection() = default;

  virtual void Send(const std::string& text_message) = 0;
  virtual bool IsOpen() const = 0;
};

}  // namespace netbox_api

#endif  // NETBOX_API_WEBSOCKET_CONNECTION_H_
