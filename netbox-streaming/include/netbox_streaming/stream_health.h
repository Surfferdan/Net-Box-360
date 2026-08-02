#ifndef NETBOX_STREAMING_STREAM_HEALTH_H_
#define NETBOX_STREAMING_STREAM_HEALTH_H_

namespace netbox_streaming {

// Coarse-grained health status for a CloudMorphBackend session. This is
// deliberately simpler than NetBoxStreamAdapter's own
// Stopped/Starting/Running/Failed lifecycle (see netbox_stream_adapter.h in
// the Xenia tree) - StreamHealth answers "is the stream actually healthy
// right now", combining process-alive + WebRTC-connected state into one
// value a caller (or future dashboard) can poll cheaply.
enum class StreamHealth {
  // CloudMorph process is running and (once implemented) the WebRTC
  // session is connected; media is flowing normally.
  kRunning,

  // Start() failed, or the CloudMorph process died/crashed and could not
  // be recovered. Terminal until Start() is called again.
  kFailed,

  // The backend was stopped normally (Stop() called), or has not been
  // started yet, or the WebRTC client disconnected while the process
  // itself is still alive (e.g. waiting for a client to reconnect).
  kDisconnected,
};

const char* StreamHealthToString(StreamHealth health);

}  // namespace netbox_streaming

#endif  // NETBOX_STREAMING_STREAM_HEALTH_H_
