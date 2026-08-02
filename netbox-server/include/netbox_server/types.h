#ifndef NETBOX_SERVER_TYPES_H_
#define NETBOX_SERVER_TYPES_H_

#include <cstdint>
#include <string>

namespace netbox_server {

using SessionId = uint64_t;
using RuntimeId = uint64_t;
using StreamId = uint64_t;
using PlayerId = uint64_t;

constexpr SessionId kInvalidSessionId = 0;
constexpr RuntimeId kInvalidRuntimeId = 0;
constexpr StreamId kInvalidStreamId = 0;
constexpr PlayerId kInvalidPlayerId = 0;

// Shared health vocabulary across runtime/stream registries - deliberately
// small and orchestration-only (no process-level detail here; that lives in
// each concrete IRuntimeHandle/IStreamHandle implementation, e.g. a future
// Xenia-process runtime handle or the existing netbox-streaming
// CloudMorphBackend).
enum class HealthState {
  kUnknown,
  kHealthy,
  kDegraded,
  kFailed,
};

const char* HealthStateToString(HealthState state);

}  // namespace netbox_server

#endif  // NETBOX_SERVER_TYPES_H_
