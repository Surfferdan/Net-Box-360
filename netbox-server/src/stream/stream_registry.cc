#include "netbox_server/stream/stream_registry.h"

namespace netbox_server {

StreamId StreamRegistry::CreateStream(const StreamFactory& factory,
                                      const std::string& backend_name) {
  if (!factory) {
    return kInvalidStreamId;
  }
  auto handle = factory();
  if (!handle) {
    return kInvalidStreamId;
  }

  std::lock_guard<std::mutex> lock(mutex_);
  StreamId id = next_id_++;
  Record record;
  record.handle = std::move(handle);
  record.backend_name = backend_name;
  streams_.emplace(id, std::move(record));
  return id;
}

void StreamRegistry::RemoveStream(StreamId id) {
  std::lock_guard<std::mutex> lock(mutex_);
  streams_.erase(id);
}

IStreamHandle* StreamRegistry::GetHandle(StreamId id) const {
  std::lock_guard<std::mutex> lock(mutex_);
  auto it = streams_.find(id);
  if (it == streams_.end()) {
    return nullptr;
  }
  return it->second.handle.get();
}

StreamInfo StreamRegistry::GetInfo(StreamId id) const {
  std::lock_guard<std::mutex> lock(mutex_);
  auto it = streams_.find(id);
  if (it == streams_.end()) {
    return StreamInfo{};
  }
  StreamInfo info;
  info.id = id;
  info.backend_name = it->second.backend_name;
  info.health = it->second.handle->Health();
  info.connected_clients = it->second.handle->ConnectedClientCount();
  return info;
}

std::vector<StreamInfo> StreamRegistry::ListStreams() const {
  std::lock_guard<std::mutex> lock(mutex_);
  std::vector<StreamInfo> infos;
  infos.reserve(streams_.size());
  for (const auto& [id, record] : streams_) {
    StreamInfo info;
    info.id = id;
    info.backend_name = record.backend_name;
    info.health = record.handle->Health();
    info.connected_clients = record.handle->ConnectedClientCount();
    infos.push_back(info);
  }
  return infos;
}

}  // namespace netbox_server
