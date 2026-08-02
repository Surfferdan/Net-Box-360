#ifndef NETBOX_STREAMING_PROCESS_LAUNCHER_H_
#define NETBOX_STREAMING_PROCESS_LAUNCHER_H_

#include <string>
#include <vector>

namespace netbox_streaming {

// Thin abstraction over "launch an external executable, check if it's
// still alive, terminate it". CloudMorphBackend depends only on this
// interface, not on any concrete process-management code, so tests can
// inject a fake launcher and exercise CloudMorphBackend's lifecycle/queuing
// logic without spawning any real process or requiring CloudMorph to be
// installed.
class IProcessLauncher {
 public:
  virtual ~IProcessLauncher() = default;

  // Attempts to launch `executable_path` with `args`. Returns false if the
  // process could not be started (e.g. file not found, permission denied).
  // Must not block indefinitely - a failed launch should return promptly.
  virtual bool Launch(const std::string& executable_path,
                      const std::vector<std::string>& args) = 0;

  // Returns true if the previously launched process is still running.
  // Safe to call even if Launch() was never called or failed (returns
  // false in that case).
  virtual bool IsAlive() const = 0;

  // Terminates the process if running. Safe to call multiple times or if
  // the process already exited / was never launched.
  virtual void Terminate() = 0;
};

}  // namespace netbox_streaming

#endif  // NETBOX_STREAMING_PROCESS_LAUNCHER_H_
