#ifndef NETBOX_XENIA_ADAPTER_TESTS_MOCKS_H_
#define NETBOX_XENIA_ADAPTER_TESTS_MOCKS_H_

#include <deque>
#include <memory>
#include <string>
#include <vector>

#include "netbox_server/stream/stream_handle.h"
#include "netbox_xenia_adapter/xenia_manager_client.h"

// Mocks for netbox-xenia-adapter tests, covering the Phase 11 spec's
// required scenarios: Xenia Manager API launcher failures, runtime
// crashes, and profile loading - without any real Xenia Manager API or
// Xenia process.
namespace netbox_xenia_adapter {
namespace test {

class MockXeniaManagerClient : public IXeniaManagerClient {
 public:
  // If true, LaunchGame() always reports rejected (launcher failure).
  bool fail_launch = false;
  // If true, RequestStop() reports failure (adapter still polls status).
  bool fail_stop = false;
  // Scripted sequence of states returned by successive GetStatus() calls
  // for the "start" phase; once exhausted, the last entry repeats. Lets
  // tests simulate slow-starting runtimes, mid-run crashes, etc.
  std::deque<XeniaRuntimeState> status_sequence{XeniaRuntimeState::kRunning};

  XeniaProfileInfo profile;
  bool fail_profile_load = false;
  std::vector<XeniaAchievementInfo> achievements;
  std::vector<XeniaGameLibraryEntry> games;

  int launch_calls = 0;
  int stop_calls = 0;
  std::string last_launched_game_path;

  XeniaLaunchResult LaunchGame(const XeniaLaunchRequest& request) override {
    ++launch_calls;
    last_launched_game_path = request.game_path;
    XeniaLaunchResult result;
    if (fail_launch) {
      result.accepted = false;
      result.error = "launcher failure";
      return result;
    }
    result.accepted = true;
    result.xenia_session_id = "xenia-session-1";
    return result;
  }

  bool RequestStop(const std::string& xenia_session_id) override {
    ++stop_calls;
    (void)xenia_session_id;
    return !fail_stop;
  }

  XeniaRuntimeState GetStatus(const std::string& xenia_session_id) override {
    (void)xenia_session_id;
    if (status_sequence.empty()) {
      return XeniaRuntimeState::kUnknown;
    }
    XeniaRuntimeState next = status_sequence.front();
    if (status_sequence.size() > 1) {
      status_sequence.pop_front();
    }
    return next;
  }

  XeniaProfileInfo GetActiveProfile(const std::string& netbox_account_id) override {
    (void)netbox_account_id;
    if (fail_profile_load) {
      return XeniaProfileInfo{};
    }
    return profile;
  }

  std::vector<XeniaAchievementInfo> GetAchievements(
      const std::string& profile_id) override {
    (void)profile_id;
    return achievements;
  }

  std::vector<XeniaGameLibraryEntry> ListGames() override { return games; }
};

// Reused from netbox-api's own test pattern (mocks a stream handle so
// session-lifecycle tests can attach/detach a stream without any real
// netbox-streaming/CloudMorph dependency).
class MockStreamHandle : public netbox_server::IStreamHandle {
 public:
  explicit MockStreamHandle(bool fail_to_start = false)
      : fail_to_start_(fail_to_start) {}

  bool Start() override {
    if (fail_to_start_) return false;
    running_ = true;
    return true;
  }
  void Stop() override { running_ = false; }
  netbox_server::HealthState Health() const override {
    return running_ ? netbox_server::HealthState::kHealthy
                    : netbox_server::HealthState::kUnknown;
  }
  uint32_t ConnectedClientCount() const override { return running_ ? 1 : 0; }

 private:
  bool fail_to_start_;
  bool running_ = false;
};

inline netbox_server::StreamFactory MakeStreamFactory(bool fail_to_start = false) {
  return [fail_to_start]() -> std::unique_ptr<netbox_server::IStreamHandle> {
    return std::make_unique<MockStreamHandle>(fail_to_start);
  };
}

}  // namespace test
}  // namespace netbox_xenia_adapter

#endif  // NETBOX_XENIA_ADAPTER_TESTS_MOCKS_H_
