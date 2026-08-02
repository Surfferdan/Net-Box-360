#ifndef NETBOX_XENIA_ADAPTER_XENIA_RUNTIME_HANDLE_H_
#define NETBOX_XENIA_ADAPTER_XENIA_RUNTIME_HANDLE_H_

#include <chrono>
#include <memory>
#include <string>
#include <thread>

#include "netbox_server/runtime/runtime_handle.h"
#include "netbox_xenia_adapter/xenia_manager_client.h"

namespace netbox_xenia_adapter {

// Concrete netbox_server::IRuntimeHandle implementation backed by the real
// Xenia Manager API (via IXeniaManagerClient), replacing the placeholder
// mock runtime described in the Phase 11 goal architecture:
//
//   SessionManager -> IRuntimeHandle -> XeniaRuntimeHandle
//                                    -> Xenia Manager API -> Xenia Runtime
//
// Threading contract: same as IRuntimeHandle in general - Start()/Stop()
// are called from SessionManager's own calling thread and may block
// briefly (polling GetStatus()) while waiting for Xenia Manager to report
// the runtime ready/stopped.
class XeniaRuntimeHandle : public netbox_server::IRuntimeHandle {
 public:
  struct Options {
    // How many times to poll GetStatus() while waiting for the runtime to
    // become kRunning (Start) or kStopped (Stop) before giving up.
    int ready_poll_attempts = 20;
    int stop_confirm_attempts = 20;
    // Delay between polls. Defaults to 0 for deterministic, instant unit
    // tests; real wiring should pass a real delay (e.g. 250ms).
    std::chrono::milliseconds poll_delay{0};
  };

  XeniaRuntimeHandle(std::shared_ptr<IXeniaManagerClient> client,
                     XeniaLaunchRequest request, Options options = {});

  // Requests Xenia Manager to launch the game (path/profile/configuration
  // passed straight through to LaunchGame), then waits for the runtime
  // ready event by polling GetStatus() until kRunning, kFailed, or the
  // poll budget is exhausted. Returns true only once kRunning is observed.
  bool Start() override;

  // Requests a clean shutdown via RequestStop(), then polls GetStatus()
  // until kStopped is confirmed or the poll budget is exhausted. Safe to
  // call even if Start() was never called or failed (RequestStop/GetStatus
  // simply no-op on an empty session id).
  void Stop() override;

  bool IsAlive() const override;

  // Maps the last-observed XeniaRuntimeState into NetBox's runtime health
  // vocabulary: kPending/kLaunching -> kDegraded (starting up, not failed),
  // kRunning -> kHealthy, kStopping/kStopped/kUnknown -> kUnknown,
  // kFailed -> kFailed.
  netbox_server::HealthState Health() const override;

  const std::string& xenia_session_id() const { return xenia_session_id_; }
  XeniaRuntimeState last_known_state() const { return last_state_; }

 private:
  std::shared_ptr<IXeniaManagerClient> client_;
  XeniaLaunchRequest request_;
  Options options_;
  std::string xenia_session_id_;
  XeniaRuntimeState last_state_ = XeniaRuntimeState::kUnknown;
};

}  // namespace netbox_xenia_adapter

#endif  // NETBOX_XENIA_ADAPTER_XENIA_RUNTIME_HANDLE_H_
