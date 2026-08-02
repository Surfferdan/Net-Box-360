#ifndef NETBOX_XENIA_ADAPTER_XENIA_GAME_LIBRARY_CLIENT_H_
#define NETBOX_XENIA_ADAPTER_XENIA_GAME_LIBRARY_CLIENT_H_

#include <memory>
#include <vector>

#include "netbox_xenia_adapter/xenia_manager_client.h"

namespace netbox_xenia_adapter {

// Exposes the existing Xenia Manager game library to the Games Blade:
//   Games Blade -> Xenia Manager Game Library -> Available Games
// Never recreates or caches a second copy of the catalog - every call
// re-queries IXeniaManagerClient::ListGames() (the existing
// GamesController's catalog, per the Phase 11 spec).
class XeniaGameLibraryClient {
 public:
  explicit XeniaGameLibraryClient(std::shared_ptr<IXeniaManagerClient> client);

  std::vector<XeniaGameLibraryEntry> ListAvailableGames();

 private:
  std::shared_ptr<IXeniaManagerClient> client_;
};

}  // namespace netbox_xenia_adapter

#endif  // NETBOX_XENIA_ADAPTER_XENIA_GAME_LIBRARY_CLIENT_H_
