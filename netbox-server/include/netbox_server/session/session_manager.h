#ifndef NETBOX_SERVER_SESSION_SESSION_MANAGER_H_
#define NETBOX_SERVER_SESSION_SESSION_MANAGER_H_

#include <mutex>
#include <unordered_map>
#include <vector>

#include "netbox_server/events.h"
#include "netbox_server/player/player_registry.h"
#include "netbox_server/runtime/runtime_registry.h"
#include "netbox_server/session/session.h"
#include "netbox_server/stream/stream_registry.h"
#include "netbox_server/types.h"

namespace netbox_server {

// Top-level orchestration object - the "Session Manager" box in the Phase 8
// architecture diagram. Owns a RuntimeRegistry, StreamRegistry, and
// PlayerRegistry, and coordinates them into Session objects. This class
// implements no authentication, no networking protocol, no frontend, and no
// database - it is purely an in-process orchestration layer that a future
// transport/API layer (outside this project) would sit in front of, the
// same "define the seam first" pattern used throughout the NetBox stack
// (IStreamBackend in Phase 6/7, IRuntimeHandle/IStreamHandle here).
//
// Thread safety: all public methods are guarded by a single mutex. This
// runs in a server process orchestrating potentially many sessions, not on
// any emulator thread, so a coarse-grained lock is the right tradeoff here
// (unlike NetBox's in-Xenia ring buffers, which must never block an
// emulator thread - see xenia-netbox's own README for that constraint).
class SessionManager {
 public:
  SessionManager() = default;

  // Creates a new session in the kCreated state: allocates a runtime (via
  // `runtime_factory`) and a stream (via `stream_factory`), but does not
  // start either yet (that's StartSession()'s job, matching the
  // Created -> Starting -> Running state machine). Returns
  // kInvalidSessionId if either factory fails to produce a handle - in
  // that case, no partial session/runtime/stream is left registered.
  SessionId CreateSession(const RuntimeFactory& runtime_factory,
                         const StreamFactory& stream_factory,
                         const std::string& stream_backend_name = "unknown");

  // Transitions kCreated -> kStarting -> (kRunning | kFailed). Starts the
  // session's runtime first; if that fails, the session moves to kFailed
  // and the stream is never started (matches "failed runtime handling" -
  // a session cannot be Running with an unstarted runtime). If the runtime
  // starts but the stream fails, the runtime is stopped again and the
  // session moves to kFailed (no half-running session). Idempotent: calling
  // while already kStarting/kRunning returns the current success/failure
  // state without restarting anything. Returns false if `id` is not
  // tracked.
  bool StartSession(SessionId id);

  // Transitions to kStopping -> kStopped: stops the stream, then the
  // runtime. Safe to call multiple times, and safe even if StartSession()
  // was never called or failed. Returns false if `id` is not tracked.
  bool StopSession(SessionId id);

  // Stops the session if still running, then removes it (and its runtime/
  // stream/players) from every registry. After this call, `id` is no
  // longer valid for any other SessionManager method. Returns false if
  // `id` was never tracked.
  bool DestroySession(SessionId id);

  // Read-only snapshot of every currently tracked session.
  std::vector<Session> ListSessions() const;

  // Read-only snapshot of a single session, or a default-constructed
  // (kInvalidSessionId) Session if `id` is not tracked.
  Session GetSession(SessionId id) const;

  // Assigns a new player to `session` at `controller_slot` (delegates to
  // the owned PlayerRegistry and keeps the Session's `players` list in
  // sync). Returns kInvalidPlayerId if the session doesn't exist or the
  // slot is already occupied.
  PlayerId AssignPlayer(SessionId session, uint32_t controller_slot);

  // Removes `player` from `session` (delegates to PlayerRegistry and keeps
  // the Session's `players` list in sync). Dispatches kPlayerLeft. Returns
  // false if the session doesn't exist.
  bool ReleasePlayer(SessionId session, PlayerId player);

  // Registries are exposed read-only for inspection/testing beyond what
  // Session/RuntimeInfo/StreamInfo/PlayerInfo snapshots already cover.
  const RuntimeRegistry& runtime_registry() const { return runtime_registry_; }
  const StreamRegistry& stream_registry() const { return stream_registry_; }
  const PlayerRegistry& player_registry() const { return player_registry_; }

  // The single event source for this SessionManager - netbox-api's
  // WebSocket gateway subscribes here directly rather than maintaining any
  // event state of its own ("do not create a second event model").
  NetBoxEventBus& events() { return events_; }

 private:
  mutable std::mutex mutex_;
  RuntimeRegistry runtime_registry_;
  StreamRegistry stream_registry_;
  PlayerRegistry player_registry_;
  NetBoxEventBus events_;
  std::unordered_map<SessionId, Session> sessions_;
  SessionId next_id_ = 1;
};

}  // namespace netbox_server

#endif  // NETBOX_SERVER_SESSION_SESSION_MANAGER_H_
