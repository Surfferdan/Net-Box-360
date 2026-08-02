#ifndef NETBOX_SERVER_PLAYER_PLAYER_REGISTRY_H_
#define NETBOX_SERVER_PLAYER_PLAYER_REGISTRY_H_

#include <mutex>
#include <unordered_map>
#include <vector>

#include "netbox_server/types.h"

namespace netbox_server {

// Connection state for a player slot. Deliberately transport-agnostic -
// "how" a controller connects (browser gamepad API, Bluetooth, a physical
// USB pad passed through for couch multiplayer) is a future concern; this
// registry only tracks that a player is or isn't currently connected.
enum class PlayerConnectionState {
  kConnected,
  kDisconnected,
};

const char* PlayerConnectionStateToString(PlayerConnectionState state);

struct PlayerInfo {
  PlayerId id = kInvalidPlayerId;
  SessionId session = kInvalidSessionId;
  // Xbox 360 controller slot (0-3), matching NetBoxInput's
  // kNetBoxMaxPlayers layout inside Xenia's NetBox module.
  uint32_t controller_slot = 0;
  PlayerConnectionState connection_state = PlayerConnectionState::kDisconnected;
};

// Tracks every player assigned to a session's controller slots. This is
// pure bookkeeping - it does not itself talk to NetBoxInputProvider or any
// Xenia code; a future bridge (outside this project) would read
// PlayerRegistry state and call into a running Xenia instance's NetBox
// input provider accordingly.
//
// Prepared for (not implemented in this phase): browser gamepad
// controllers, Bluetooth controllers, and couch multiplayer (multiple
// players in a single session) - the registry's `session`/`controller_slot`
// fields and multi-player-per-session support already accommodate all
// three; only the actual input-transport bridges are future work.
class PlayerRegistry {
 public:
  PlayerRegistry() = default;

  // Assigns a new player to `session` at `controller_slot` (0-3). Returns
  // kInvalidPlayerId if `controller_slot` is already occupied within that
  // session (each session's controller slots are exclusive, matching
  // Xenia's 4-controller limit).
  PlayerId AssignPlayer(SessionId session, uint32_t controller_slot);

  // Removes a player entirely (e.g. session destroyed or player left for
  // good). No-op if `player` is not tracked.
  void ReleasePlayer(PlayerId player);

  // Removes every player belonging to `session` (used by SessionManager on
  // DestroySession()).
  void ReleaseSessionPlayers(SessionId session);

  void SetConnectionState(PlayerId player, PlayerConnectionState state);

  PlayerInfo GetInfo(PlayerId player) const;
  std::vector<PlayerInfo> ListPlayersForSession(SessionId session) const;
  std::vector<PlayerInfo> ListAllPlayers() const;

 private:
  mutable std::mutex mutex_;
  std::unordered_map<PlayerId, PlayerInfo> players_;
  PlayerId next_id_ = 1;
};

}  // namespace netbox_server

#endif  // NETBOX_SERVER_PLAYER_PLAYER_REGISTRY_H_
