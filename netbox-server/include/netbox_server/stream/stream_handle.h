#ifndef NETBOX_SERVER_STREAM_STREAM_HANDLE_H_
#define NETBOX_SERVER_STREAM_STREAM_HANDLE_H_

#include <cstdint>

#include "netbox_server/types.h"

namespace netbox_server {

// Abstraction over "one running stream backend session" (e.g. a
// netbox-streaming CloudMorphBackend instance, wired to a specific
// NetBoxStreamAdapter). netbox-server does not link against
// netbox-streaming or CloudMorph directly - concrete implementations of
// this interface (not provided in this phase) own that dependency; this
// project only orchestrates via this interface.
//
// Threading contract: same as IRuntimeHandle - Start()/Stop() are called
// from SessionManager's calling thread, never from a Xenia/emulator thread
// or a netbox-streaming consumer thread.
class IStreamHandle {
 public:
  virtual ~IStreamHandle() = default;

  virtual bool Start() = 0;
  virtual void Stop() = 0;

  virtual HealthState Health() const = 0;

  // Number of currently connected viewer/client connections (e.g. WebRTC
  // peers). 0 if not running or no clients connected yet.
  virtual uint32_t ConnectedClientCount() const = 0;
};

}  // namespace netbox_server

#endif  // NETBOX_SERVER_STREAM_STREAM_HANDLE_H_
