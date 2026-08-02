using Microsoft.Extensions.Logging;
using NetBox.Core.Abstractions;
using NetBox.Data.Repositories;
using NetBox.Models;
using XeniaManager.Core.Abstractions;
using XeniaManager.Models;

namespace NetBox.Core.Services;

/// <summary>
/// Handles player-facing session membership: joining an existing session
/// (claiming the next free controller slot), preventing duplicate
/// assignments, and leaving a session (releasing the slot and detaching
/// from the CloudMorph stream).
/// </summary>
public sealed class InputManager : IInputManager
{
  private const string OwnerLeaveNotAllowedReason = "Session owner cannot leave an active session. Use end session instead.";
  private const int ReservedOwnerSlot = 1;
  private const int MaxControllerSlots = 4;
  private const int JoinClaimAttempts = 3;

  private readonly INetBoxRepository repository;
  private readonly IConsoleSessionManager consoleSessions;
  private readonly IStreamManager streamManager;
  private readonly IBackendEventSink eventSink;
  private readonly ILogger<InputManager> logger;

  public InputManager(
    INetBoxRepository repository,
    IConsoleSessionManager consoleSessions,
    IStreamManager streamManager,
    IBackendEventSink eventSink,
    ILogger<InputManager> logger)
  {
    this.repository = repository;
    this.consoleSessions = consoleSessions;
    this.streamManager = streamManager;
    this.eventSink = eventSink;
    this.logger = logger;
  }

  public async Task<JoinGameSessionResponse> JoinAsync(string sessionToken, string sessionId, CancellationToken cancellationToken = default)
  {
    var userSession = await ResolveValidUserSessionAsync(sessionToken, cancellationToken).ConfigureAwait(false);
    var session = await consoleSessions.GetBySessionIdAsync(sessionId, cancellationToken).ConfigureAwait(false);
    if (session is null)
    {
      throw new InvalidOperationException("Session not found.");
    }

    if (session.OwnerUserId == userSession.UserId)
    {
      throw new InvalidOperationException("Session owner is already part of this session.");
    }

    var existingAssignment = session.ControllerAssignments.FirstOrDefault(a => a.UserId == userSession.UserId);
    if (existingAssignment is not null)
    {
      // Idempotent rejoin (e.g. browser refresh/reconnect): re-attach the
      // existing slot to the stream instead of claiming a new one.
      await TryConnectPlayerAsync(session.CloudMorphSessionId, userSession.UserId, existingAssignment.ControllerSlot, cancellationToken).ConfigureAwait(false);
      return new JoinGameSessionResponse(session.SessionId, session.GameTitle, session.StreamUrl, "live", existingAssignment.ControllerSlot);
    }

    var currentSession = session;
    for (var attempt = 1; attempt <= JoinClaimAttempts; attempt++)
    {
      var takenSlots = currentSession.ControllerAssignments.Select(a => a.ControllerSlot).Append(ReservedOwnerSlot).ToHashSet();
      var freeSlot = Enumerable.Range(ReservedOwnerSlot + 1, MaxControllerSlots - ReservedOwnerSlot).FirstOrDefault(slot => !takenSlots.Contains(slot));
      if (freeSlot == default)
      {
        throw new InvalidOperationException("Session is full.");
      }

      var assignment = await consoleSessions.AttachPlayerAsync(sessionId, userSession.UserId, freeSlot, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
      if (assignment is not null)
      {
        await TryConnectPlayerAsync(currentSession.CloudMorphSessionId, userSession.UserId, assignment.ControllerSlot, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("[session:{SessionId}] User {UserId} joined at controller slot {ControllerSlot}.", sessionId, userSession.UserId, assignment.ControllerSlot);
        await PublishPlayerEventAsync("PlayerJoined", sessionId, currentSession.GameId, userSession.UserId, assignment.ControllerSlot, cancellationToken).ConfigureAwait(false);
        return new JoinGameSessionResponse(currentSession.SessionId, currentSession.GameTitle, currentSession.StreamUrl, "live", assignment.ControllerSlot);
      }

      // Lost a race for the slot; re-read the session state and retry.
      var refreshed = await consoleSessions.GetBySessionIdAsync(sessionId, cancellationToken).ConfigureAwait(false);
      if (refreshed is null)
      {
        throw new InvalidOperationException("Session not found.");
      }

      currentSession = refreshed;
    }

    throw new InvalidOperationException("Unable to claim a controller slot; please try again.");
  }

  public async Task<LeaveGameSessionResponse> LeaveAsync(string sessionToken, string sessionId, CancellationToken cancellationToken = default)
  {
    var userSession = await ResolveValidUserSessionAsync(sessionToken, cancellationToken).ConfigureAwait(false);
    var session = await consoleSessions.GetBySessionIdAsync(sessionId, cancellationToken).ConfigureAwait(false);
    if (session is null)
    {
      throw new InvalidOperationException("Session not found.");
    }

    if (session.OwnerUserId == userSession.UserId)
    {
      throw new UnauthorizedAccessException(OwnerLeaveNotAllowedReason);
    }

    var assignment = session.ControllerAssignments.FirstOrDefault(player => player.UserId == userSession.UserId);
    if (assignment is null)
    {
      throw new InvalidOperationException("Session participant not found.");
    }

    if (!string.IsNullOrWhiteSpace(session.CloudMorphSessionId))
    {
      try
      {
        await streamManager.DetachPlayerAsync(session.CloudMorphSessionId, userSession.UserId, cancellationToken).ConfigureAwait(false);
      }
      catch (Exception ex)
      {
        logger.LogWarning(ex, "[session:{SessionId}] DetachSessionAsync threw for user {UserId}; continuing local leave flow.", sessionId, userSession.UserId);
      }
    }

    var removed = await repository.RemoveGameSessionPlayerAsync(sessionId, userSession.UserId, cancellationToken).ConfigureAwait(false);
    if (removed <= 0)
    {
      throw new InvalidOperationException("Session participant not found.");
    }

    var released = await repository.ReleaseGameSessionSlotAsync(sessionId, assignment.ControllerSlot, cancellationToken).ConfigureAwait(false);
    if (!released)
    {
      logger.LogWarning("[session:{SessionId}] Failed to release slot {ControllerSlot} for user {UserId} during leave.", sessionId, assignment.ControllerSlot, userSession.UserId);
    }

    var playersRemaining = await repository.GetGameSessionPlayerCountAsync(sessionId, cancellationToken).ConfigureAwait(false);
    logger.LogInformation("[session:{SessionId}] User {UserId} left session; players remaining={PlayersRemaining}.", sessionId, userSession.UserId, playersRemaining);
    await PublishPlayerEventAsync("PlayerLeft", sessionId, session.GameId, userSession.UserId, assignment.ControllerSlot, cancellationToken).ConfigureAwait(false);

    return new LeaveGameSessionResponse(true, "left", sessionId, playersRemaining);
  }

  public async Task<int> ResolvePlayerSlotAsync(string sessionToken, string sessionId, CancellationToken cancellationToken = default)
  {
    var userSession = await ResolveValidUserSessionAsync(sessionToken, cancellationToken).ConfigureAwait(false);
    var session = await consoleSessions.GetBySessionIdAsync(sessionId, cancellationToken).ConfigureAwait(false);
    if (session is null)
    {
      throw new InvalidOperationException("Session not found.");
    }

    int controllerSlot;
    if (session.OwnerUserId == userSession.UserId)
    {
      controllerSlot = ReservedOwnerSlot;
    }
    else
    {
      var assignment = session.ControllerAssignments.FirstOrDefault(a => a.UserId == userSession.UserId);
      if (assignment is null)
      {
        throw new InvalidOperationException("Caller has not joined this session.");
      }

      controllerSlot = assignment.ControllerSlot;
    }

    // ControllerSlot is 1-based (owner = 1); NetBox player slots are 0-based.
    return controllerSlot - 1;
  }

  private Task PublishPlayerEventAsync(string type, string sessionId, string gameId, long userId, int controllerSlot, CancellationToken cancellationToken)
  {
    var data = new Dictionary<string, string>
    {
      ["sessionId"] = sessionId,
      ["gameId"] = gameId,
      ["userId"] = userId.ToString(),
      ["controllerSlot"] = controllerSlot.ToString(),
    };

    return eventSink.PublishAsync(new BackendEventDto(type, DateTimeOffset.UtcNow, data), cancellationToken);
  }

  private async Task TryConnectPlayerAsync(string? cloudMorphSessionId, long userId, int controllerSlot, CancellationToken cancellationToken)
  {
    if (string.IsNullOrWhiteSpace(cloudMorphSessionId))
    {
      return;
    }

    try
    {
      await streamManager.ConnectPlayerAsync(cloudMorphSessionId, userId, controllerSlot, cancellationToken).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
      logger.LogWarning(ex, "ConnectPlayerAsync threw for user {UserId} at slot {ControllerSlot}; player joined locally but stream attach may need a retry.", userId, controllerSlot);
    }
  }

  private async Task<SessionRecordDto> ResolveValidUserSessionAsync(string sessionToken, CancellationToken cancellationToken)
  {
    if (string.IsNullOrWhiteSpace(sessionToken))
    {
      throw new UnauthorizedAccessException("Missing session token.");
    }

    var session = await repository.GetSessionByTokenAsync(sessionToken, cancellationToken).ConfigureAwait(false);
    if (session is null || session.ExpiresAt <= DateTimeOffset.UtcNow)
    {
      throw new UnauthorizedAccessException("Invalid or expired session token.");
    }

    return session;
  }
}
