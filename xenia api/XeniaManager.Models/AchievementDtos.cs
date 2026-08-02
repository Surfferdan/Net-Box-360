namespace XeniaManager.Models;

public sealed record AchievementDto(
  string Id,
  string TitleId,
  string Name,
  string Description,
  int Gamerscore,
  bool IsUnlocked,
  DateTimeOffset? UnlockedAt,
  double? ProgressPercent);

public sealed record AchievementSummaryDto(
  string ProfileId,
  int Total,
  int Unlocked,
  IReadOnlyList<AchievementDto> Achievements);
