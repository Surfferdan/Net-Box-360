#ifndef NETBOX_XENIA_ADAPTER_XENIA_EVENT_BRIDGE_H_
#define NETBOX_XENIA_ADAPTER_XENIA_EVENT_BRIDGE_H_

#include <functional>
#include <string>
#include <unordered_map>

#include "netbox_server/events.h"
#include "netbox_server/types.h"

namespace netbox_xenia_adapter {

// Raw shape of an event arriving from the Xenia Manager API's existing
// event bus (BackendEventDto - see `xenia api/XeniaManager.Api/Events/
// BackendEventHub.cs`, forwarded over its own `/ws/events` WebSocket).
// This adapter does not create a second, parallel event model - it only
// translates this existing vocabulary into netbox-server's NetBoxEventBus.
struct XeniaManagerEvent {
  std::string type;
  std::unordered_map<std::string, std::string> data;
};

// Fired for Xenia Manager events that have no equivalent in
// netbox_server::NetBoxEventType (currently only AchievementUnlocked,
// mapped to a "profile update" passthrough per the Phase 11 spec). Kept
// as a separate listener rather than extending NetBoxEventBus's fixed
// enum, since that type is owned by the already-completed netbox-server
// project and should not be modified for this adapter's sake.
struct ProfileUpdateEvent {
  std::string profile_id;
  std::string achievement_id;
};

using ProfileUpdateListener = std::function<void(const ProfileUpdateEvent&)>;

// Bridges Xenia Manager Events -> NetBoxEventBus, per the Phase 11 event
// mapping:
//   XeniaStarted/SessionStarted/SessionReused/SessionStaleRecovered -> RuntimeStarted
//   XeniaStopped/SessionStopped                                     -> RuntimeStopped
//   XeniaError/SessionFailed                                        -> RuntimeFailed
//   PlayerJoined                                                    -> PlayerJoined
//   PlayerLeft                                                      -> PlayerLeft
//   StreamHealthy                                                   -> StreamHealthy
//   StreamFailed                                                    -> StreamFailed
//   AchievementUnlocked                                             -> Profile update event (see ProfileUpdateListener)
class XeniaEventBridge {
 public:
  using SessionIdResolver =
      std::function<netbox_server::SessionId(const std::string& xenia_session_id)>;

  explicit XeniaEventBridge(netbox_server::NetBoxEventBus& bus);

  // Resolves a raw Xenia session id (event data key "sessionId") to a
  // netbox_server::SessionId. Required for every event except
  // AchievementUnlocked. If unset (or resolution returns
  // kInvalidSessionId), the event is dropped rather than dispatched with a
  // bogus session id.
  void SetSessionIdResolver(SessionIdResolver resolver);

  void SetProfileUpdateListener(ProfileUpdateListener listener);

  // Translates and dispatches a single raw event. Unknown event types are
  // ignored (no-op) rather than throwing, since the Xenia Manager event
  // bus already carries unrelated event types (profile/save/config/
  // launcher lifecycle events) that this bridge intentionally does not
  // forward.
  void HandleXeniaEvent(const XeniaManagerEvent& event);

 private:
  netbox_server::NetBoxEventBus& bus_;
  SessionIdResolver resolver_;
  ProfileUpdateListener profile_listener_;
};

}  // namespace netbox_xenia_adapter

#endif  // NETBOX_XENIA_ADAPTER_XENIA_EVENT_BRIDGE_H_
