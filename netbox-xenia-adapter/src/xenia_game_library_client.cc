#include "netbox_xenia_adapter/xenia_game_library_client.h"

namespace netbox_xenia_adapter {

XeniaGameLibraryClient::XeniaGameLibraryClient(
    std::shared_ptr<IXeniaManagerClient> client)
    : client_(std::move(client)) {}

std::vector<XeniaGameLibraryEntry> XeniaGameLibraryClient::ListAvailableGames() {
  if (!client_) {
    return {};
  }
  return client_->ListGames();
}

}  // namespace netbox_xenia_adapter
