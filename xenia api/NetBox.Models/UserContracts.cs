namespace NetBox.Models;

public sealed record UserRecordDto(
  long Id,
  string Username,
  string? Email,
  string PasswordHash,
  string XeniaProfileId,
  DateTimeOffset CreatedAt,
  DateTimeOffset? LastLogin);

public sealed record UserSettingsDto(
  long Id,
  long UserId,
  string? Avatar,
  string Theme,
  string ControllerPreference,
  string Language);

public sealed record SessionRecordDto(
  long Id,
  long UserId,
  string Token,
  DateTimeOffset CreatedAt,
  DateTimeOffset ExpiresAt);

public sealed record ChatMessageRecordDto(
  long Id,
  long SenderUserId,
  long? RecipientUserId,
  string Message,
  DateTimeOffset CreatedAt);

public sealed record FriendLinkRecordDto(
  long UserAId,
  long UserBId,
  DateTimeOffset CreatedAt);
