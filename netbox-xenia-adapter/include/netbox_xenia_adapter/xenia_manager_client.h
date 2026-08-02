#ifndef NETBOX_XENIA_ADAPTER_XENIA_MANAGER_CLIENT_H_
#define NETBOX_XENIA_ADAPTER_XENIA_MANAGER_CLIENT_H_

#include <memory>
#include <string>
#include <vector>

namespace netbox_xenia_adapter {

// Runtime state vocabulary as reported by the Xenia Manager API
// (xenia api/XeniaManager.Api - a separate, pre-existing .NET project).
// This mirrors XeniaManager.Api's session process-state strings
// (pending/launching/running/stopping/stopped/failed, see
// RuntimeSessionState.cs) - kept as its own enum here rather than reusing
// netbox_server::HealthState, since XeniaRuntimeHandle::Status() (per the
// Phase 11 spec) explicitly "maps Xenia states into NetBox runtime
// states", implying two distinct vocabularies with an explicit mapping
// step, not a shared type.
enum class XeniaRuntimeState {
  kUnknown,
  kPending,
  kLaunching,
  kRunning,
  kStopping,
  kStopped,
  kFailed,
};

const char* XeniaRuntimeStateToString(XeniaRuntimeState state);

// Mirrors XeniaManager.Api's StartGameSessionRequest shape closely enough
// for this adapter's purposes: game path, profile, and configuration are
// passed straight through to the existing launcher/profile/configuration
// services - this project does not reinterpret or duplicate any of them.
struct XeniaLaunchRequest {
  std::string game_path;
  std::string profile_id;
  std::string configuration_id;
};

struct XeniaLaunchResult {
  bool accepted = false;
  std::string xenia_session_id;
  std::string error;
};

// Read-only passthrough of a Xenia Manager profile - "connect NetBox
// account to Xenia profile" per the Phase 11 spec, without duplicating any
// profile storage. `has_save_ownership` reflects whether the resolved
// profile is confirmed to own its save data on the Xenia Manager side.
struct XeniaProfileInfo {
  std::string profile_id;
  std::string display_name;
  bool has_save_ownership = false;
};

// One entry from the Xenia Manager's existing game library, reshaped for
// the Games Blade per the Phase 11 spec (title/executable path/cover
// metadata/last played only - no new catalog fields invented here).
struct XeniaGameLibraryEntry {
  std::string title;
  std::string executable_path;
  std::string cover_path;
  std::string last_played_iso8601;
};

// Achievement passthrough item - forwarded, never duplicated/stored by
// this adapter.
struct XeniaAchievementInfo {
  std::string achievement_id;
  std::string title;
  bool unlocked = false;
};

// The single seam between this adapter and the real Xenia Manager API's
// HTTP surface (LauncherController/ProfilesController/SavesController/
// AchievementsController/ConfigController/SessionController in
// `xenia api/XeniaManager.Api/Controllers`). No concrete HTTP transport
// implementation is provided in this phase - the real implementation
// (not part of these unit tests) is expected to be a thin HTTP client
// calling those existing endpoints; this project only orchestrates via
// this interface, the same "define the seam first" approach used
// throughout the NetBox stack (IRuntimeHandle/IStreamHandle, etc).
class IXeniaManagerClient {
 public:
  virtual ~IXeniaManagerClient() = default;

  // Requests Xenia Manager to launch a game (game path + profile +
  // configuration). Does not block until the runtime is fully ready -
  // callers should poll GetStatus() for that (see XeniaRuntimeHandle).
  virtual XeniaLaunchResult LaunchGame(const XeniaLaunchRequest& request) = 0;

  // Requests a clean shutdown of a previously launched runtime. Does not
  // block until the runtime is fully stopped - callers should poll
  // GetStatus() to confirm.
  virtual bool RequestStop(const std::string& xenia_session_id) = 0;

  // Current runtime state as last reported by Xenia Manager.
  virtual XeniaRuntimeState GetStatus(const std::string& xenia_session_id) = 0;

  // Resolves the Xenia profile linked to a NetBox account id. Never
  // creates/stores a profile - purely a read passthrough.
  virtual XeniaProfileInfo GetActiveProfile(
      const std::string& netbox_account_id) = 0;

  // Achievements for a given profile - passthrough only, no local storage.
  virtual std::vector<XeniaAchievementInfo> GetAchievements(
      const std::string& profile_id) = 0;

  // Existing Xenia Manager game library (GamesController's catalog),
  // reshaped for Games Blade consumption.
  virtual std::vector<XeniaGameLibraryEntry> ListGames() = 0;
};

}  // namespace netbox_xenia_adapter

#endif  // NETBOX_XENIA_ADAPTER_XENIA_MANAGER_CLIENT_H_
