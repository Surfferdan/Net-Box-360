#include "netbox_streaming/cloudmorph_backend.h"

#include <chrono>

#include "netbox_streaming/cloudmorph_process.h"

namespace netbox_streaming {

CloudMorphBackend::CloudMorphBackend(Options options,
                                     std::unique_ptr<IProcessLauncher> launcher)
    : options_(std::move(options)) {
  if (launcher) {
    owned_launcher_ = std::move(launcher);
  } else {
    owned_launcher_ = std::make_unique<CloudMorphProcess>();
  }
  launcher_ = owned_launcher_.get();
}

CloudMorphBackend::~CloudMorphBackend() { Stop(); }

bool CloudMorphBackend::Start() {
  if (running_.load(std::memory_order_acquire)) {
    // Idempotent, matching NetBoxStreamAdapter::StartStream()'s contract.
    return health_.load(std::memory_order_acquire) == StreamHealth::kRunning;
  }

  if (!launcher_->Launch(options_.executable_path, options_.args)) {
    health_.store(StreamHealth::kFailed, std::memory_order_release);
    return false;
  }

  {
    std::lock_guard<std::mutex> video_lock(video_mutex_);
    video_queue_.clear();
    dropped_video_frame_count_ = 0;
    video_frames_processed_ = 0;
  }
  {
    std::lock_guard<std::mutex> audio_lock(audio_mutex_);
    audio_queue_.clear();
    dropped_audio_packet_count_ = 0;
    audio_packets_processed_ = 0;
  }

  running_.store(true, std::memory_order_release);
  health_.store(StreamHealth::kRunning, std::memory_order_release);
  monitor_thread_ = std::thread([this]() { MonitorThreadMain(); });
  return true;
}

void CloudMorphBackend::Stop() {
  if (!running_.exchange(false)) {
    // Already stopped - still make sure the process (if launched despite
    // running_ being false, e.g. a failed Start()) isn't left dangling.
    launcher_->Terminate();
    if (health_.load(std::memory_order_acquire) != StreamHealth::kFailed) {
      health_.store(StreamHealth::kDisconnected, std::memory_order_release);
    }
    return;
  }

  if (monitor_thread_.joinable()) {
    monitor_thread_.join();
  }
  launcher_->Terminate();
  health_.store(StreamHealth::kDisconnected, std::memory_order_release);
}

void CloudMorphBackend::SubmitVideoFrame(const xe::netbox::VideoFrame& frame) {
  // Never blocks: a single short critical section, then return. Timestamps
  // (timestamp_us) and frame_id are copied as-is - never modified - so a
  // future encoder/WebRTC layer can still reconstruct original timing and
  // detect gaps exactly as NetBoxVideo intended.
  std::lock_guard<std::mutex> lock(video_mutex_);
  video_queue_.push_back(frame);
  if (video_queue_.size() > options_.max_queued_video_frames) {
    video_queue_.pop_front();
    ++dropped_video_frame_count_;
  }
}

void CloudMorphBackend::SubmitAudioPacket(const xe::netbox::AudioPacket& packet) {
  // Same non-blocking, drop-oldest-on-overflow policy as video, preserving
  // FIFO arrival order for a future PCM conversion layer.
  std::lock_guard<std::mutex> lock(audio_mutex_);
  audio_queue_.push_back(packet);
  if (audio_queue_.size() > options_.max_queued_audio_packets) {
    audio_queue_.pop_front();
    ++dropped_audio_packet_count_;
  }
}

StreamHealth CloudMorphBackend::health() const {
  return health_.load(std::memory_order_acquire);
}

bool CloudMorphBackend::is_process_alive() const {
  return launcher_->IsAlive();
}

bool CloudMorphBackend::is_webrtc_connected() const {
  // No real WebRTC session exists in this phase - always false until a
  // future phase wires up an actual connection state.
  return false;
}

uint64_t CloudMorphBackend::dropped_video_frame_count() const {
  std::lock_guard<std::mutex> lock(video_mutex_);
  return dropped_video_frame_count_;
}

uint64_t CloudMorphBackend::dropped_audio_packet_count() const {
  std::lock_guard<std::mutex> lock(audio_mutex_);
  return dropped_audio_packet_count_;
}

uint64_t CloudMorphBackend::video_frames_processed() const {
  std::lock_guard<std::mutex> lock(video_mutex_);
  return video_frames_processed_;
}

uint64_t CloudMorphBackend::audio_packets_processed() const {
  std::lock_guard<std::mutex> lock(audio_mutex_);
  return audio_packets_processed_;
}

void CloudMorphBackend::MonitorThreadMain() {
  while (running_.load(std::memory_order_acquire)) {
    if (!launcher_->IsAlive()) {
      // The CloudMorph process died unexpectedly - this is a failure, not
      // a normal disconnect, matching the Phase 7 health-reporting
      // requirement to track "process alive".
      health_.store(StreamHealth::kFailed, std::memory_order_release);
      running_.store(false, std::memory_order_release);
      break;
    }

    DrainVideoQueue();
    DrainAudioQueue();

    std::this_thread::sleep_for(
        std::chrono::milliseconds(options_.monitor_interval_ms));
  }
}

void CloudMorphBackend::DrainVideoQueue() {
  // Stand-in for "hand frames to the encoder / WebRTC video track". No
  // actual encoding/transport happens in this phase - frames are simply
  // consumed in FIFO order and counted, preserving the same ordering
  // guarantee a real encoder would need.
  std::lock_guard<std::mutex> lock(video_mutex_);
  while (!video_queue_.empty()) {
    video_queue_.pop_front();
    ++video_frames_processed_;
  }
}

void CloudMorphBackend::DrainAudioQueue() {
  // Stand-in for "hand packets to the PCM conversion layer / WebRTC audio
  // track". Same no-op-but-ordered-and-counted behavior as video.
  std::lock_guard<std::mutex> lock(audio_mutex_);
  while (!audio_queue_.empty()) {
    audio_queue_.pop_front();
    ++audio_packets_processed_;
  }
}

}  // namespace netbox_streaming
