using XeniaManager.Models;

namespace XeniaManager.Core.Services;

public interface ILauncherService
{
  Task<LauncherStatusDto> StartAsync(LauncherStartRequest request, CancellationToken cancellationToken = default);
  Task<LauncherStatusDto> StopAsync(CancellationToken cancellationToken = default);
  Task<LauncherStatusDto> StatusAsync(CancellationToken cancellationToken = default);
}
