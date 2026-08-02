#include "netbox_xenia_adapter/xenia_profile_adapter.h"

namespace netbox_xenia_adapter {

XeniaProfileAdapter::XeniaProfileAdapter(
    std::shared_ptr<IXeniaManagerClient> client)
    : client_(std::move(client)) {}

XeniaProfileInfo XeniaProfileAdapter::ResolveProfileForAccount(
    const std::string& netbox_account_id) {
  if (!client_) {
    return XeniaProfileInfo{};
  }
  return client_->GetActiveProfile(netbox_account_id);
}

bool XeniaProfileAdapter::HasSaveOwnership(
    const std::string& netbox_account_id) {
  return ResolveProfileForAccount(netbox_account_id).has_save_ownership;
}

std::vector<XeniaAchievementInfo> XeniaProfileAdapter::GetAchievements(
    const std::string& netbox_account_id) {
  if (!client_) {
    return {};
  }
  XeniaProfileInfo profile = ResolveProfileForAccount(netbox_account_id);
  if (profile.profile_id.empty()) {
    return {};
  }
  return client_->GetAchievements(profile.profile_id);
}

}  // namespace netbox_xenia_adapter
