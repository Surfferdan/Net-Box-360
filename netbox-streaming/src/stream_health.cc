#include "netbox_streaming/stream_health.h"

namespace netbox_streaming {

const char* StreamHealthToString(StreamHealth health) {
  switch (health) {
    case StreamHealth::kRunning:
      return "Running";
    case StreamHealth::kFailed:
      return "Failed";
    case StreamHealth::kDisconnected:
      return "Disconnected";
    default:
      return "Unknown";
  }
}

}  // namespace netbox_streaming
