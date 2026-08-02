#include "netbox_server/runtime/runtime_registry.h"

namespace netbox_server {

RuntimeId RuntimeRegistry::CreateRuntime(const RuntimeFactory& factory) {
  if (!factory) {
    return kInvalidRuntimeId;
  }
  auto handle = factory();
  if (!handle) {
    return kInvalidRuntimeId;
  }

  std::lock_guard<std::mutex> lock(mutex_);
  RuntimeId id = next_id_++;
  Record record;
  record.handle = std::move(handle);
  runtimes_.emplace(id, std::move(record));
  return id;
}

void RuntimeRegistry::RemoveRuntime(RuntimeId id) {
  std::lock_guard<std::mutex> lock(mutex_);
  runtimes_.erase(id);
}

IRuntimeHandle* RuntimeRegistry::GetHandle(RuntimeId id) const {
  std::lock_guard<std::mutex> lock(mutex_);
  auto it = runtimes_.find(id);
  if (it == runtimes_.end()) {
    return nullptr;
  }
  return it->second.handle.get();
}

void RuntimeRegistry::AssignStream(RuntimeId id, StreamId stream_id) {
  std::lock_guard<std::mutex> lock(mutex_);
  auto it = runtimes_.find(id);
  if (it == runtimes_.end()) {
    return;
  }
  it->second.assigned_stream = stream_id;
}

RuntimeInfo RuntimeRegistry::GetInfo(RuntimeId id) const {
  std::lock_guard<std::mutex> lock(mutex_);
  auto it = runtimes_.find(id);
  if (it == runtimes_.end()) {
    return RuntimeInfo{};
  }
  RuntimeInfo info;
  info.id = id;
  info.alive = it->second.handle->IsAlive();
  info.health = it->second.handle->Health();
  info.assigned_stream = it->second.assigned_stream;
  return info;
}

std::vector<RuntimeInfo> RuntimeRegistry::ListRuntimes() const {
  std::lock_guard<std::mutex> lock(mutex_);
  std::vector<RuntimeInfo> infos;
  infos.reserve(runtimes_.size());
  for (const auto& [id, record] : runtimes_) {
    RuntimeInfo info;
    info.id = id;
    info.alive = record.handle->IsAlive();
    info.health = record.handle->Health();
    info.assigned_stream = record.assigned_stream;
    infos.push_back(info);
  }
  return infos;
}

}  // namespace netbox_server
