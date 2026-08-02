#ifndef NETBOX_SERVER_RUNTIME_RUNTIME_HANDLE_H_
#define NETBOX_SERVER_RUNTIME_RUNTIME_HANDLE_H_

#include "netbox_server/types.h"

namespace netbox_server {

// Abstraction over "one running Xenia instance" (or, in the future, a VM/
// container runtime, or a runtime on a remote host - see the Runtime
// Registry's "Future" notes). netbox-server never launches Xenia directly
// or links against it - concrete implementations of this interface (not
// provided in this phase) are responsible for actually starting a Xenia
// process (or equivalent) and wiring it to a netbox-streaming
// CloudMorphBackend/IStreamBackend instance; this project only orchestrates
// via this interface, the same "define the seam first" approach used for
// IStreamBackend in Phase 6/7.
//
// Threading contract: Start()/Stop() are called from SessionManager's own
// calling thread (never from inside a Xenia/emulator thread, since
// netbox-server is an entirely separate process/orchestration layer sitting
// above Xenia and netbox-streaming). Implementations may block briefly to
// launch/tear down a runtime.
class IRuntimeHandle {
 public:
  virtual ~IRuntimeHandle() = default;

  // Starts the underlying runtime (e.g. launches a Xenia process). Returns
  // true on success.
  virtual bool Start() = 0;

  // Stops the underlying runtime. Safe to call even if Start() was never
  // called or already failed; idempotent.
  virtual void Stop() = 0;

  // True if the runtime process/instance is currently alive.
  virtual bool IsAlive() const = 0;

  // Current health of the runtime (process alive/crashed/degraded/etc).
  virtual HealthState Health() const = 0;
};

}  // namespace netbox_server

#endif  // NETBOX_SERVER_RUNTIME_RUNTIME_HANDLE_H_
