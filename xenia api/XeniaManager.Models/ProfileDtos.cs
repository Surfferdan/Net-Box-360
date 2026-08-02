namespace XeniaManager.Models;

public sealed record RecentGameDto(string TitleId, string Name, DateTimeOffset LastPlayedAt);

public sealed record ProfileDto(
  string Id,
  string Gamertag,
  int Gamerscore,
  string? AvatarPath,
  bool IsActive,
  IReadOnlyList<RecentGameDto> RecentGames);

public sealed record CreateProfileRequest(string Gamertag);

public sealed record UpdateProfileRequest(string? Gamertag, bool? IsActive);
