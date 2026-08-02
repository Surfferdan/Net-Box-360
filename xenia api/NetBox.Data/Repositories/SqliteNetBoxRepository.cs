using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using NetBox.Models;

namespace NetBox.Data.Repositories;

public sealed class SqliteNetBoxRepository : INetBoxRepository
{
  private readonly NetBoxDatabaseOptions options;
  private readonly string databaseFilePath;

  public SqliteNetBoxRepository(IOptions<NetBoxDatabaseOptions> options, Microsoft.Extensions.Hosting.IHostEnvironment environment)
  {
    this.options = options.Value;
    databaseFilePath = ResolvePath(environment, this.options.DatabasePath);
  }

  public async Task InitializeAsync(CancellationToken cancellationToken = default)
  {
    Directory.CreateDirectory(Path.GetDirectoryName(databaseFilePath)!);
    await using var connection = CreateConnection();
    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

    using var command = connection.CreateCommand();
    command.CommandText = @"
PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS Users (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  Username TEXT NOT NULL UNIQUE,
  Email TEXT NULL,
  PasswordHash TEXT NOT NULL,
  XeniaProfileId TEXT NOT NULL,
  CreatedAt TEXT NOT NULL,
  LastLogin TEXT NULL
);

CREATE TABLE IF NOT EXISTS UserSettings (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  UserId INTEGER NOT NULL UNIQUE,
  Avatar TEXT NULL,
  Theme TEXT NOT NULL,
  ControllerPreference TEXT NOT NULL,
  Language TEXT NOT NULL,
  FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS ProfileCustomizations (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  UserId INTEGER NOT NULL UNIQUE,
  DisplayName TEXT NOT NULL,
  Motto TEXT NOT NULL,
  CardStyle TEXT NOT NULL,
  AvatarDataUrl TEXT NULL,
  FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS Sessions (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  UserId INTEGER NOT NULL,
  Token TEXT NOT NULL UNIQUE,
  CreatedAt TEXT NOT NULL,
  ExpiresAt TEXT NOT NULL,
  FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS GameSessions (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  SessionId TEXT NOT NULL UNIQUE,
  UserId INTEGER NOT NULL,
  GameId TEXT NOT NULL,
  GameTitle TEXT NOT NULL,
  LaunchPath TEXT NOT NULL,
  Status TEXT NOT NULL,
  CreatedAt TEXT NOT NULL,
  StartedAt TEXT NULL,
  StoppedAt TEXT NULL,
  CloudMorphSessionId TEXT NULL,
  StreamUrl TEXT NULL,
  LastError TEXT NULL,
  VirtualDisplayId TEXT NULL,
  WindowHandle TEXT NULL,
  FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS IX_GameSessions_User_Status ON GameSessions(UserId, Status);

CREATE TABLE IF NOT EXISTS SessionPlayers (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  SessionId TEXT NOT NULL,
  UserId INTEGER NOT NULL,
  ControllerSlot INTEGER NOT NULL,
  JoinedAt TEXT NOT NULL,
  FOREIGN KEY(SessionId) REFERENCES GameSessions(SessionId) ON DELETE CASCADE,
  FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE CASCADE,
  UNIQUE(SessionId, ControllerSlot),
  UNIQUE(SessionId, UserId)
);

CREATE TABLE IF NOT EXISTS ChatMessages (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  SenderUserId INTEGER NOT NULL,
  RecipientUserId INTEGER NULL,
  Message TEXT NOT NULL,
  CreatedAt TEXT NOT NULL,
  FOREIGN KEY(SenderUserId) REFERENCES Users(Id) ON DELETE CASCADE,
  FOREIGN KEY(RecipientUserId) REFERENCES Users(Id) ON DELETE SET NULL
);

CREATE INDEX IF NOT EXISTS IX_ChatMessages_CreatedAt ON ChatMessages(CreatedAt DESC);
CREATE INDEX IF NOT EXISTS IX_ChatMessages_RecipientUserId ON ChatMessages(RecipientUserId);

CREATE TABLE IF NOT EXISTS FriendLinks (
  UserAId INTEGER NOT NULL,
  UserBId INTEGER NOT NULL,
  CreatedAt TEXT NOT NULL,
  PRIMARY KEY (UserAId, UserBId),
  FOREIGN KEY(UserAId) REFERENCES Users(Id) ON DELETE CASCADE,
  FOREIGN KEY(UserBId) REFERENCES Users(Id) ON DELETE CASCADE,
  CHECK (UserAId < UserBId)
);

CREATE INDEX IF NOT EXISTS IX_FriendLinks_UserA ON FriendLinks(UserAId);
CREATE INDEX IF NOT EXISTS IX_FriendLinks_UserB ON FriendLinks(UserBId);

CREATE TABLE IF NOT EXISTS GameCatalog (
  Id TEXT PRIMARY KEY,
  TitleId TEXT NOT NULL,
  Title TEXT NOT NULL,
  RelativePath TEXT NOT NULL,
  FullPath TEXT NOT NULL,
  Extension TEXT NOT NULL,
  SizeBytes INTEGER NOT NULL,
  Genre TEXT NULL,
  Players INTEGER NULL,
  LastWriteTimeUtc TEXT NOT NULL,
  LastPlayedAt TEXT NULL,
  CoverPath TEXT NULL
);
";
    _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

    await EnsureGameSessionSchemaAsync(connection, cancellationToken).ConfigureAwait(false);
  }

  private static async Task EnsureGameSessionSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
  {
    var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    await using (var info = connection.CreateCommand())
    {
      info.CommandText = "PRAGMA table_info(GameSessions);";
      await using var reader = await info.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
      while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
      {
        if (!reader.IsDBNull(1))
        {
          columns.Add(reader.GetString(1));
        }
      }
    }

    if (!columns.Contains("VirtualDisplayId"))
    {
      await using var alter = connection.CreateCommand();
      alter.CommandText = "ALTER TABLE GameSessions ADD COLUMN VirtualDisplayId TEXT NULL;";
      _ = await alter.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    if (!columns.Contains("WindowHandle"))
    {
      await using var alter = connection.CreateCommand();
      alter.CommandText = "ALTER TABLE GameSessions ADD COLUMN WindowHandle TEXT NULL;";
      _ = await alter.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
  }

  public async Task<IReadOnlyList<UserRecordDto>> ListUsersAsync(CancellationToken cancellationToken = default)
  {
    const string sql = @"
SELECT Id, Username, Email, PasswordHash, XeniaProfileId, CreatedAt, LastLogin
FROM Users
ORDER BY
  CASE WHEN LastLogin IS NULL THEN 1 ELSE 0 END,
  LastLogin DESC,
  Username COLLATE NOCASE ASC;";

    await using var connection = CreateConnection();
    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

    var users = new List<UserRecordDto>();
    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
    {
      users.Add(new UserRecordDto(
        reader.GetInt64(0),
        reader.GetString(1),
        reader.IsDBNull(2) ? null : reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        DateTimeOffset.Parse(reader.GetString(5)),
        reader.IsDBNull(6) ? null : DateTimeOffset.Parse(reader.GetString(6))));
    }

    return users;
  }

  public async Task<UserRecordDto?> GetUserByIdAsync(long userId, CancellationToken cancellationToken = default)
  {
    const string sql = "SELECT Id, Username, Email, PasswordHash, XeniaProfileId, CreatedAt, LastLogin FROM Users WHERE Id = $id LIMIT 1;";
    await using var connection = CreateConnection();
    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.Parameters.AddWithValue("$id", userId);
    await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
    return await ReadUserAsync(reader, cancellationToken).ConfigureAwait(false);
  }

  public async Task<UserRecordDto?> GetUserByUsernameAsync(string username, CancellationToken cancellationToken = default)
  {
    const string sql = "SELECT Id, Username, Email, PasswordHash, XeniaProfileId, CreatedAt, LastLogin FROM Users WHERE Username = $username COLLATE NOCASE LIMIT 1;";
    await using var connection = CreateConnection();
    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.Parameters.AddWithValue("$username", username);
    await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
    return await ReadUserAsync(reader, cancellationToken).ConfigureAwait(false);
  }

  public async Task<long> CreateUserAsync(string username, string? email, string passwordHash, string xeniaProfileId, DateTimeOffset createdAt, CancellationToken cancellationToken = default)
  {
    const string sql = @"
INSERT INTO Users (Username, Email, PasswordHash, XeniaProfileId, CreatedAt)
VALUES ($username, $email, $passwordHash, $xeniaProfileId, $createdAt);
SELECT last_insert_rowid();";
    await using var connection = CreateConnection();
    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.Parameters.AddWithValue("$username", username);
    command.Parameters.AddWithValue("$email", (object?)email ?? DBNull.Value);
    command.Parameters.AddWithValue("$passwordHash", passwordHash);
    command.Parameters.AddWithValue("$xeniaProfileId", xeniaProfileId);
    command.Parameters.AddWithValue("$createdAt", createdAt.ToString("O"));
    var scalar = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    return Convert.ToInt64(scalar);
  }

  public async Task UpdateLastLoginAsync(long userId, DateTimeOffset lastLogin, CancellationToken cancellationToken = default)
  {
    await using var connection = CreateConnection();
    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
    await using var command = connection.CreateCommand();
    command.CommandText = "UPDATE Users SET LastLogin = $lastLogin WHERE Id = $id;";
    command.Parameters.AddWithValue("$lastLogin", lastLogin.ToString("O"));
    command.Parameters.AddWithValue("$id", userId);
    _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
  }

  public async Task UpdateXeniaProfileIdAsync(long userId, string xeniaProfileId, CancellationToken cancellationToken = default)
  {
    await using var connection = CreateConnection();
    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
    await using var command = connection.CreateCommand();
    command.CommandText = "UPDATE Users SET XeniaProfileId = $xeniaProfileId WHERE Id = $id;";
    command.Parameters.AddWithValue("$xeniaProfileId", xeniaProfileId);
    command.Parameters.AddWithValue("$id", userId);
    _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
  }

  public async Task<UserSettingsDto?> GetSettingsAsync(long userId, CancellationToken cancellationToken = default)
  {
    const string sql = "SELECT Id, UserId, Avatar, Theme, ControllerPreference, Language FROM UserSettings WHERE UserId = $userId LIMIT 1;";
    await using var connection = CreateConnection();
    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.Parameters.AddWithValue("$userId", userId);
    await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
    if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
    {
      return null;
    }

    return new UserSettingsDto(
      reader.GetInt64(0),
      reader.GetInt64(1),
      reader.IsDBNull(2) ? null : reader.GetString(2),
      reader.GetString(3),
      reader.GetString(4),
      reader.GetString(5));
  }

  public async Task<long> UpsertSettingsAsync(long userId, string? avatar, string theme, string controllerPreference, string language, CancellationToken cancellationToken = default)
  {
    await using var connection = CreateConnection();
    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
    await using var command = connection.CreateCommand();
    command.CommandText = @"
INSERT INTO UserSettings (UserId, Avatar, Theme, ControllerPreference, Language)
VALUES ($userId, $avatar, $theme, $controllerPreference, $language)
ON CONFLICT(UserId) DO UPDATE SET
  Avatar = excluded.Avatar,
  Theme = excluded.Theme,
  ControllerPreference = excluded.ControllerPreference,
  Language = excluded.Language;
SELECT Id FROM UserSettings WHERE UserId = $userId LIMIT 1;";
    command.Parameters.AddWithValue("$userId", userId);
    command.Parameters.AddWithValue("$avatar", (object?)avatar ?? DBNull.Value);
    command.Parameters.AddWithValue("$theme", theme);
    command.Parameters.AddWithValue("$controllerPreference", controllerPreference);
    command.Parameters.AddWithValue("$language", language);
    var scalar = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    return Convert.ToInt64(scalar);
  }

  public async Task<ProfileCustomizationDto?> GetProfileCustomizationAsync(long userId, CancellationToken cancellationToken = default)
  {
    const string sql = @"
SELECT DisplayName, Motto, CardStyle, AvatarDataUrl
FROM ProfileCustomizations
WHERE UserId = $userId
LIMIT 1;";
    await using var connection = CreateConnection();
    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.Parameters.AddWithValue("$userId", userId);
    await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
    if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
    {
      return null;
    }

    return new ProfileCustomizationDto(
      reader.GetString(0),
      reader.GetString(1),
      reader.GetString(2),
      reader.IsDBNull(3) ? null : reader.GetString(3));
  }

  public async Task UpsertProfileCustomizationAsync(long userId, string displayName, string motto, string cardStyle, string? avatarDataUrl, CancellationToken cancellationToken = default)
  {
    const string sql = @"
INSERT INTO ProfileCustomizations (UserId, DisplayName, Motto, CardStyle, AvatarDataUrl)
VALUES ($userId, $displayName, $motto, $cardStyle, $avatarDataUrl)
ON CONFLICT(UserId) DO UPDATE SET
  DisplayName = excluded.DisplayName,
  Motto = excluded.Motto,
  CardStyle = excluded.CardStyle,
  AvatarDataUrl = excluded.AvatarDataUrl;";
    await using var connection = CreateConnection();
    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.Parameters.AddWithValue("$userId", userId);
    command.Parameters.AddWithValue("$displayName", displayName);
    command.Parameters.AddWithValue("$motto", motto);
    command.Parameters.AddWithValue("$cardStyle", cardStyle);
    command.Parameters.AddWithValue("$avatarDataUrl", (object?)avatarDataUrl ?? DBNull.Value);
    _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
  }

  public async Task<SessionRecordDto?> GetSessionByTokenAsync(string token, CancellationToken cancellationToken = default)
  {
    const string sql = "SELECT Id, UserId, Token, CreatedAt, ExpiresAt FROM Sessions WHERE Token = $token LIMIT 1;";
    await using var connection = CreateConnection();
    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.Parameters.AddWithValue("$token", token);
    await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
    if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
    {
      return null;
    }

    return new SessionRecordDto(
      reader.GetInt64(0),
      reader.GetInt64(1),
      reader.GetString(2),
      DateTimeOffset.Parse(reader.GetString(3)),
      DateTimeOffset.Parse(reader.GetString(4)));
  }

  public async Task<long> CreateSessionAsync(long userId, string token, DateTimeOffset createdAt, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
  {
    const string sql = @"
INSERT INTO Sessions (UserId, Token, CreatedAt, ExpiresAt)
VALUES ($userId, $token, $createdAt, $expiresAt);
SELECT last_insert_rowid();";
    await using var connection = CreateConnection();
    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.Parameters.AddWithValue("$userId", userId);
    command.Parameters.AddWithValue("$token", token);
    command.Parameters.AddWithValue("$createdAt", createdAt.ToString("O"));
    command.Parameters.AddWithValue("$expiresAt", expiresAt.ToString("O"));
    var scalar = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    return Convert.ToInt64(scalar);
  }

  public async Task RevokeSessionAsync(string token, CancellationToken cancellationToken = default)
  {
    await using var connection = CreateConnection();
    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
    await using var command = connection.CreateCommand();
    command.CommandText = "DELETE FROM Sessions WHERE Token = $token;";
    command.Parameters.AddWithValue("$token", token);
    _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
  }

  public async Task<SessionRecordDto?> RefreshSessionAsync(string currentToken, string newToken, DateTimeOffset createdAt, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
  {
    await using var connection = CreateConnection();
    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
    using var transaction = connection.BeginTransaction();

    try
    {
      await using var lookupCommand = connection.CreateCommand();
      lookupCommand.Transaction = transaction;
      lookupCommand.CommandText = "SELECT Id, UserId, Token, CreatedAt, ExpiresAt FROM Sessions WHERE Token = $currentToken LIMIT 1;";
      lookupCommand.Parameters.AddWithValue("$currentToken", currentToken);

      await using var reader = await lookupCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
      if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
      {
        await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
        return null;
      }

      var userId = reader.GetInt64(1);
      await reader.DisposeAsync().ConfigureAwait(false);

      await using var deleteCommand = connection.CreateCommand();
      deleteCommand.Transaction = transaction;
      deleteCommand.CommandText = "DELETE FROM Sessions WHERE Token = $currentToken;";
      deleteCommand.Parameters.AddWithValue("$currentToken", currentToken);
      _ = await deleteCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

      await using var insertCommand = connection.CreateCommand();
      insertCommand.Transaction = transaction;
      insertCommand.CommandText = @"
INSERT INTO Sessions (UserId, Token, CreatedAt, ExpiresAt)
VALUES ($userId, $newToken, $createdAt, $expiresAt);
SELECT last_insert_rowid();";
      insertCommand.Parameters.AddWithValue("$userId", userId);
      insertCommand.Parameters.AddWithValue("$newToken", newToken);
      insertCommand.Parameters.AddWithValue("$createdAt", createdAt.ToString("O"));
      insertCommand.Parameters.AddWithValue("$expiresAt", expiresAt.ToString("O"));
      var insertId = Convert.ToInt64(await insertCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));

      await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
      return new SessionRecordDto(insertId, userId, newToken, createdAt, expiresAt);
    }
    catch
    {
      await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
      throw;
    }
  }

  public async Task<long> CreateGameSessionAsync(
    string sessionId,
    long userId,
    string gameId,
    string gameTitle,
    string launchPath,
    string status,
    DateTimeOffset createdAt,
    CancellationToken cancellationToken = default)
  {
    const string sql = @"
INSERT INTO GameSessions (SessionId, UserId, GameId, GameTitle, LaunchPath, Status, CreatedAt)
VALUES ($sessionId, $userId, $gameId, $gameTitle, $launchPath, $status, $createdAt);
SELECT last_insert_rowid();";
    await using var connection = CreateConnection();
    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.Parameters.AddWithValue("$sessionId", sessionId);
    command.Parameters.AddWithValue("$userId", userId);
    command.Parameters.AddWithValue("$gameId", gameId);
    command.Parameters.AddWithValue("$gameTitle", gameTitle);
    command.Parameters.AddWithValue("$launchPath", launchPath);
    command.Parameters.AddWithValue("$status", status);
    command.Parameters.AddWithValue("$createdAt", createdAt.ToString("O"));
    var scalar = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    return Convert.ToInt64(scalar);
  }

  public async Task<GameSessionRecordDto?> GetGameSessionBySessionIdAsync(string sessionId, CancellationToken cancellationToken = default)
  {
    const string sql = @"
SELECT Id, SessionId, UserId, GameId, GameTitle, LaunchPath, Status, CreatedAt, StartedAt, StoppedAt, CloudMorphSessionId, StreamUrl, LastError, VirtualDisplayId, WindowHandle
FROM GameSessions
WHERE SessionId = $sessionId
LIMIT 1;";
    await using var connection = CreateConnection();
    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.Parameters.AddWithValue("$sessionId", sessionId);
    await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
    return await ReadGameSessionAsync(reader, cancellationToken).ConfigureAwait(false);
  }

  public async Task<GameSessionRecordDto?> GetActiveGameSessionForUserAsync(long userId, CancellationToken cancellationToken = default)
  {
    const string sql = @"
SELECT Id, SessionId, UserId, GameId, GameTitle, LaunchPath, Status, CreatedAt, StartedAt, StoppedAt, CloudMorphSessionId, StreamUrl, LastError, VirtualDisplayId, WindowHandle
FROM GameSessions
WHERE UserId = $userId AND Status IN ('pending', 'launching', 'running', 'stopping')
ORDER BY CreatedAt DESC
LIMIT 1;";
    await using var connection = CreateConnection();
    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.Parameters.AddWithValue("$userId", userId);
    await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
    return await ReadGameSessionAsync(reader, cancellationToken).ConfigureAwait(false);
  }

  public async Task UpdateGameSessionAsync(
    string sessionId,
    string status,
    DateTimeOffset? startedAt,
    DateTimeOffset? stoppedAt,
    string? cloudMorphSessionId,
    string? streamUrl,
    string? lastError,
    string? virtualDisplayId = null,
    string? windowHandle = null,
    CancellationToken cancellationToken = default)
  {
    const string sql = @"
UPDATE GameSessions
SET Status = $status,
    StartedAt = $startedAt,
    StoppedAt = $stoppedAt,
    CloudMorphSessionId = $cloudMorphSessionId,
    StreamUrl = $streamUrl,
    LastError = $lastError,
    VirtualDisplayId = $virtualDisplayId,
    WindowHandle = $windowHandle
WHERE SessionId = $sessionId;";
    await using var connection = CreateConnection();
    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.Parameters.AddWithValue("$status", status);
    command.Parameters.AddWithValue("$startedAt", startedAt?.ToString("O") ?? (object)DBNull.Value);
    command.Parameters.AddWithValue("$stoppedAt", stoppedAt?.ToString("O") ?? (object)DBNull.Value);
    command.Parameters.AddWithValue("$cloudMorphSessionId", (object?)cloudMorphSessionId ?? DBNull.Value);
    command.Parameters.AddWithValue("$streamUrl", (object?)streamUrl ?? DBNull.Value);
    command.Parameters.AddWithValue("$lastError", (object?)lastError ?? DBNull.Value);
    command.Parameters.AddWithValue("$virtualDisplayId", (object?)virtualDisplayId ?? DBNull.Value);
    command.Parameters.AddWithValue("$windowHandle", (object?)windowHandle ?? DBNull.Value);
    command.Parameters.AddWithValue("$sessionId", sessionId);
    _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
  }

  public async Task<long> AddGameSessionPlayerAsync(string sessionId, long userId, int controllerSlot, DateTimeOffset joinedAt, CancellationToken cancellationToken = default)
  {
    const string sql = @"
INSERT INTO SessionPlayers (SessionId, UserId, ControllerSlot, JoinedAt)
VALUES ($sessionId, $userId, $controllerSlot, $joinedAt);
SELECT last_insert_rowid();";
    await using var connection = CreateConnection();
    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.Parameters.AddWithValue("$sessionId", sessionId);
    command.Parameters.AddWithValue("$userId", userId);
    command.Parameters.AddWithValue("$controllerSlot", controllerSlot);
    command.Parameters.AddWithValue("$joinedAt", joinedAt.ToString("O"));
    var scalar = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    return Convert.ToInt64(scalar);
  }

  public async Task<bool> ClaimGameSessionSlotAsync(string sessionId, long userId, int controllerSlot, DateTimeOffset joinedAt, CancellationToken cancellationToken = default)
  {
    if (controllerSlot <= 0)
    {
      return false;
    }

    const string sql = @"
INSERT INTO SessionPlayers (SessionId, UserId, ControllerSlot, JoinedAt)
VALUES ($sessionId, $userId, $controllerSlot, $joinedAt)
ON CONFLICT(SessionId, ControllerSlot) DO NOTHING;
SELECT changes();";
    await using var connection = CreateConnection();
    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.Parameters.AddWithValue("$sessionId", sessionId);
    command.Parameters.AddWithValue("$userId", userId);
    command.Parameters.AddWithValue("$controllerSlot", controllerSlot);
    command.Parameters.AddWithValue("$joinedAt", joinedAt.ToString("O"));
    var scalar = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    return Convert.ToInt32(scalar) > 0;
  }

  public async Task<bool> ReleaseGameSessionSlotAsync(string sessionId, int controllerSlot, CancellationToken cancellationToken = default)
  {
    const string sql = @"
DELETE FROM SessionPlayers
WHERE SessionId = $sessionId AND ControllerSlot = $controllerSlot;";
    await using var connection = CreateConnection();
    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.Parameters.AddWithValue("$sessionId", sessionId);
    command.Parameters.AddWithValue("$controllerSlot", controllerSlot);
    var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    return affected > 0;
  }

  public async Task<GameSessionPlayerRecordDto?> GetGameSessionSlotAssignmentAsync(string sessionId, int controllerSlot, CancellationToken cancellationToken = default)
  {
    const string sql = @"
SELECT Id, SessionId, UserId, ControllerSlot, JoinedAt
FROM SessionPlayers
WHERE SessionId = $sessionId AND ControllerSlot = $controllerSlot
LIMIT 1;";
    await using var connection = CreateConnection();
    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.Parameters.AddWithValue("$sessionId", sessionId);
    command.Parameters.AddWithValue("$controllerSlot", controllerSlot);
    await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
    if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
    {
      return null;
    }

    return new GameSessionPlayerRecordDto(
      reader.GetInt64(0),
      reader.GetString(1),
      reader.GetInt64(2),
      reader.GetInt32(3),
      DateTimeOffset.Parse(reader.GetString(4)));
  }

  public async Task<int> RemoveGameSessionPlayerAsync(string sessionId, long userId, CancellationToken cancellationToken = default)
  {
    const string sql = @"
DELETE FROM SessionPlayers
WHERE SessionId = $sessionId AND UserId = $userId;";
    await using var connection = CreateConnection();
    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.Parameters.AddWithValue("$sessionId", sessionId);
    command.Parameters.AddWithValue("$userId", userId);
    var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    return affected;
  }

  public async Task<int> GetGameSessionPlayerCountAsync(string sessionId, CancellationToken cancellationToken = default)
  {
    const string sql = "SELECT COUNT(1) FROM SessionPlayers WHERE SessionId = $sessionId;";
    await using var connection = CreateConnection();
    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.Parameters.AddWithValue("$sessionId", sessionId);
    var scalar = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    return Convert.ToInt32(scalar);
  }

  public async Task<IReadOnlyList<GameSessionPlayerRecordDto>> ListGameSessionPlayersAsync(string sessionId, CancellationToken cancellationToken = default)
  {
    const string sql = @"
SELECT Id, SessionId, UserId, ControllerSlot, JoinedAt
FROM SessionPlayers
WHERE SessionId = $sessionId
ORDER BY ControllerSlot ASC;";
    await using var connection = CreateConnection();
    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.Parameters.AddWithValue("$sessionId", sessionId);
    await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

    var players = new List<GameSessionPlayerRecordDto>();
    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
    {
      players.Add(new GameSessionPlayerRecordDto(
        reader.GetInt64(0),
        reader.GetString(1),
        reader.GetInt64(2),
        reader.GetInt32(3),
        DateTimeOffset.Parse(reader.GetString(4))));
    }

    return players;
  }

  public async Task<long> AddChatMessageAsync(long senderUserId, long? recipientUserId, string message, DateTimeOffset createdAt, CancellationToken cancellationToken = default)
  {
    const string sql = @"
INSERT INTO ChatMessages (SenderUserId, RecipientUserId, Message, CreatedAt)
VALUES ($senderUserId, $recipientUserId, $message, $createdAt);
SELECT last_insert_rowid();";
    await using var connection = CreateConnection();
    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.Parameters.AddWithValue("$senderUserId", senderUserId);
    command.Parameters.AddWithValue("$recipientUserId", (object?)recipientUserId ?? DBNull.Value);
    command.Parameters.AddWithValue("$message", message);
    command.Parameters.AddWithValue("$createdAt", createdAt.ToString("O"));
    var scalar = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    return Convert.ToInt64(scalar);
  }

  public async Task<IReadOnlyList<ChatMessageRecordDto>> ListChatMessagesAsync(long userId, int limit = 50, CancellationToken cancellationToken = default)
  {
    const string sql = @"
SELECT Id, SenderUserId, RecipientUserId, Message, CreatedAt
FROM ChatMessages
WHERE RecipientUserId IS NULL OR SenderUserId = $userId OR RecipientUserId = $userId
ORDER BY CreatedAt DESC
LIMIT $limit;";
    await using var connection = CreateConnection();
    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.Parameters.AddWithValue("$userId", userId);
    command.Parameters.AddWithValue("$limit", limit);
    await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

    var messages = new List<ChatMessageRecordDto>();
    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
    {
      messages.Add(new ChatMessageRecordDto(
        reader.GetInt64(0),
        reader.GetInt64(1),
        reader.IsDBNull(2) ? null : reader.GetInt64(2),
        reader.GetString(3),
        DateTimeOffset.Parse(reader.GetString(4))));
    }

    return messages;
  }

  public async Task<IReadOnlyList<FriendLinkRecordDto>> ListFriendLinksAsync(long userId, CancellationToken cancellationToken = default)
  {
    const string sql = @"
SELECT UserAId, UserBId, CreatedAt
FROM FriendLinks
WHERE UserAId = $userId OR UserBId = $userId
ORDER BY CreatedAt DESC;";
    await using var connection = CreateConnection();
    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.Parameters.AddWithValue("$userId", userId);
    await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

    var links = new List<FriendLinkRecordDto>();
    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
    {
      links.Add(new FriendLinkRecordDto(
        reader.GetInt64(0),
        reader.GetInt64(1),
        DateTimeOffset.Parse(reader.GetString(2))));
    }

    return links;
  }

  public async Task<bool> AreFriendsAsync(long userId, long otherUserId, CancellationToken cancellationToken = default)
  {
    if (userId == otherUserId)
    {
      return false;
    }

    var (userAId, userBId) = NormalizeFriendPair(userId, otherUserId);
    const string sql = @"
SELECT 1
FROM FriendLinks
WHERE UserAId = $userAId AND UserBId = $userBId
LIMIT 1;";
    await using var connection = CreateConnection();
    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.Parameters.AddWithValue("$userAId", userAId);
    command.Parameters.AddWithValue("$userBId", userBId);

    var scalar = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    return scalar is not null;
  }

  public async Task AddFriendLinkAsync(long userId, long otherUserId, DateTimeOffset createdAt, CancellationToken cancellationToken = default)
  {
    if (userId == otherUserId)
    {
      throw new InvalidOperationException("Cannot friend the same user.");
    }

    var (userAId, userBId) = NormalizeFriendPair(userId, otherUserId);
    const string sql = @"
INSERT INTO FriendLinks (UserAId, UserBId, CreatedAt)
VALUES ($userAId, $userBId, $createdAt)
ON CONFLICT(UserAId, UserBId) DO NOTHING;";
    await using var connection = CreateConnection();
    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.Parameters.AddWithValue("$userAId", userAId);
    command.Parameters.AddWithValue("$userBId", userBId);
    command.Parameters.AddWithValue("$createdAt", createdAt.ToString("O"));
    _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
  }

  public async Task RemoveFriendLinkAsync(long userId, long otherUserId, CancellationToken cancellationToken = default)
  {
    if (userId == otherUserId)
    {
      return;
    }

    var (userAId, userBId) = NormalizeFriendPair(userId, otherUserId);
    const string sql = @"
DELETE FROM FriendLinks
WHERE UserAId = $userAId AND UserBId = $userBId;";
    await using var connection = CreateConnection();
    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.Parameters.AddWithValue("$userAId", userAId);
    command.Parameters.AddWithValue("$userBId", userBId);
    _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
  }

  public async Task<IReadOnlyList<GameCatalogEntryDto>> ListGameCatalogAsync(CancellationToken cancellationToken = default)
  {
    const string sql = @"
SELECT Id, TitleId, Title, RelativePath, FullPath, Extension, SizeBytes, Genre, Players, LastWriteTimeUtc, LastPlayedAt, CoverPath
FROM GameCatalog
ORDER BY Title COLLATE NOCASE ASC;";
    await using var connection = CreateConnection();
    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

    var entries = new List<GameCatalogEntryDto>();
    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
    {
      entries.Add(ReadGameCatalogEntry(reader));
    }

    return entries;
  }

  public async Task<GameCatalogEntryDto?> GetGameCatalogEntryAsync(string id, CancellationToken cancellationToken = default)
  {
    const string sql = @"
SELECT Id, TitleId, Title, RelativePath, FullPath, Extension, SizeBytes, Genre, Players, LastWriteTimeUtc, LastPlayedAt, CoverPath
FROM GameCatalog
WHERE Id = $id
LIMIT 1;";
    await using var connection = CreateConnection();
    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.Parameters.AddWithValue("$id", id);
    await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
    return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadGameCatalogEntry(reader) : null;
  }

  public async Task UpsertGameCatalogEntryAsync(GameCatalogEntryDto entry, CancellationToken cancellationToken = default)
  {
    const string sql = @"
INSERT INTO GameCatalog (Id, TitleId, Title, RelativePath, FullPath, Extension, SizeBytes, Genre, Players, LastWriteTimeUtc, LastPlayedAt, CoverPath)
VALUES ($id, $titleId, $title, $relativePath, $fullPath, $extension, $sizeBytes, $genre, $players, $lastWriteTimeUtc, $lastPlayedAt, $coverPath)
ON CONFLICT(Id) DO UPDATE SET
  TitleId = excluded.TitleId,
  Title = excluded.Title,
  RelativePath = excluded.RelativePath,
  FullPath = excluded.FullPath,
  Extension = excluded.Extension,
  SizeBytes = excluded.SizeBytes,
  Genre = excluded.Genre,
  Players = excluded.Players,
  LastWriteTimeUtc = excluded.LastWriteTimeUtc,
  LastPlayedAt = excluded.LastPlayedAt,
  CoverPath = excluded.CoverPath;";
    await using var connection = CreateConnection();
    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.Parameters.AddWithValue("$id", entry.Id);
    command.Parameters.AddWithValue("$titleId", entry.TitleId);
    command.Parameters.AddWithValue("$title", entry.Title);
    command.Parameters.AddWithValue("$relativePath", entry.RelativePath);
    command.Parameters.AddWithValue("$fullPath", entry.FullPath);
    command.Parameters.AddWithValue("$extension", entry.Extension);
    command.Parameters.AddWithValue("$sizeBytes", entry.SizeBytes);
    command.Parameters.AddWithValue("$genre", (object?)entry.Genre ?? DBNull.Value);
    command.Parameters.AddWithValue("$players", (object?)entry.Players ?? DBNull.Value);
    command.Parameters.AddWithValue("$lastWriteTimeUtc", entry.LastWriteTimeUtc.ToString("O"));
    command.Parameters.AddWithValue("$lastPlayedAt", (object?)entry.LastPlayedAt?.ToString("O") ?? DBNull.Value);
    command.Parameters.AddWithValue("$coverPath", (object?)entry.CoverPath ?? DBNull.Value);
    _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
  }

  public async Task UpdateLastPlayedAsync(string id, DateTimeOffset lastPlayedAt, CancellationToken cancellationToken = default)
  {
    const string sql = "UPDATE GameCatalog SET LastPlayedAt = $lastPlayedAt WHERE Id = $id;";
    await using var connection = CreateConnection();
    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.Parameters.AddWithValue("$id", id);
    command.Parameters.AddWithValue("$lastPlayedAt", lastPlayedAt.ToString("O"));
    _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
  }

  private static async Task<UserRecordDto?> ReadUserAsync(SqliteDataReader reader, CancellationToken cancellationToken)
  {
    if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
    {
      return null;
    }

    return new UserRecordDto(
      reader.GetInt64(0),
      reader.GetString(1),
      reader.IsDBNull(2) ? null : reader.GetString(2),
      reader.GetString(3),
      reader.GetString(4),
      DateTimeOffset.Parse(reader.GetString(5)),
      reader.IsDBNull(6) ? null : DateTimeOffset.Parse(reader.GetString(6)));
  }

  private static async Task<GameSessionRecordDto?> ReadGameSessionAsync(SqliteDataReader reader, CancellationToken cancellationToken)
  {
    if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
    {
      return null;
    }

    return new GameSessionRecordDto(
      reader.GetInt64(0),
      reader.GetString(1),
      reader.GetInt64(2),
      reader.GetString(3),
      reader.GetString(4),
      reader.GetString(5),
      reader.GetString(6),
      DateTimeOffset.Parse(reader.GetString(7)),
      reader.IsDBNull(8) ? null : DateTimeOffset.Parse(reader.GetString(8)),
      reader.IsDBNull(9) ? null : DateTimeOffset.Parse(reader.GetString(9)),
      reader.IsDBNull(10) ? null : reader.GetString(10),
      reader.IsDBNull(11) ? null : reader.GetString(11),
      reader.IsDBNull(12) ? null : reader.GetString(12),
      reader.IsDBNull(13) ? null : reader.GetString(13),
      reader.IsDBNull(14) ? null : reader.GetString(14));
  }

  private static GameCatalogEntryDto ReadGameCatalogEntry(SqliteDataReader reader)
  {
    return new GameCatalogEntryDto(
      reader.GetString(0),
      reader.GetString(1),
      reader.GetString(2),
      reader.GetString(3),
      reader.GetString(4),
      reader.GetString(5),
      reader.GetInt64(6),
      reader.IsDBNull(7) ? null : reader.GetString(7),
      reader.IsDBNull(8) ? null : reader.GetInt32(8),
      DateTimeOffset.Parse(reader.GetString(9)),
      reader.IsDBNull(10) ? null : DateTimeOffset.Parse(reader.GetString(10)),
      reader.IsDBNull(11) ? null : reader.GetString(11));
  }

  private SqliteConnection CreateConnection() => new($"Data Source={databaseFilePath}");

  private static string ResolvePath(Microsoft.Extensions.Hosting.IHostEnvironment environment, string path)
    => Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(environment.ContentRootPath, path));

  private static (long UserAId, long UserBId) NormalizeFriendPair(long firstUserId, long secondUserId)
    => firstUserId < secondUserId ? (firstUserId, secondUserId) : (secondUserId, firstUserId);
}
