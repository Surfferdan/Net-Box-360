using XeniaManager.Core.Abstractions.Adapters;
using XeniaManager.Models;

namespace XeniaManager.Core.Services;

public sealed class AchievementService : IAchievementService
{
  private readonly IXeniaAchievementAdapter achievementAdapter;

  public AchievementService(IXeniaAchievementAdapter achievementAdapter)
  {
    this.achievementAdapter = achievementAdapter;
  }

  public async Task<AchievementSummaryDto> GetAchievementsAsync(string profileId, CancellationToken cancellationToken = default)
  {
    var all = await achievementAdapter.GetAchievementsAsync(profileId, cancellationToken).ConfigureAwait(false);
    var unlocked = all.Count(a => a.IsUnlocked);
    return new AchievementSummaryDto(profileId, all.Count, unlocked, all);
  }
}
