#ifndef NETBOX_STREAMING_CLOUDMORPH_PROCESS_H_
#define NETBOX_STREAMING_CLOUDMORPH_PROCESS_H_

#include "netbox_streaming/process_launcher.h"

#if defined(_WIN32)
// Avoid pulling in the full Windows.h surface in the header; the .cc file
// includes it directly. Only an opaque handle is stored here.
using HANDLE = void*;
#endif

namespace netbox_streaming {

// Real, external-process implementation of IProcessLauncher: launches the
// CloudMorph executable as a separate OS process and monitors/terminates it
// via standard process APIs. This is the ONLY place in this project that
// touches platform process-management APIs - CloudMorph's own source is
// never embedded or linked here, only its compiled executable is spawned
// at runtime, matching the "CloudMorph runs externally" requirement.
class CloudMorphProcess : public IProcessLauncher {
 public:
  CloudMorphProcess();
  ~CloudMorphProcess() override;

  CloudMorphProcess(const CloudMorphProcess&) = delete;
  CloudMorphProcess& operator=(const CloudMorphProcess&) = delete;

  bool Launch(const std::string& executable_path,
             const std::vector<std::string>& args) override;
  bool IsAlive() const override;
  void Terminate() override;

 private:
#if defined(_WIN32)
  HANDLE process_handle_ = nullptr;
#else
  int pid_ = -1;
#endif
};

}  // namespace netbox_streaming

#endif  // NETBOX_STREAMING_CLOUDMORPH_PROCESS_H_
