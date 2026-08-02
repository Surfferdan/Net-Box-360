#include "netbox_xenia_adapter/xenia_event_bridge.h"

namespace netbox_xenia_adapter {

namespace {

std::string FindOr(const std::unordered_map<std::string, std::string>& data,
                   const std::string& key) {
  auto it = data.find(key);
  return it == data.end() ? std::string() : it->second;
}

}  // namespace

XeniaEventBridge::XeniaEventBridge(netbox_server::NetBoxEventBus& bus)
    : bus_(bus) {}

void XeniaEventBridge::SetSessionIdResolver(SessionIdResolver resolver) {
  resolver_ = std::move(resolver);
}

void XeniaEventBridge::SetProfileUpdateListener(ProfileUpdateListener listener) {
  profile_listener_ = std::move(listener);
}

void XeniaEventBridge::HandleXeniaEvent(const XeniaManagerEvent& event) {
  if (event.type == "AchievementUnlocked") {
    if (profile_listener_) {
      ProfileUpdateEvent profile_event;
      profile_event.profile_id = FindOr(event.data, "profileId");
      profile_event.achievement_id = FindOr(event.data, "achievementId");
      profile_listener_(profile_event);
    }
    return;
  }

  netbox_server::NetBoxEventType mapped_type;
  if (event.type == "XeniaStarted" || event.type == "SessionStarted" ||
      event.type == "SessionReused" || event.type == "SessionStaleRecovered") {
    mapped_type = netbox_server::NetBoxEventType::kRuntimeStarted;
  } else if (event.type == "XeniaStopped" || event.type == "SessionStopped") {
    mapped_type = netbox_server::NetBoxEventType::kRuntimeStopped;
  } else if (event.type == "XeniaError" || event.type == "SessionFailed") {
    mapped_type = netbox_server::NetBoxEventType::kRuntimeFailed;
  } else if (event.type == "PlayerJoined") {
    mapped_type = netbox_server::NetBoxEventType::kPlayerJoined;
  } else if (event.type == "PlayerLeft") {
    mapped_type = netbox_server::NetBoxEventType::kPlayerLeft;
  } else if (event.type == "StreamHealthy") {
    mapped_type = netbox_server::NetBoxEventType::kStreamHealthy;
  } else if (event.type == "StreamFailed") {
    mapped_type = netbox_server::NetBoxEventType::kStreamFailed;
  } else {
    // Unrelated Xenia Manager event (profile/save/config/launcher
    // lifecycle events not part of the Phase 11 mapping) - ignore.
    return;
  }

  if (!resolver_) {
    return;
  }
  std::string xenia_session_id = FindOr(event.data, "sessionId");
  netbox_server::SessionId session_id = resolver_(xenia_session_id);
  if (session_id == netbox_server::kInvalidSessionId) {
    return;
  }

  netbox_server::NetBoxEvent netbox_event;
  netbox_event.type = mapped_type;
  netbox_event.session = session_id;
  bus_.Dispatch(netbox_event);
}

}  // namespace netbox_xenia_adapter
