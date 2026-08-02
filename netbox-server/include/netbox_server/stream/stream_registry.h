#ifndef NETBOX_SERVER_STREAM_STREAM_REGISTRY_H_
#define NETBOX_SERVER_STREAM_STREAM_REGISTRY_H_

#include <cstdint>
#include <functional>
#include <memory>
#include <mutex>
#include <string>
#include <unordered_map>
#include <vector>

#include "netbox_server/stream/stream_handle.h"
#include "netbox_server/types.h"

namespace netbox_server {

using StreamFactory = std::function<std::unique_ptr<IStreamHandle>()>;

struct StreamInfo {
  StreamId id = kInvalidStreamId;
  std::string backend_name;
  HealthState health = HealthState::kUnknown;
  uint32_t connected_clients = 0;
};

// Tracks every stream backend session (today: netbox-streaming's
// CloudMorphBackend via IStreamHandle; future: LocalLAN/other transports -
// this registry's interface does not need to change for that). Mirrors
// RuntimeRegistry's shape/thread-safety approach.
class StreamRegistry {
 public:
  StreamRegistry() = default;

  // `backend_name` is purely descriptive/diagnostic (e.g. "cloudmorph",
  // "local-lan", "mock") - not used for any dispatch logic here.
  StreamId CreateStream(const StreamFactory& factory,
                        const std::string& backend_name);

  void RemoveStream(StreamId id);

  IStreamHandle* GetHandle(StreamId id) const;

  StreamInfo GetInfo(StreamId id) const;
  std::vector<StreamInfo> ListStreams() const;

 private:
  struct Record {
    std::unique_ptr<IStreamHandle> handle;
    std::string backend_name;
  };

  mutable std::mutex mutex_;
  std::unordered_map<StreamId, Record> streams_;
  StreamId next_id_ = 1;
};

}  // namespace netbox_server

#endif  // NETBOX_SERVER_STREAM_STREAM_REGISTRY_H_
