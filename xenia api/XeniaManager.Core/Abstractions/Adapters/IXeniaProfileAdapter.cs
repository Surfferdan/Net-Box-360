using XeniaManager.Models;

namespace XeniaManager.Core.Abstractions.Adapters;

public interface IXeniaProfileAdapter
{
  Task<IReadOnlyList<ProfileDto>> GetProfilesAsync(CancellationToken cancellationToken = default);
  Task<ProfileDto?> GetProfileAsync(string profileId, CancellationToken cancellationToken = default);
  Task<ProfileDto> CreateProfileAsync(CreateProfileRequest request, CancellationToken cancellationToken = default);
  Task<ProfileDto?> RenameProfileAsync(string profileId, string gamertag, CancellationToken cancellationToken = default);
  Task<bool> DeleteProfileAsync(string profileId, CancellationToken cancellationToken = default);
  Task<bool> SetActiveProfileAsync(string profileId, CancellationToken cancellationToken = default);
}
