using System.Collections.Generic;
using System.Linq;
using NetBox.Data.Repositories;
using NetBox.Models;

namespace NetBox.Tests;

public sealed class TestNetBoxRepository : INetBoxRepository
{
  private readonly Dictionary<long, UserRecordDto> usersById = new();
  private readonly Dictionary<string, UserRecordDto> usersByUsername = new(StringComparer.OrdinalIgnoreCase);
  private readonly Dictionary<string, SessionRecordDto> sessionsByToken = new(StringComparer.OrdinalIgnoreCase);
  private readonly Dictionary<string, GameSessionRecordDto> gameSessionsById = new(StringComparer.OrdinalIgnoreCase);
  private readonly Dictionary<long, UserSettingsDto> settingsByUserId = new();
  private readonly Dictionary<string, List<long>> playersBySession = new(StringComparer.OrdinalIgnoreCase);
  private readonly Dictionary<string, List<GameSessionPlayerRecordDto>> playerRecordsBySession = new(StringComparer.OrdinalIgnoreCase);
  private readonly Dictionary<string, GameCatalogEntryDto> gameCatalogById = new(StringComparer.OrdinalIgnoreCase);
  private readonly Dictionary<long, ProfileCustomizationDto> customizationsByUserId = new();
  private readonly List<ChatMessageRecordDto> chatMessages = new();
  private readonly List<FriendLinkRecordDto> friendLinks = new();
  private long nextUserId = 1;
  private long nextSessionId = 1;
  private long nextChatMessageId = 1;

  public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

  public Task<IReadOnlyList<UserRecordDto>> ListUsersAsync(CancellationToken cancellationToken = default)
    => Task.FromResult<IReadOnlyList<UserRecordDto>>(usersById.Values.OrderBy(u => u.Username, StringComparer.OrdinalIgnoreCase).ToList());

  public Task<UserRecordDto?> GetUserByIdAsync(long userId, CancellationToken cancellationToken = default)
    => Task.FromResult(usersById.TryGetValue(userId, out var user) ? user : null);

  public Task<UserRecordDto?> GetUserByUsernameAsync(string username, CancellationToken cancellationToken = default)
    => Task.FromResult(usersByUsername.TryGetValue(username, out var user) ? user : null);

  public Task<long> CreateUserAsync(string username, string? email, string passwordHash, string xeniaProfileId, DateTimeOffset createdAt, CancellationToken cancellationToken = default)
  {
    var user = new UserRecordDto(nextUserId++, username, email, passwordHash, xeniaProfileId, createdAt, null);
    usersById[user.Id] = user;
    usersByUsername[user.Username] = user;
    return Task.FromResult(user.Id);
  }

  public Task UpdateLastLoginAsync(long userId, DateTimeOffset lastLogin, CancellationToken cancellationToken = default)
  {
    if (usersById.TryGetValue(userId, out var user))
    {
      usersById[userId] = user with { LastLogin = lastLogin };
      usersByUsername[user.Username] = usersById[userId];
    }

    return Task.CompletedTask;
  }

  public Task UpdateXeniaProfileIdAsync(long userId, string xeniaProfileId, CancellationToken cancellationToken = default)
  {
    if (usersById.TryGetValue(userId, out var user))
    {
      usersById[userId] = user with { XeniaProfileId = xeniaProfileId };
      usersByUsername[user.Username] = usersById[userId];
    }

    return Task.CompletedTask;
  }

  public Task<UserSettingsDto?> GetSettingsAsync(long userId, CancellationToken cancellationToken = default)
    => Task.FromResult(settingsByUserId.TryGetValue(userId, out var settings) ? settings : null);

  public Task<long> UpsertSettingsAsync(long userId, string? avatar, string theme, string controllerPreference, string language, CancellationToken cancellationToken = default)
  {
    settingsByUserId[userId] = new UserSettingsDto(nextSessionId++, userId, avatar, theme, controllerPreference, language);
    return Task.FromResult(settingsByUserId[userId].Id);
  }

  public Task<ProfileCustomizationDto?> GetProfileCustomizationAsync(long userId, CancellationToken cancellationToken = default)
    => Task.FromResult(customizationsByUserId.TryGetValue(userId, out var customization) ? customization : null);

  public Task UpsertProfileCustomizationAsync(long userId, string displayName, string motto, string cardStyle, string? avatarDataUrl, CancellationToken cancellationToken = default)
  {
    customizationsByUserId[userId] = new ProfileCustomizationDto(displayName, motto, cardStyle, avatarDataUrl);
    return Task.CompletedTask;
  }

  public Task<SessionRecordDto?> GetSessionByTokenAsync(string token, CancellationToken cancellationToken = default)
    => Task.FromResult(sessionsByToken.TryGetValue(token, out var session) ? session : null);

  public Task<long> CreateSessionAsync(long userId, string token, DateTimeOffset createdAt, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
  {
    var session = new SessionRecordDto(nextSessionId++, userId, token, createdAt, expiresAt);
    sessionsByToken[token] = session;
    return Task.FromResult(session.Id);
  }

  public Task RevokeSessionAsync(string token, CancellationToken cancellationToken = default)
  {
    sessionsByToken.Remove(token);
    return Task.CompletedTask;
  }

  public Task<SessionRecordDto?> RefreshSessionAsync(string currentToken, string newToken, DateTimeOffset createdAt, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
  {
    if (!sessionsByToken.TryGetValue(currentToken, out var existing))
    {
      return Task.FromResult<SessionRecordDto?>(null);
    }

    sessionsByToken.Remove(currentToken);
    var refreshed = new SessionRecordDto(nextSessionId++, existing.UserId, newToken, createdAt, expiresAt);
    sessionsByToken[newToken] = refreshed;
    return Task.FromResult<SessionRecordDto?>(refreshed);
  }

  public Task<long> CreateGameSessionAsync(string sessionId, long userId, string gameId, string gameTitle, string launchPath, string status, DateTimeOffset createdAt, CancellationToken cancellationToken = default)
  {
     gameSessionsById[sessionId] = new GameSessionRecordDto(nextSessionId++, sessionId, userId, gameId, gameTitle, launchPath, status, createdAt, null, null, null, null, null, null, null);
    return Task.FromResult(gameSessionsById[sessionId].Id);
  }

  public Task<GameSessionRecordDto?> GetGameSessionBySessionIdAsync(string sessionId, CancellationToken cancellationToken = default)
    => Task.FromResult(gameSessionsById.TryGetValue(sessionId, out var session) ? session : null);

  public Task<GameSessionRecordDto?> GetActiveGameSessionForUserAsync(long userId, CancellationToken cancellationToken = default)
  {
    var active = gameSessionsById.Values.Where(session => session.UserId == userId && new[] { "pending", "launching", "running", "stopping" }.Contains(session.Status)).OrderByDescending(session => session.CreatedAt).FirstOrDefault();
    return Task.FromResult(active);
  }

  public Task UpdateGameSessionAsync(string sessionId, string status, DateTimeOffset? startedAt, DateTimeOffset? stoppedAt, string? cloudMorphSessionId, string? streamUrl, string? lastError, string? virtualDisplayId = null, string? windowHandle = null, CancellationToken cancellationToken = default)
  {
    if (gameSessionsById.TryGetValue(sessionId, out var existing))
    {
      gameSessionsById[sessionId] = existing with { Status = status, StartedAt = startedAt, StoppedAt = stoppedAt, CloudMorphSessionId = cloudMorphSessionId, StreamUrl = streamUrl, LastError = lastError, VirtualDisplayId = virtualDisplayId, WindowHandle = windowHandle };
    }

    return Task.CompletedTask;
  }

  public Task<long> AddGameSessionPlayerAsync(string sessionId, long userId, int controllerSlot, DateTimeOffset joinedAt, CancellationToken cancellationToken = default)
  {
    if (!playersBySession.TryGetValue(sessionId, out var players))
    {
      players = new List<long>();
      playersBySession[sessionId] = players;
    }

    players.Add(userId);

    if (!playerRecordsBySession.TryGetValue(sessionId, out var records))
    {
      records = new List<GameSessionPlayerRecordDto>();
      playerRecordsBySession[sessionId] = records;
    }

    var id = records.Count + 1;
    records.Add(new GameSessionPlayerRecordDto(id, sessionId, userId, controllerSlot, joinedAt));

    return Task.FromResult((long)id);
  }

  public Task<bool> ClaimGameSessionSlotAsync(string sessionId, long userId, int controllerSlot, DateTimeOffset joinedAt, CancellationToken cancellationToken = default)
  {
    if (controllerSlot <= 0)
    {
      return Task.FromResult(false);
    }

    if (!playerRecordsBySession.TryGetValue(sessionId, out var records))
    {
      records = new List<GameSessionPlayerRecordDto>();
      playerRecordsBySession[sessionId] = records;
    }

    if (records.Any(record => record.ControllerSlot == controllerSlot))
    {
      return Task.FromResult(false);
    }

    var id = records.Count + 1;
    records.Add(new GameSessionPlayerRecordDto(id, sessionId, userId, controllerSlot, joinedAt));
    if (!playersBySession.TryGetValue(sessionId, out var players))
    {
      players = new List<long>();
      playersBySession[sessionId] = players;
    }

    if (!players.Contains(userId))
    {
      players.Add(userId);
    }

    return Task.FromResult(true);
  }

  public Task<bool> ReleaseGameSessionSlotAsync(string sessionId, int controllerSlot, CancellationToken cancellationToken = default)
  {
    if (!playerRecordsBySession.TryGetValue(sessionId, out var records))
    {
      return Task.FromResult(false);
    }

    var removed = records.RemoveAll(record => record.ControllerSlot == controllerSlot);
    return Task.FromResult(removed > 0);
  }

  public Task<GameSessionPlayerRecordDto?> GetGameSessionSlotAssignmentAsync(string sessionId, int controllerSlot, CancellationToken cancellationToken = default)
  {
    if (!playerRecordsBySession.TryGetValue(sessionId, out var records))
    {
      return Task.FromResult<GameSessionPlayerRecordDto?>(null);
    }

    return Task.FromResult(records.FirstOrDefault(record => record.ControllerSlot == controllerSlot));
  }

  public Task<int> RemoveGameSessionPlayerAsync(string sessionId, long userId, CancellationToken cancellationToken = default)
  {
    var affected = 0;

    if (playersBySession.TryGetValue(sessionId, out var players))
    {
      var before = players.Count;
      players.RemoveAll(value => value == userId);
      affected = before - players.Count;
    }

    if (playerRecordsBySession.TryGetValue(sessionId, out var records))
    {
      _ = records.RemoveAll(record => record.UserId == userId);
    }

    return Task.FromResult(affected);
  }

  public Task<int> GetGameSessionPlayerCountAsync(string sessionId, CancellationToken cancellationToken = default)
  {
    return Task.FromResult(playersBySession.TryGetValue(sessionId, out var players) ? players.Count : 0);
  }

  public Task<IReadOnlyList<GameSessionPlayerRecordDto>> ListGameSessionPlayersAsync(string sessionId, CancellationToken cancellationToken = default)
  {
    if (!playerRecordsBySession.TryGetValue(sessionId, out var records))
    {
      return Task.FromResult<IReadOnlyList<GameSessionPlayerRecordDto>>(Array.Empty<GameSessionPlayerRecordDto>());
    }

    return Task.FromResult<IReadOnlyList<GameSessionPlayerRecordDto>>(records
      .OrderBy(record => record.ControllerSlot)
      .ToArray());
  }

  public Task<long> AddChatMessageAsync(long senderUserId, long? recipientUserId, string message, DateTimeOffset createdAt, CancellationToken cancellationToken = default)
  {
    var record = new ChatMessageRecordDto(nextChatMessageId++, senderUserId, recipientUserId, message, createdAt);
    chatMessages.Add(record);
    return Task.FromResult(record.Id);
  }

  public Task<IReadOnlyList<ChatMessageRecordDto>> ListChatMessagesAsync(long userId, int limit = 50, CancellationToken cancellationToken = default)
  {
    var rows = chatMessages
      .Where(message => message.RecipientUserId is null || message.SenderUserId == userId || message.RecipientUserId == userId)
      .OrderByDescending(message => message.CreatedAt)
      .Take(limit)
      .ToArray();
    return Task.FromResult<IReadOnlyList<ChatMessageRecordDto>>(rows);
  }

  public Task<IReadOnlyList<FriendLinkRecordDto>> ListFriendLinksAsync(long userId, CancellationToken cancellationToken = default)
  {
    var rows = friendLinks
      .Where(link => link.UserAId == userId || link.UserBId == userId)
      .OrderByDescending(link => link.CreatedAt)
      .ToArray();
    return Task.FromResult<IReadOnlyList<FriendLinkRecordDto>>(rows);
  }

  public Task<bool> AreFriendsAsync(long userId, long otherUserId, CancellationToken cancellationToken = default)
  {
    if (userId == otherUserId)
    {
      return Task.FromResult(false);
    }

    var (userAId, userBId) = NormalizeFriendPair(userId, otherUserId);
    var exists = friendLinks.Any(link => link.UserAId == userAId && link.UserBId == userBId);
    return Task.FromResult(exists);
  }

  public Task AddFriendLinkAsync(long userId, long otherUserId, DateTimeOffset createdAt, CancellationToken cancellationToken = default)
  {
    if (userId == otherUserId)
    {
      return Task.CompletedTask;
    }

    var (userAId, userBId) = NormalizeFriendPair(userId, otherUserId);
    var exists = friendLinks.Any(link => link.UserAId == userAId && link.UserBId == userBId);
    if (!exists)
    {
      friendLinks.Add(new FriendLinkRecordDto(userAId, userBId, createdAt));
    }

    return Task.CompletedTask;
  }

  public Task RemoveFriendLinkAsync(long userId, long otherUserId, CancellationToken cancellationToken = default)
  {
    if (userId == otherUserId)
    {
      return Task.CompletedTask;
    }

    var (userAId, userBId) = NormalizeFriendPair(userId, otherUserId);
    friendLinks.RemoveAll(link => link.UserAId == userAId && link.UserBId == userBId);
    return Task.CompletedTask;
  }

  public Task<IReadOnlyList<GameCatalogEntryDto>> ListGameCatalogAsync(CancellationToken cancellationToken = default)
    => Task.FromResult<IReadOnlyList<GameCatalogEntryDto>>(gameCatalogById.Values.OrderBy(entry => entry.Title, StringComparer.OrdinalIgnoreCase).ToList());

  public Task<GameCatalogEntryDto?> GetGameCatalogEntryAsync(string id, CancellationToken cancellationToken = default)
    => Task.FromResult(gameCatalogById.TryGetValue(id, out var entry) ? entry : null);

  public Task UpsertGameCatalogEntryAsync(GameCatalogEntryDto entry, CancellationToken cancellationToken = default)
  {
    gameCatalogById[entry.Id] = entry;
    return Task.CompletedTask;
  }

  public Task UpdateLastPlayedAsync(string id, DateTimeOffset lastPlayedAt, CancellationToken cancellationToken = default)
  {
    if (gameCatalogById.TryGetValue(id, out var entry))
    {
      gameCatalogById[id] = entry with { LastPlayedAt = lastPlayedAt };
    }

    return Task.CompletedTask;
  }

  private static (long UserAId, long UserBId) NormalizeFriendPair(long firstUserId, long secondUserId)
    => firstUserId < secondUserId ? (firstUserId, secondUserId) : (secondUserId, firstUserId);
}
