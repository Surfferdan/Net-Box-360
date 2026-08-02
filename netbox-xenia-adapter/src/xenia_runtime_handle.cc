#include "netbox_xenia_adapter/xenia_runtime_handle.h"

namespace netbox_xenia_adapter {

XeniaRuntimeHandle::XeniaRuntimeHandle(
    std::shared_ptr<IXeniaManagerClient> client, XeniaLaunchRequest request,
    Options options)
    : client_(std::move(client)),
      request_(std::move(request)),
      options_(options) {}

bool XeniaRuntimeHandle::Start() {
  if (!client_) {
    last_state_ = XeniaRuntimeState::kFailed;
    return false;
  }

  XeniaLaunchResult result = client_->LaunchGame(request_);
  if (!result.accepted) {
    last_state_ = XeniaRuntimeState::kFailed;
    return false;
  }
  xenia_session_id_ = result.xenia_session_id;
  last_state_ = XeniaRuntimeState::kLaunching;

  for (int attempt = 0; attempt < options_.ready_poll_attempts; ++attempt) {
    last_state_ = client_->GetStatus(xenia_session_id_);
    if (last_state_ == XeniaRuntimeState::kRunning) {
      return true;
    }
    if (last_state_ == XeniaRuntimeState::kFailed) {
      return false;
    }
    if (options_.poll_delay.count() > 0) {
      std::this_thread::sleep_for(options_.poll_delay);
    }
  }
  // Poll budget exhausted without reaching kRunning or kFailed - treat as
  // a failed start rather than reporting success on an unconfirmed state.
  return false;
}

void XeniaRuntimeHandle::Stop() {
  if (!client_ || xenia_session_id_.empty()) {
    return;
  }

  client_->RequestStop(xenia_session_id_);

  for (int attempt = 0; attempt < options_.stop_confirm_attempts; ++attempt) {
    last_state_ = client_->GetStatus(xenia_session_id_);
    if (last_state_ == XeniaRuntimeState::kStopped ||
        last_state_ == XeniaRuntimeState::kFailed) {
      return;
    }
    if (options_.poll_delay.count() > 0) {
      std::this_thread::sleep_for(options_.poll_delay);
    }
  }
}

bool XeniaRuntimeHandle::IsAlive() const {
  return last_state_ == XeniaRuntimeState::kRunning ||
        last_state_ == XeniaRuntimeState::kLaunching;
}

netbox_server::HealthState XeniaRuntimeHandle::Health() const {
  switch (last_state_) {
    case XeniaRuntimeState::kPending:
    case XeniaRuntimeState::kLaunching:
      return netbox_server::HealthState::kDegraded;
    case XeniaRuntimeState::kRunning:
      return netbox_server::HealthState::kHealthy;
    case XeniaRuntimeState::kFailed:
      return netbox_server::HealthState::kFailed;
    case XeniaRuntimeState::kStopping:
    case XeniaRuntimeState::kStopped:
    case XeniaRuntimeState::kUnknown:
    default:
      return netbox_server::HealthState::kUnknown;
  }
}

}  // namespace netbox_xenia_adapter
