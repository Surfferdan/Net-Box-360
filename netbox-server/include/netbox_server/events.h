#ifndef NETBOX_SERVER_EVENTS_H_
#define NETBOX_SERVER_EVENTS_H_

#include <functional>
#include <mutex>
#include <unordered_map>
#include <vector>

#include "netbox_server/types.h"

namespace netbox_server {

// The single event vocabulary for netbox-server. This mirrors the same
// "one event model, subscribe/dispatch" concept already used inside
// Xenia's NetBox module (NetBoxRuntime::events()) and NetBoxService's
// event-driven ready flags - netbox-api's WebSocket gateway forwards these
// exact events rather than inventing a second, parallel event model.
enum class NetBoxEventType {
  kRuntimeStarted,
  kRuntimeStopped,
  kRuntimeFailed,
  kPlayerJoined,
  kPlayerLeft,
  kStreamHealthy,
  kStreamFailed,
};

const char* NetBoxEventTypeToString(NetBoxEventType type);

// A single dispatched event. `session`/`player` are populated when
// relevant to the event type (kInvalidPlayerId when not applicable, e.g.
// for runtime/stream events).
struct NetBoxEvent {
  NetBoxEventType type;
  SessionId session = kInvalidSessionId;
  PlayerId player = kInvalidPlayerId;
};

using NetBoxEventListener = std::function<void(const NetBoxEvent&)>;
using NetBoxEventSubscriptionId = uint64_t;

// Minimal subscribe/dispatch event bus. Dispatch() invokes every currently
// subscribed listener synchronously and in-order on the calling thread -
// SessionManager dispatches from within its own mutex-guarded methods, so
// listeners must be quick and must not call back into SessionManager
// re-entrantly (matches the same constraint documented on Xenia's
// NetBoxRuntime event dispatch).
class NetBoxEventBus {
 public:
  NetBoxEventBus() = default;

  NetBoxEventSubscriptionId Subscribe(NetBoxEventListener listener);
  void Unsubscribe(NetBoxEventSubscriptionId id);
  void Dispatch(const NetBoxEvent& event) const;

 private:
  mutable std::mutex mutex_;
  std::unordered_map<NetBoxEventSubscriptionId, NetBoxEventListener> listeners_;
  NetBoxEventSubscriptionId next_id_ = 1;
};

}  // namespace netbox_server

#endif  // NETBOX_SERVER_EVENTS_H_
