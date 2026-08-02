using NetBox.Adapters.Xenia;
using NetBox.Models;

namespace NetBox.Tests;

public sealed class TestXeniaProfileGateway : IXeniaProfileGateway
{
  private long nextId = 1;

  public Task<NetBoxXeniaProfileDto> CreateProfileAsync(string gamertag, CancellationToken cancellationToken = default)
  {
    var profile = new NetBoxXeniaProfileDto($"profile-{nextId++}", gamertag, 0, null, Array.Empty<string>(), Array.Empty<NetBoxAchievementDto>());
    return Task.FromResult(profile);
  }

  public Task<NetBoxXeniaProfileDto?> GetProfileAsync(string profileId, CancellationToken cancellationToken = default)
  {
    var profile = new NetBoxXeniaProfileDto(profileId, "TestUser", 0, null, Array.Empty<string>(), Array.Empty<NetBoxAchievementDto>());
    return Task.FromResult<NetBoxXeniaProfileDto?>(profile);
  }

  public Task<IReadOnlyList<NetBoxAchievementDto>> GetAchievementsAsync(string profileId, CancellationToken cancellationToken = default)
    => Task.FromResult<IReadOnlyList<NetBoxAchievementDto>>(Array.Empty<NetBoxAchievementDto>());
}
