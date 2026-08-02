using XeniaManager.Models;

namespace XeniaManager.Core.Services;

public interface IConfigService
{
  Task<EmulatorConfigDto> GetConfigAsync(CancellationToken cancellationToken = default);
  Task<EmulatorConfigDto> UpdateConfigAsync(UpdateConfigRequest request, CancellationToken cancellationToken = default);
}
