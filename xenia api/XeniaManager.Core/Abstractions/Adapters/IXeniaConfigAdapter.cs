using XeniaManager.Models;

namespace XeniaManager.Core.Abstractions.Adapters;

public interface IXeniaConfigAdapter
{
  Task<EmulatorConfigDto> GetConfigAsync(CancellationToken cancellationToken = default);
  Task<EmulatorConfigDto> SaveConfigAsync(UpdateConfigRequest request, CancellationToken cancellationToken = default);
}
