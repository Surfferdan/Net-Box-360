namespace NetBox.Models;

public sealed record NetBoxAchievementDto(
  string Id,
  string Name,
  string Description,
  int Gamerscore,
  bool IsUnlocked,
  DateTimeOffset? UnlockedAt,
  double? ProgressPercent);

public sealed record NetBoxXeniaProfileDto(
  string Id,
  string Gamertag,
  int Gamerscore,
  string? Avatar,
  IReadOnlyList<string> RecentGames,
  IReadOnlyList<NetBoxAchievementDto> Achievements);

public sealed record NetBoxUserProfileDto(
  long UserId,
  string Username,
  string? Email,
  string? Avatar,
  string Theme,
  string ControllerPreference,
  string Language);

public sealed record ProfileCustomizationDto(
  string DisplayName,
  string Motto,
  string CardStyle,
  string? AvatarDataUrl);

public sealed record UpdateProfileCustomizationRequest(
  string DisplayName,
  string Motto,
  string CardStyle,
  string? AvatarDataUrl);

public sealed record CombinedProfileDto(
  string Username,
  string DisplayName,
  string? Avatar,
  string Motto,
  string CardStyle,
  int Gamerscore,
  IReadOnlyList<string> RecentGames,
  IReadOnlyList<NetBoxAchievementDto> Achievements,
  NetBoxUserProfileDto Settings,
  ProfileCustomizationDto Customization);
