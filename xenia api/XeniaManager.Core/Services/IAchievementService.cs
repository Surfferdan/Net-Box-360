using XeniaManager.Models;

namespace XeniaManager.Core.Services;

public interface IAchievementService
{
  Task<AchievementSummaryDto> GetAchievementsAsync(string profileId, CancellationToken cancellationToken = default);
}
