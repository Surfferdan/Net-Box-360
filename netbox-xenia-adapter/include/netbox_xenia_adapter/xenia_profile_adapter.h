#ifndef NETBOX_XENIA_ADAPTER_XENIA_PROFILE_ADAPTER_H_
#define NETBOX_XENIA_ADAPTER_XENIA_PROFILE_ADAPTER_H_

#include <memory>
#include <string>
#include <vector>

#include "netbox_xenia_adapter/xenia_manager_client.h"

namespace netbox_xenia_adapter {

// Connects a NetBox account to its Xenia profile without duplicating any
// profile/save/achievement storage - every method here is a pure
// passthrough to IXeniaManagerClient (i.e. the real Xenia Manager
// profile/save/achievement services), per the Phase 11 "Profile
// Integration" spec and its explicit restriction against duplicating
// account/save systems.
class XeniaProfileAdapter {
 public:
  explicit XeniaProfileAdapter(std::shared_ptr<IXeniaManagerClient> client);

  // Resolves the Xenia profile linked to a NetBox account id (profile
  // selection). Returns a default-constructed XeniaProfileInfo (empty
  // profile_id) if the client has no client configured or no linked
  // profile exists.
  XeniaProfileInfo ResolveProfileForAccount(const std::string& netbox_account_id);

  // True if the resolved profile confirms it owns its own save data on the
  // Xenia Manager side (save ownership check, not a local copy of saves).
  bool HasSaveOwnership(const std::string& netbox_account_id);

  // Passthrough achievement listing for the profile linked to this NetBox
  // account.
  std::vector<XeniaAchievementInfo> GetAchievements(
      const std::string& netbox_account_id);

 private:
  std::shared_ptr<IXeniaManagerClient> client_;
};

}  // namespace netbox_xenia_adapter

#endif  // NETBOX_XENIA_ADAPTER_XENIA_PROFILE_ADAPTER_H_
