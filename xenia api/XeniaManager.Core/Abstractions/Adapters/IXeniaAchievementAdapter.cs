using XeniaManager.Models;

namespace XeniaManager.Core.Abstractions.Adapters;

public interface IXeniaAchievementAdapter
{
  Task<IReadOnlyList<AchievementDto>> GetAchievementsAsync(string profileId, CancellationToken cancellationToken = default);
  Task<IReadOnlyList<AchievementDto>> GetUnlockedAchievementsAsync(string profileId, CancellationToken cancellationToken = default);
}
