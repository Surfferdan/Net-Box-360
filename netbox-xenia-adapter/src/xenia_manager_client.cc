#include "netbox_xenia_adapter/xenia_manager_client.h"

namespace netbox_xenia_adapter {

const char* XeniaRuntimeStateToString(XeniaRuntimeState state) {
  switch (state) {
    case XeniaRuntimeState::kPending:
      return "pending";
    case XeniaRuntimeState::kLaunching:
      return "launching";
    case XeniaRuntimeState::kRunning:
      return "running";
    case XeniaRuntimeState::kStopping:
      return "stopping";
    case XeniaRuntimeState::kStopped:
      return "stopped";
    case XeniaRuntimeState::kFailed:
      return "failed";
    case XeniaRuntimeState::kUnknown:
    default:
      return "unknown";
  }
}

}  // namespace netbox_xenia_adapter
