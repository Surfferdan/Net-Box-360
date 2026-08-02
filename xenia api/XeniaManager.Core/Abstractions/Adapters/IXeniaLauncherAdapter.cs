using XeniaManager.Models;

namespace XeniaManager.Core.Abstractions.Adapters;

public interface IXeniaLauncherAdapter
{
  Task<LauncherStatusDto> StartAsync(LauncherStartRequest request, CancellationToken cancellationToken = default);
  Task<LauncherStatusDto> StopAsync(CancellationToken cancellationToken = default);
  Task<LauncherStatusDto> GetStatusAsync(CancellationToken cancellationToken = default);
}
