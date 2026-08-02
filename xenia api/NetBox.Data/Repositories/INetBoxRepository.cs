using NetBox.Models;

namespace NetBox.Data.Repositories;

public interface INetBoxRepository
{
  Task InitializeAsync(CancellationToken cancellationToken = default);
  Task<IReadOnlyList<UserRecordDto>> ListUsersAsync(CancellationToken cancellationToken = default);
  Task<UserRecordDto?> GetUserByIdAsync(long userId, CancellationToken cancellationToken = default);
  Task<UserRecordDto?> GetUserByUsernameAsync(string username, CancellationToken cancellationToken = default);
  Task<long> CreateUserAsync(string username, string? email, string passwordHash, string xeniaProfileId, DateTimeOffset createdAt, CancellationToken cancellationToken = default);
  Task UpdateLastLoginAsync(long userId, DateTimeOffset lastLogin, CancellationToken cancellationToken = default);
  Task UpdateXeniaProfileIdAsync(long userId, string xeniaProfileId, CancellationToken cancellationToken = default);
  Task<UserSettingsDto?> GetSettingsAsync(long userId, CancellationToken cancellationToken = default);
  Task<long> UpsertSettingsAsync(long userId, string? avatar, string theme, string controllerPreference, string language, CancellationToken cancellationToken = default);
  Task<ProfileCustomizationDto?> GetProfileCustomizationAsync(long userId, CancellationToken cancellationToken = default);
  Task UpsertProfileCustomizationAsync(long userId, string displayName, string motto, string cardStyle, string? avatarDataUrl, CancellationToken cancellationToken = default);
  Task<SessionRecordDto?> GetSessionByTokenAsync(string token, CancellationToken cancellationToken = default);
  Task<long> CreateSessionAsync(long userId, string token, DateTimeOffset createdAt, DateTimeOffset expiresAt, CancellationToken cancellationToken = default);
  Task RevokeSessionAsync(string token, CancellationToken cancellationToken = default);
  Task<SessionRecordDto?> RefreshSessionAsync(string currentToken, string newToken, DateTimeOffset createdAt, DateTimeOffset expiresAt, CancellationToken cancellationToken = default);
  Task<long> CreateGameSessionAsync(
    string sessionId,
    long userId,
    string gameId,
    string gameTitle,
    string launchPath,
    string status,
    DateTimeOffset createdAt,
    CancellationToken cancellationToken = default);
  Task<GameSessionRecordDto?> GetGameSessionBySessionIdAsync(string sessionId, CancellationToken cancellationToken = default);
  Task<GameSessionRecordDto?> GetActiveGameSessionForUserAsync(long userId, CancellationToken cancellationToken = default);
  Task UpdateGameSessionAsync(
    string sessionId,
    string status,
    DateTimeOffset? startedAt,
    DateTimeOffset? stoppedAt,
    string? cloudMorphSessionId,
    string? streamUrl,
    string? lastError,
    string? virtualDisplayId = null,
    string? windowHandle = null,
    CancellationToken cancellationToken = default);
  Task<long> AddGameSessionPlayerAsync(string sessionId, long userId, int controllerSlot, DateTimeOffset joinedAt, CancellationToken cancellationToken = default);
  Task<bool> ClaimGameSessionSlotAsync(string sessionId, long userId, int controllerSlot, DateTimeOffset joinedAt, CancellationToken cancellationToken = default);
  Task<bool> ReleaseGameSessionSlotAsync(string sessionId, int controllerSlot, CancellationToken cancellationToken = default);
  Task<GameSessionPlayerRecordDto?> GetGameSessionSlotAssignmentAsync(string sessionId, int controllerSlot, CancellationToken cancellationToken = default);
  Task<int> RemoveGameSessionPlayerAsync(string sessionId, long userId, CancellationToken cancellationToken = default);
  Task<int> GetGameSessionPlayerCountAsync(string sessionId, CancellationToken cancellationToken = default);
  Task<IReadOnlyList<GameSessionPlayerRecordDto>> ListGameSessionPlayersAsync(string sessionId, CancellationToken cancellationToken = default);
  Task<long> AddChatMessageAsync(long senderUserId, long? recipientUserId, string message, DateTimeOffset createdAt, CancellationToken cancellationToken = default);
  Task<IReadOnlyList<ChatMessageRecordDto>> ListChatMessagesAsync(long userId, int limit = 50, CancellationToken cancellationToken = default);
  Task<IReadOnlyList<FriendLinkRecordDto>> ListFriendLinksAsync(long userId, CancellationToken cancellationToken = default);
  Task<bool> AreFriendsAsync(long userId, long otherUserId, CancellationToken cancellationToken = default);
  Task AddFriendLinkAsync(long userId, long otherUserId, DateTimeOffset createdAt, CancellationToken cancellationToken = default);
  Task RemoveFriendLinkAsync(long userId, long otherUserId, CancellationToken cancellationToken = default);
  Task<IReadOnlyList<GameCatalogEntryDto>> ListGameCatalogAsync(CancellationToken cancellationToken = default);
  Task<GameCatalogEntryDto?> GetGameCatalogEntryAsync(string id, CancellationToken cancellationToken = default);
  Task UpsertGameCatalogEntryAsync(GameCatalogEntryDto entry, CancellationToken cancellationToken = default);
  Task UpdateLastPlayedAsync(string id, DateTimeOffset lastPlayedAt, CancellationToken cancellationToken = default);
}
