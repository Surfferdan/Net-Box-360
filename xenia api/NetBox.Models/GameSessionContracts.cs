namespace NetBox.Models;

public sealed record StartGameSessionRequest(string GameId);

public sealed record StartGameSessionResponse(
  string SessionId,
  string Game,
  string StreamUrl,
  string Status,
  string ControllerStatus,
  bool CanStopSession,
  int AssignedControllerSlot);

public sealed record GameSessionStatusResponse(
  string SessionId,
  string Status,
  string Game,
  int Players,
  bool CanStopSession,
  string? StreamUrl,
  string? CloudMorphSessionId,
  string? Error,
  string StreamHealth,
  int AssignedControllerSlot,
  IReadOnlyList<int> OccupiedControllerSlots);

public sealed record StopGameSessionResponse(bool Success, string Status);

public sealed record LeaveGameSessionResponse(bool Success, string Status, string SessionId, int PlayersRemaining);

public sealed record JoinGameSessionResponse(
  string SessionId,
  string Game,
  string? StreamUrl,
  string ControllerStatus,
  int AssignedControllerSlot);


public sealed record CloudStreamStartResult(string CloudMorphSessionId, string StreamUrl, string ControllerStatus);

public sealed record CloudMorphSessionOptions(
  string CaptureMode,
  string TargetWindowTitle);

public sealed record CloudMorphHealthResponse(
  string Status,
  bool CaptureReady,
  bool StreamReady,
  int ActiveSessions);

public sealed record CloudMorphStreamStatus(
  string StreamId,
  string Status,
  string? Error);

public sealed record XeniaGameCatalogItemDto(
  string Id,
  string Name,
  string RelativePath,
  string FullPath,
  string Extension,
  DateTimeOffset LastWriteTimeUtc,
  string? CoverPath);

public sealed record GameSessionRecordDto(
  long Id,
  string SessionId,
  long UserId,
  string GameId,
  string GameTitle,
  string LaunchPath,
  string Status,
  DateTimeOffset CreatedAt,
  DateTimeOffset? StartedAt,
  DateTimeOffset? StoppedAt,
  string? CloudMorphSessionId,
  string? StreamUrl,
  string? LastError,
  string? VirtualDisplayId,
  string? WindowHandle);

public sealed record GameCatalogEntryDto(
  string Id,
  string TitleId,
  string Title,
  string RelativePath,
  string FullPath,
  string Extension,
  long SizeBytes,
  string? Genre,
  int? Players,
  DateTimeOffset LastWriteTimeUtc,
  DateTimeOffset? LastPlayedAt,
  string? CoverPath);

public sealed record GameSessionPlayerRecordDto(
  long Id,
  string SessionId,
  long UserId,
  int ControllerSlot,
  DateTimeOffset JoinedAt);

public sealed record ConsoleSession(
  string SessionId,
  long OwnerUserId,
  string GameId,
  string GameTitle,
  string LaunchPath,
  string ProcessState,
  string StreamState,
  IReadOnlyList<ConsoleSessionControllerAssignment> ControllerAssignments,
  string? StreamUrl,
  string? CloudMorphSessionId,
  DateTimeOffset CreatedAt,
  DateTimeOffset? StartedAt,
  DateTimeOffset? StoppedAt,
  string? LastError,
  string? VirtualDisplayId,
  string? WindowHandle);

public sealed record ConsoleSessionControllerAssignment(
  long UserId,
  int ControllerSlot,
  DateTimeOffset JoinedAt);
