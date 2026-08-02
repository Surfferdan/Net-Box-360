#include "netbox_streaming/cloudmorph_backend.h"

#include <atomic>
#include <chrono>
#include <thread>

#include "mini_test.h"
#include "netbox_streaming/process_launcher.h"

// Mock-backed tests for CloudMorphBackend (Phase 7). These use a
// MockProcessLauncher instead of CloudMorphProcess, so no real CloudMorph
// executable needs to exist or be installed for these tests to run - they
// exercise Start()/Stop()/queuing/health-reporting logic in isolation.

namespace netbox_streaming {
namespace test {

namespace {

// Fully local IProcessLauncher stand-in. `fail_launch` simulates CloudMorph
// failing to start (e.g. missing executable); `alive` can be flipped by a
// test to simulate the process crashing while "running".
class MockProcessLauncher : public IProcessLauncher {
 public:
  bool Launch(const std::string& executable_path,
             const std::vector<std::string>& args) override {
    (void)executable_path;
    (void)args;
    launch_count_.fetch_add(1, std::memory_order_relaxed);
    if (fail_launch_) {
      return false;
    }
    alive_.store(true, std::memory_order_relaxed);
    return true;
  }

  bool IsAlive() const override {
    return alive_.load(std::memory_order_relaxed);
  }

  void Terminate() override {
    terminate_count_.fetch_add(1, std::memory_order_relaxed);
    alive_.store(false, std::memory_order_relaxed);
  }

  void set_fail_launch(bool fail) { fail_launch_ = fail; }
  void set_alive(bool alive) { alive_.store(alive, std::memory_order_relaxed); }
  int launch_count() const {
    return launch_count_.load(std::memory_order_relaxed);
  }
  int terminate_count() const {
    return terminate_count_.load(std::memory_order_relaxed);
  }

 private:
  bool fail_launch_ = false;
  std::atomic<bool> alive_{false};
  std::atomic<int> launch_count_{0};
  std::atomic<int> terminate_count_{0};
};

CloudMorphBackend::Options MakeTestOptions() {
  CloudMorphBackend::Options options;
  options.executable_path = "cloudmorph_fake.exe";
  options.monitor_interval_ms = 2;  // Fast polling to keep tests quick.
  return options;
}

template <typename Predicate>
bool WaitUntil(Predicate predicate, int timeout_ms = 500) {
  const int step_ms = 2;
  int waited = 0;
  while (!predicate()) {
    if (waited >= timeout_ms) {
      return false;
    }
    std::this_thread::sleep_for(std::chrono::milliseconds(step_ms));
    waited += step_ms;
  }
  return true;
}

}  // namespace

TEST_CASE("CloudMorphBackend Start() launches the process and reports Running") {
  auto launcher = std::make_unique<MockProcessLauncher>();
  auto* launcher_ptr = launcher.get();
  CloudMorphBackend backend(MakeTestOptions(), std::move(launcher));

  REQUIRE(backend.health() == StreamHealth::kDisconnected);
  REQUIRE(backend.Start());
  REQUIRE(backend.health() == StreamHealth::kRunning);
  REQUIRE(launcher_ptr->launch_count() == 1);
  REQUIRE(backend.is_process_alive());
  REQUIRE_FALSE(backend.is_webrtc_connected());

  backend.Stop();
}

TEST_CASE("CloudMorphBackend Stop() terminates the process and reports Disconnected") {
  auto launcher = std::make_unique<MockProcessLauncher>();
  auto* launcher_ptr = launcher.get();
  CloudMorphBackend backend(MakeTestOptions(), std::move(launcher));

  REQUIRE(backend.Start());
  backend.Stop();

  REQUIRE(backend.health() == StreamHealth::kDisconnected);
  REQUIRE(launcher_ptr->terminate_count() >= 1);
  REQUIRE_FALSE(backend.is_process_alive());
}

TEST_CASE("CloudMorphBackend Start() reports Failed when launch fails") {
  auto launcher = std::make_unique<MockProcessLauncher>();
  launcher->set_fail_launch(true);
  CloudMorphBackend backend(MakeTestOptions(), std::move(launcher));

  REQUIRE_FALSE(backend.Start());
  REQUIRE(backend.health() == StreamHealth::kFailed);
  REQUIRE_FALSE(backend.is_process_alive());

  // Stop() after a failed start must remain safe and must not mask the
  // Failed status with Disconnected.
  backend.Stop();
  REQUIRE(backend.health() == StreamHealth::kFailed);
}

TEST_CASE("CloudMorphBackend detects the process dying and reports Failed") {
  auto launcher = std::make_unique<MockProcessLauncher>();
  auto* launcher_ptr = launcher.get();
  CloudMorphBackend backend(MakeTestOptions(), std::move(launcher));

  REQUIRE(backend.Start());
  REQUIRE(backend.health() == StreamHealth::kRunning);

  // Simulate an external crash: the process is no longer alive, but Stop()
  // was never called.
  launcher_ptr->set_alive(false);

  REQUIRE(WaitUntil(
      [&]() { return backend.health() == StreamHealth::kFailed; }));

  backend.Stop();
}

TEST_CASE("CloudMorphBackend forwards video frames preserving order and timestamps") {
  auto launcher = std::make_unique<MockProcessLauncher>();
  CloudMorphBackend backend(MakeTestOptions(), std::move(launcher));
  REQUIRE(backend.Start());

  xe::netbox::VideoFrame frame_a;
  frame_a.frame_id = 1;
  frame_a.timestamp_us = 1000;
  xe::netbox::VideoFrame frame_b;
  frame_b.frame_id = 2;
  frame_b.timestamp_us = 2000;

  backend.SubmitVideoFrame(frame_a);
  backend.SubmitVideoFrame(frame_b);

  REQUIRE(WaitUntil(
      [&]() { return backend.video_frames_processed() >= 2; }));
  REQUIRE(backend.dropped_video_frame_count() == 0);

  backend.Stop();
}

TEST_CASE("CloudMorphBackend drops oldest video frames on queue overflow") {
  auto launcher = std::make_unique<MockProcessLauncher>();
  CloudMorphBackend::Options options = MakeTestOptions();
  // Slow the monitor thread way down so frames pile up before being
  // drained, letting us deterministically observe an overflow drop.
  options.monitor_interval_ms = 1000;
  options.max_queued_video_frames = 2;
  CloudMorphBackend backend(options, std::move(launcher));
  REQUIRE(backend.Start());

  for (int i = 0; i < 5; ++i) {
    xe::netbox::VideoFrame frame;
    frame.frame_id = static_cast<uint64_t>(i);
    backend.SubmitVideoFrame(frame);
  }

  REQUIRE(backend.dropped_video_frame_count() == 3);

  backend.Stop();
}

TEST_CASE("CloudMorphBackend forwards audio packets in order") {
  auto launcher = std::make_unique<MockProcessLauncher>();
  CloudMorphBackend backend(MakeTestOptions(), std::move(launcher));
  REQUIRE(backend.Start());

  xe::netbox::AudioPacket packet_a;
  packet_a.timestamp_us = 500;
  xe::netbox::AudioPacket packet_b;
  packet_b.timestamp_us = 1500;

  backend.SubmitAudioPacket(packet_a);
  backend.SubmitAudioPacket(packet_b);

  REQUIRE(WaitUntil(
      [&]() { return backend.audio_packets_processed() >= 2; }));
  REQUIRE(backend.dropped_audio_packet_count() == 0);

  backend.Stop();
}

TEST_CASE("CloudMorphBackend Start()/Stop() are idempotent and repeatable") {
  auto launcher = std::make_unique<MockProcessLauncher>();
  auto* launcher_ptr = launcher.get();
  CloudMorphBackend backend(MakeTestOptions(), std::move(launcher));

  REQUIRE(backend.Start());
  REQUIRE(backend.Start());  // Second call while running: no-op, still true.
  REQUIRE(launcher_ptr->launch_count() == 1);

  backend.Stop();
  backend.Stop();  // Second stop: safe no-op.
  REQUIRE(launcher_ptr->terminate_count() >= 1);

  // Restart after a clean stop must work again.
  REQUIRE(backend.Start());
  REQUIRE(launcher_ptr->launch_count() == 2);
  backend.Stop();
}

}  // namespace test
}  // namespace netbox_streaming
