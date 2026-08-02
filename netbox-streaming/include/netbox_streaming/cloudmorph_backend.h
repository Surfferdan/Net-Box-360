#ifndef NETBOX_STREAMING_CLOUDMORPH_BACKEND_H_
#define NETBOX_STREAMING_CLOUDMORPH_BACKEND_H_

#include <atomic>
#include <cstdint>
#include <deque>
#include <memory>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

// These two headers are the ONLY dependency this project has on the Xenia
// tree, and they are pure, dependency-free data/interface definitions
// (VideoFrame/AudioPacket are plain structs of std:: containers;
// IStreamBackend is a pure-virtual interface) - no Xenia .cc file, .lib, or
// any other Xenia subsystem is compiled, linked, or referenced here. See
// this project's CMakeLists.txt for how the include path is wired up.
#include "xenia/netbox/netbox_audio.h"
#include "xenia/netbox/netbox_stream_backend.h"
#include "xenia/netbox/netbox_video.h"

#include "netbox_streaming/process_launcher.h"
#include "netbox_streaming/stream_health.h"

namespace netbox_streaming {

// Real (non-Xenia) implementation of xe::netbox::IStreamBackend that
// forwards NetBox's captured audio/video to an externally-running
// CloudMorph process over (eventually) a WebRTC session. This phase does
// NOT implement the actual WebRTC transport or media encoding - it
// implements the process lifecycle, queuing, ordering, and health-reporting
// scaffolding that a future encoder/WebRTC phase will plug into, matching
// the same "define the seam first" approach used for NetBoxStreamAdapter/
// IStreamBackend inside Xenia's own NetBox module.
//
// CloudMorph itself is never embedded as source or linked as a library
// here - it is launched as a separate OS process via IProcessLauncher (see
// process_launcher.h / cloudmorph_process.h), and all communication with it
// is expected to happen over IPC/WebRTC in a future phase, not via direct
// function calls into CloudMorph code.
class CloudMorphBackend : public xe::netbox::IStreamBackend {
 public:
  struct Options {
    // Path to the CloudMorph executable to launch on Start().
    std::string executable_path;
    // Extra command-line arguments passed to the CloudMorph process.
    std::vector<std::string> args;
    // Bounded queue sizes - kept small like NetBoxVideo/NetBoxAudio's own
    // ring buffers, since SubmitVideoFrame()/SubmitAudioPacket() must never
    // block the NetBoxStreamAdapter consumer thread that calls them.
    size_t max_queued_video_frames = 8;
    size_t max_queued_audio_packets = 64;
    // How often the internal media/monitor thread checks process liveness
    // and drains queues, in milliseconds.
    int monitor_interval_ms = 20;
  };

  // `launcher` is optional - if null, CloudMorphBackend constructs and owns
  // a real CloudMorphProcess internally. Tests should inject a fake
  // IProcessLauncher here instead, so no real CloudMorph executable is
  // required to exercise this class's lifecycle/queuing logic.
  explicit CloudMorphBackend(Options options,
                            std::unique_ptr<IProcessLauncher> launcher = nullptr);
  ~CloudMorphBackend() override;

  CloudMorphBackend(const CloudMorphBackend&) = delete;
  CloudMorphBackend& operator=(const CloudMorphBackend&) = delete;

  // IStreamBackend:
  bool Start() override;
  void Stop() override;
  void SubmitVideoFrame(const xe::netbox::VideoFrame& frame) override;
  void SubmitAudioPacket(const xe::netbox::AudioPacket& packet) override;

  // Health reporting (Phase 7 requirement).
  StreamHealth health() const;
  bool is_process_alive() const;
  // Stubbed false in this phase - no real WebRTC session exists yet. Once a
  // future phase establishes real WebRTC connectivity, this should reflect
  // that session's actual connected state instead of a constant.
  bool is_webrtc_connected() const;
  uint64_t dropped_video_frame_count() const;
  uint64_t dropped_audio_packet_count() const;

  // Diagnostics only, for tests: counts of frames/packets that made it
  // through the queue to the monitor thread (i.e. were NOT dropped).
  uint64_t video_frames_processed() const;
  uint64_t audio_packets_processed() const;

 private:
  void MonitorThreadMain();
  void DrainVideoQueue();
  void DrainAudioQueue();

  Options options_;
  std::unique_ptr<IProcessLauncher> owned_launcher_;
  IProcessLauncher* launcher_ = nullptr;  // Points at owned_launcher_.

  std::atomic<StreamHealth> health_{StreamHealth::kDisconnected};
  std::atomic<bool> running_{false};
  std::thread monitor_thread_;

  // Video queue: FIFO, oldest dropped on overflow (mirrors NetBoxVideo's
  // own overflow policy) - preserves arrival order and each VideoFrame's
  // original timestamp_us/frame_id untouched.
  mutable std::mutex video_mutex_;
  std::deque<xe::netbox::VideoFrame> video_queue_;
  uint64_t dropped_video_frame_count_ = 0;
  uint64_t video_frames_processed_ = 0;

  // Audio queue: FIFO, oldest dropped on overflow (mirrors NetBoxAudio's
  // own overflow policy) - preserves packet arrival order for a future PCM
  // conversion layer to process sequentially.
  mutable std::mutex audio_mutex_;
  std::deque<xe::netbox::AudioPacket> audio_queue_;
  uint64_t dropped_audio_packet_count_ = 0;
  uint64_t audio_packets_processed_ = 0;
};

}  // namespace netbox_streaming

#endif  // NETBOX_STREAMING_CLOUDMORPH_BACKEND_H_
