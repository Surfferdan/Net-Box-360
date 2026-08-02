#include "netbox_server/session/session.h"

namespace netbox_server {

const char* SessionStateToString(SessionState state) {
  switch (state) {
    case SessionState::kCreated:
      return "Created";
    case SessionState::kStarting:
      return "Starting";
    case SessionState::kRunning:
      return "Running";
    case SessionState::kStopping:
      return "Stopping";
    case SessionState::kStopped:
      return "Stopped";
    case SessionState::kFailed:
      return "Failed";
    default:
      return "Unknown";
  }
}

}  // namespace netbox_server
