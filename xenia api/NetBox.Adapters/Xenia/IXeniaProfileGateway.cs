using NetBox.Models;

namespace NetBox.Adapters.Xenia;

public interface IXeniaProfileGateway
{
  Task<NetBoxXeniaProfileDto> CreateProfileAsync(string gamertag, CancellationToken cancellationToken = default);
  Task<NetBoxXeniaProfileDto?> GetProfileAsync(string profileId, CancellationToken cancellationToken = default);
  Task<IReadOnlyList<NetBoxAchievementDto>> GetAchievementsAsync(string profileId, CancellationToken cancellationToken = default);
}
