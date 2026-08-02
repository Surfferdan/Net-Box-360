#include "netbox_server/types.h"

namespace netbox_server {

const char* HealthStateToString(HealthState state) {
  switch (state) {
    case HealthState::kUnknown:
      return "Unknown";
    case HealthState::kHealthy:
      return "Healthy";
    case HealthState::kDegraded:
      return "Degraded";
    case HealthState::kFailed:
      return "Failed";
    default:
      return "Unknown";
  }
}

}  // namespace netbox_server
