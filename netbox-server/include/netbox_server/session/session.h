#ifndef NETBOX_SERVER_SESSION_SESSION_H_
#define NETBOX_SERVER_SESSION_SESSION_H_

#include <vector>

#include "netbox_server/types.h"

namespace netbox_server {

enum class SessionState {
  kCreated,
  kStarting,
  kRunning,
  kStopping,
  kStopped,
  kFailed,
};

const char* SessionStateToString(SessionState state);

// Plain-data snapshot of one NetBox session - the "one Xenia + one Stream +
// N players" unit SessionManager orchestrates. Returned by value from
// SessionManager so callers never hold a reference into internal state that
// could be mutated/destroyed concurrently.
struct Session {
  SessionId id = kInvalidSessionId;
  RuntimeId runtime = kInvalidRuntimeId;
  StreamId stream = kInvalidStreamId;
  std::vector<PlayerId> players;
  SessionState state = SessionState::kCreated;
};

}  // namespace netbox_server

#endif  // NETBOX_SERVER_SESSION_SESSION_H_
