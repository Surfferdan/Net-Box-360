#ifndef NETBOX_SERVER_RUNTIME_RUNTIME_REGISTRY_H_
#define NETBOX_SERVER_RUNTIME_RUNTIME_REGISTRY_H_

#include <functional>
#include <memory>
#include <mutex>
#include <unordered_map>
#include <vector>

#include "netbox_server/runtime/runtime_handle.h"
#include "netbox_server/types.h"

namespace netbox_server {

// Factory for constructing a new IRuntimeHandle - injected so tests (and
// eventually different runtime kinds: local Xenia process, VM/container,
// remote host) can supply their own construction logic without
// RuntimeRegistry needing to know about any of them.
using RuntimeFactory = std::function<std::unique_ptr<IRuntimeHandle>()>;

// Read-only snapshot of one tracked runtime, for ListSessions()-style
// inspection without exposing the owned IRuntimeHandle pointer itself.
struct RuntimeInfo {
  RuntimeId id = kInvalidRuntimeId;
  bool alive = false;
  HealthState health = HealthState::kUnknown;
  StreamId assigned_stream = kInvalidStreamId;
};

// Tracks every runtime instance (today: exclusively local Xenia instances,
// per the Phase 8 "one Xenia, one stream, one user" -> "multiple sessions"
// goal; future: VM/container runtimes, remote hosts - this registry's
// interface does not need to change for that, only the RuntimeFactory
// implementations supplied to CreateRuntime() do).
//
// Thread safety: all public methods are guarded by a single mutex. This is
// an orchestration-layer class running in a server process, not on any
// Xenia/emulator thread, so a coarse-grained lock is appropriate here
// (unlike NetBox's in-process ring buffers, which must never block an
// emulator thread).
class RuntimeRegistry {
 public:
  RuntimeRegistry() = default;

  // Constructs a new runtime via `factory` (does NOT call Start() on it -
  // that's SessionManager's responsibility, matching the Session state
  // machine's Created -> Starting transition). Returns kInvalidRuntimeId if
  // `factory` returns null.
  RuntimeId CreateRuntime(const RuntimeFactory& factory);

  // Removes a tracked runtime. Does not call Stop() - callers must stop the
  // runtime themselves first (SessionManager does this before destroying a
  // session). No-op if `id` is not tracked.
  void RemoveRuntime(RuntimeId id);

  // Returns the underlying handle for `id`, or nullptr if not tracked. The
  // returned pointer remains valid until RemoveRuntime(id) is called.
  IRuntimeHandle* GetHandle(RuntimeId id) const;

  // Associates a stream with a runtime (purely bookkeeping - does not
  // affect either's lifecycle).
  void AssignStream(RuntimeId id, StreamId stream_id);

  // Read-only snapshot of a tracked runtime's current state, refreshed by
  // calling into the handle's IsAlive()/Health(). Returns a default-
  // constructed (kInvalidRuntimeId) RuntimeInfo if not tracked.
  RuntimeInfo GetInfo(RuntimeId id) const;

  // Snapshot of every tracked runtime.
  std::vector<RuntimeInfo> ListRuntimes() const;

 private:
  struct Record {
    std::unique_ptr<IRuntimeHandle> handle;
    StreamId assigned_stream = kInvalidStreamId;
  };

  mutable std::mutex mutex_;
  std::unordered_map<RuntimeId, Record> runtimes_;
  RuntimeId next_id_ = 1;
};

}  // namespace netbox_server

#endif  // NETBOX_SERVER_RUNTIME_RUNTIME_REGISTRY_H_
