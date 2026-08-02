#include "netbox_server/events.h"

namespace netbox_server {

const char* NetBoxEventTypeToString(NetBoxEventType type) {
  switch (type) {
    case NetBoxEventType::kRuntimeStarted:
      return "RuntimeStarted";
    case NetBoxEventType::kRuntimeStopped:
      return "RuntimeStopped";
    case NetBoxEventType::kRuntimeFailed:
      return "RuntimeFailed";
    case NetBoxEventType::kPlayerJoined:
      return "PlayerJoined";
    case NetBoxEventType::kPlayerLeft:
      return "PlayerLeft";
    case NetBoxEventType::kStreamHealthy:
      return "StreamHealthy";
    case NetBoxEventType::kStreamFailed:
      return "StreamFailed";
    default:
      return "Unknown";
  }
}

NetBoxEventSubscriptionId NetBoxEventBus::Subscribe(
    NetBoxEventListener listener) {
  std::lock_guard<std::mutex> lock(mutex_);
  NetBoxEventSubscriptionId id = next_id_++;
  listeners_.emplace(id, std::move(listener));
  return id;
}

void NetBoxEventBus::Unsubscribe(NetBoxEventSubscriptionId id) {
  std::lock_guard<std::mutex> lock(mutex_);
  listeners_.erase(id);
}

void NetBoxEventBus::Dispatch(const NetBoxEvent& event) const {
  std::vector<NetBoxEventListener> listeners_copy;
  {
    std::lock_guard<std::mutex> lock(mutex_);
    listeners_copy.reserve(listeners_.size());
    for (const auto& [id, listener] : listeners_) {
      listeners_copy.push_back(listener);
    }
  }
  for (const auto& listener : listeners_copy) {
    listener(event);
  }
}

}  // namespace netbox_server
