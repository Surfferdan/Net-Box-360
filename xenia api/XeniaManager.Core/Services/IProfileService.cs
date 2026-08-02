using XeniaManager.Models;

namespace XeniaManager.Core.Services;

public interface IProfileService
{
  Task<IReadOnlyList<ProfileDto>> GetProfilesAsync(CancellationToken cancellationToken = default);
  Task<ProfileDto?> GetProfileAsync(string id, CancellationToken cancellationToken = default);
  Task<ProfileDto> CreateProfileAsync(CreateProfileRequest request, CancellationToken cancellationToken = default);
  Task<ProfileDto?> UpdateProfileAsync(string id, UpdateProfileRequest request, CancellationToken cancellationToken = default);
  Task<bool> DeleteProfileAsync(string id, CancellationToken cancellationToken = default);
}
