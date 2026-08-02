#include "netbox_server/player/player_registry.h"

namespace netbox_server {

const char* PlayerConnectionStateToString(PlayerConnectionState state) {
  switch (state) {
    case PlayerConnectionState::kConnected:
      return "Connected";
    case PlayerConnectionState::kDisconnected:
      return "Disconnected";
    default:
      return "Unknown";
  }
}

PlayerId PlayerRegistry::AssignPlayer(SessionId session,
                                      uint32_t controller_slot) {
  std::lock_guard<std::mutex> lock(mutex_);
  for (const auto& [id, info] : players_) {
    if (info.session == session && info.controller_slot == controller_slot) {
      return kInvalidPlayerId;  // Slot already occupied for this session.
    }
  }

  PlayerId id = next_id_++;
  PlayerInfo info;
  info.id = id;
  info.session = session;
  info.controller_slot = controller_slot;
  info.connection_state = PlayerConnectionState::kConnected;
  players_.emplace(id, info);
  return id;
}

void PlayerRegistry::ReleasePlayer(PlayerId player) {
  std::lock_guard<std::mutex> lock(mutex_);
  players_.erase(player);
}

void PlayerRegistry::ReleaseSessionPlayers(SessionId session) {
  std::lock_guard<std::mutex> lock(mutex_);
  for (auto it = players_.begin(); it != players_.end();) {
    if (it->second.session == session) {
      it = players_.erase(it);
    } else {
      ++it;
    }
  }
}

void PlayerRegistry::SetConnectionState(PlayerId player,
                                        PlayerConnectionState state) {
  std::lock_guard<std::mutex> lock(mutex_);
  auto it = players_.find(player);
  if (it == players_.end()) {
    return;
  }
  it->second.connection_state = state;
}

PlayerInfo PlayerRegistry::GetInfo(PlayerId player) const {
  std::lock_guard<std::mutex> lock(mutex_);
  auto it = players_.find(player);
  if (it == players_.end()) {
    return PlayerInfo{};
  }
  return it->second;
}

std::vector<PlayerInfo> PlayerRegistry::ListPlayersForSession(
    SessionId session) const {
  std::lock_guard<std::mutex> lock(mutex_);
  std::vector<PlayerInfo> infos;
  for (const auto& [id, info] : players_) {
    if (info.session == session) {
      infos.push_back(info);
    }
  }
  return infos;
}

std::vector<PlayerInfo> PlayerRegistry::ListAllPlayers() const {
  std::lock_guard<std::mutex> lock(mutex_);
  std::vector<PlayerInfo> infos;
  infos.reserve(players_.size());
  for (const auto& [id, info] : players_) {
    infos.push_back(info);
  }
  return infos;
}

}  // namespace netbox_server
