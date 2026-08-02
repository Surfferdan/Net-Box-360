#include "netbox_streaming/cloudmorph_process.h"

#if defined(_WIN32)
#include <windows.h>
#else
#include <signal.h>
#include <sys/wait.h>
#include <unistd.h>
#endif

#include <sstream>

namespace netbox_streaming {

namespace {
std::string BuildCommandLine(const std::string& executable_path,
                             const std::vector<std::string>& args) {
  std::ostringstream out;
  out << "\"" << executable_path << "\"";
  for (const auto& arg : args) {
    out << " \"" << arg << "\"";
  }
  return out.str();
}
}  // namespace

CloudMorphProcess::CloudMorphProcess() = default;

CloudMorphProcess::~CloudMorphProcess() { Terminate(); }

bool CloudMorphProcess::Launch(const std::string& executable_path,
                               const std::vector<std::string>& args) {
  Terminate();

#if defined(_WIN32)
  std::string command_line = BuildCommandLine(executable_path, args);

  STARTUPINFOA startup_info = {};
  startup_info.cb = sizeof(startup_info);
  PROCESS_INFORMATION process_info = {};

  // CreateProcessA requires a mutable command line buffer.
  std::vector<char> mutable_command_line(command_line.begin(),
                                         command_line.end());
  mutable_command_line.push_back('\0');

  BOOL created = CreateProcessA(
      /*lpApplicationName=*/nullptr, mutable_command_line.data(),
      /*lpProcessAttributes=*/nullptr, /*lpThreadAttributes=*/nullptr,
      /*bInheritHandles=*/FALSE, /*dwCreationFlags=*/0,
      /*lpEnvironment=*/nullptr, /*lpCurrentDirectory=*/nullptr,
      &startup_info, &process_info);
  if (!created) {
    process_handle_ = nullptr;
    return false;
  }

  // The thread handle isn't needed for lifecycle tracking.
  CloseHandle(process_info.hThread);
  process_handle_ = process_info.hProcess;
  return true;
#else
  pid_t child = fork();
  if (child < 0) {
    pid_ = -1;
    return false;
  }
  if (child == 0) {
    std::vector<char*> argv;
    argv.push_back(const_cast<char*>(executable_path.c_str()));
    for (auto& arg : args) {
      argv.push_back(const_cast<char*>(arg.c_str()));
    }
    argv.push_back(nullptr);
    execv(executable_path.c_str(), argv.data());
    _exit(127);  // execv only returns on failure.
  }
  pid_ = child;
  return true;
#endif
}

bool CloudMorphProcess::IsAlive() const {
#if defined(_WIN32)
  if (!process_handle_) {
    return false;
  }
  DWORD exit_code = 0;
  if (!GetExitCodeProcess(process_handle_, &exit_code)) {
    return false;
  }
  return exit_code == STILL_ACTIVE;
#else
  if (pid_ <= 0) {
    return false;
  }
  int status = 0;
  pid_t result = waitpid(pid_, &status, WNOHANG);
  return result == 0;
#endif
}

void CloudMorphProcess::Terminate() {
#if defined(_WIN32)
  if (process_handle_) {
    TerminateProcess(process_handle_, /*uExitCode=*/0);
    CloseHandle(process_handle_);
    process_handle_ = nullptr;
  }
#else
  if (pid_ > 0) {
    kill(pid_, SIGTERM);
    int status = 0;
    waitpid(pid_, &status, 0);
    pid_ = -1;
  }
#endif
}

}  // namespace netbox_streaming
