using Microsoft.Extensions.Logging;
using NetBox.Adapters.Xenia;
using NetBox.Core.Abstractions;
using NetBox.Data.Repositories;
using NetBox.Models;
using XeniaManager.Core.Abstractions;
using XeniaManager.Models;

namespace NetBox.Core.Services;

public sealed class GameSessionService : IGameSessionService
{
  private const string OwnerOnlyStopReason = "Only the session owner can end this session.";
  private readonly INetBoxRepository repository;
  private readonly IConsoleSessionManager consoleSessions;
  private readonly IXeniaProfileGateway xeniaProfiles;
  private readonly IGameLauncher gameLauncher;
  private readonly IRuntimeManager runtimeManager;
  private readonly IInputManager inputManager;
  private readonly IBackendEventSink eventSink;
  private readonly ILogger<GameSessionService> logger;

  public GameSessionService(
    INetBoxRepository repository,
    IConsoleSessionManager consoleSessions,
    IXeniaProfileGateway xeniaProfiles,
    IGameLauncher gameLauncher,
    IRuntimeManager runtimeManager,
    IInputManager inputManager,
    IBackendEventSink eventSink,
    ILogger<GameSessionService> logger)
  {
    this.repository = repository;
    this.consoleSessions = consoleSessions;
    this.xeniaProfiles = xeniaProfiles;
    this.gameLauncher = gameLauncher;
    this.runtimeManager = runtimeManager;
    this.inputManager = inputManager;
    this.eventSink = eventSink;
    this.logger = logger;
  }

  public async Task<StartGameSessionResponse> StartAsync(string sessionToken, StartGameSessionRequest request, CancellationToken cancellationToken = default)
  {
    var userSession = await ResolveValidUserSessionAsync(sessionToken, cancellationToken).ConfigureAwait(false);
    var existing = await consoleSessions.GetActiveForOwnerAsync(userSession.UserId, cancellationToken).ConfigureAwait(false);
    if (existing is not null)
    {
      var launcherRunning = await runtimeManager.IsLauncherRunningAsync(cancellationToken).ConfigureAwait(false);

      // Reuse an active stream when possible so repeated launch actions are idempotent.
      if (RuntimeSessionStateMachine.CanResumeStream(RuntimeSessionStateMachine.Parse(existing.ProcessState))
          && !string.IsNullOrWhiteSpace(existing.StreamUrl)
          && launcherRunning)
      {
        var controllerStatus = "live";
        var streamUrl = existing.StreamUrl;
        if (!string.IsNullOrWhiteSpace(existing.CloudMorphSessionId))
        {
          try
          {
            var ready = await runtimeManager.EnsureSessionRuntimeAsync(existing, cancellationToken).ConfigureAwait(false);
            streamUrl = ready.StreamUrl;
            controllerStatus = ready.ControllerStatus;

          }
          catch (Exception ex)
          {
            controllerStatus = "offline";
            streamUrl = existing.StreamUrl;
            logger.LogWarning(ex, "[session:{SessionId}] Stream reuse readiness failed; using fallback URL.", existing.SessionId);
          }
        }

        logger.LogInformation("[session:{SessionId}] Reusing active session (status={Status}, controller={ControllerStatus}).", existing.SessionId, existing.ProcessState, controllerStatus);
        await PublishSessionEventAsync("SessionReused", existing.SessionId, existing.GameId, cancellationToken).ConfigureAwait(false);

        return new StartGameSessionResponse(
          existing.SessionId,
          existing.GameTitle,
          streamUrl,
          existing.ProcessState,
          string.IsNullOrWhiteSpace(controllerStatus) ? "live" : controllerStatus,
          CanStopSession: true,
          AssignedControllerSlot: ResolveAssignedControllerSlot(existing, userSession.UserId));
      }

      // Recover stale active records left after crashes/restarts. The launcher is
      // not running (or CloudMorph cannot confirm health), so the previous record
      // cannot be trusted; never hand back its stream URL to a new caller.
      logger.LogWarning(
        "[session:{SessionId}] Stale active session detected (status={Status}, launcherRunning={LauncherRunning}); recovering before starting a new one.",
        existing.SessionId,
        existing.ProcessState,
        launcherRunning);
      await runtimeManager.CleanupStaleRuntimeAsync(existing, cancellationToken).ConfigureAwait(false);
      await PublishSessionEventAsync("SessionStaleRecovered", existing.SessionId, existing.GameId, cancellationToken).ConfigureAwait(false);
    }

    var gameId = request.GameId.Trim();
    if (string.IsNullOrWhiteSpace(gameId))
    {
      throw new ArgumentException("gameId is required.", nameof(request));
    }

    var launch = await gameLauncher.ResolveGameAsync(gameId, cancellationToken).ConfigureAwait(false);
    var gameTitle = launch.GameTitle;
    var launchPath = launch.LaunchPath;
    var createdAt = DateTimeOffset.UtcNow;

    var consoleSession = await consoleSessions.CreateLaunchingSessionAsync(
      userSession.UserId,
      gameId,
      gameTitle,
      launchPath,
      createdAt,
      cancellationToken).ConfigureAwait(false);
    var sessionId = consoleSession.SessionId;

    using var scope = logger.BeginScope(new Dictionary<string, object> { ["SessionId"] = sessionId, ["GameId"] = gameId });
    logger.LogInformation("[session:{SessionId}] Created pending session for game {GameId} ({GameTitle}).", sessionId, gameId, gameTitle);

    try
    {
      var profile = await LoadLinkedProfileAsync(userSession.UserId, cancellationToken).ConfigureAwait(false);
      logger.LogInformation("[session:{SessionId}] Loaded linked Xenia profile {ProfileId}.", sessionId, profile.Id);

      var runtime = await runtimeManager.StartRuntimeAsync(consoleSession, cancellationToken).ConfigureAwait(false);
      await PublishSessionEventAsync("SessionStarted", sessionId, gameId, cancellationToken).ConfigureAwait(false);

      return new StartGameSessionResponse(sessionId, gameTitle, runtime.StreamUrl, "running", runtime.ControllerStatus, CanStopSession: true, AssignedControllerSlot: ResolveAssignedControllerSlot(consoleSession, userSession.UserId));
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "[session:{SessionId}] Failed to start session for game {GameId}.", sessionId, gameId);
      await runtimeManager.CleanupFailedStartAsync(sessionId, cancellationToken).ConfigureAwait(false);
      
      await consoleSessions.MarkFailedAsync(sessionId, ex.Message, cancellationToken).ConfigureAwait(false);
      await PublishSessionEventAsync("SessionFailed", sessionId, gameId, cancellationToken, ex.Message).ConfigureAwait(false);
      throw;
    }
  }

  public async Task<GameSessionStatusResponse?> GetAsync(string sessionToken, string sessionId, CancellationToken cancellationToken = default)
  {
    var userSession = await ResolveValidUserSessionAsync(sessionToken, cancellationToken).ConfigureAwait(false);
    var session = await consoleSessions.GetBySessionIdAsync(sessionId, cancellationToken).ConfigureAwait(false);
    if (session is null)
    {
      return null;
    }

    var isOwner = session.OwnerUserId == userSession.UserId;
    var isParticipant = session.ControllerAssignments.Any(assignment => assignment.UserId == userSession.UserId);
    if (!isOwner && !isParticipant)
    {
      return null;
    }

    var streamHealth = await runtimeManager.ResolveStreamHealthAsync(session, cancellationToken).ConfigureAwait(false);
    return new GameSessionStatusResponse(
      session.SessionId,
      session.ProcessState,
      session.GameTitle,
      session.ControllerAssignments.Count,
      CanStopSession: isOwner,
      session.StreamUrl,
      session.CloudMorphSessionId,
      session.LastError,
      streamHealth.Status,
      ResolveAssignedControllerSlot(session, userSession.UserId),
      ResolveOccupiedControllerSlots(session));
  }

  public async Task<GameSessionStatusResponse?> ReconnectAsync(string sessionToken, CancellationToken cancellationToken = default)
  {
    var userSession = await ResolveValidUserSessionAsync(sessionToken, cancellationToken).ConfigureAwait(false);
    var session = await consoleSessions.GetActiveForOwnerAsync(userSession.UserId, cancellationToken).ConfigureAwait(false);
    if (session is null)
    {
      return null;
    }

    var launcherRunning = await runtimeManager.IsLauncherRunningAsync(cancellationToken).ConfigureAwait(false);

    var recoverableState = RuntimeSessionStateMachine.CanResumeStream(RuntimeSessionStateMachine.Parse(session.ProcessState));

    if (recoverableState && launcherRunning)
    {
      var runtime = await runtimeManager.EnsureSessionRuntimeAsync(session, cancellationToken).ConfigureAwait(false);

      return new GameSessionStatusResponse(
        session.SessionId,
        session.ProcessState,
        session.GameTitle,
        session.ControllerAssignments.Count,
        CanStopSession: true,
        runtime.StreamUrl,
        runtime.CloudMorphSessionId,
        session.LastError,
        string.IsNullOrWhiteSpace(runtime.StreamHealth) ? runtime.ControllerStatus : runtime.StreamHealth,
        ResolveAssignedControllerSlot(session, userSession.UserId),
        ResolveOccupiedControllerSlots(session));
    }

    await runtimeManager.CleanupStaleRuntimeAsync(session, cancellationToken).ConfigureAwait(false);
    await PublishSessionEventAsync("SessionStaleRecovered", session.SessionId, session.GameId, cancellationToken).ConfigureAwait(false);
    return null;
  }

  private static int ResolveAssignedControllerSlot(ConsoleSession session, long userId)
    => session.ControllerAssignments.FirstOrDefault(assignment => assignment.UserId == userId)?.ControllerSlot ?? 1;

  private static IReadOnlyList<int> ResolveOccupiedControllerSlots(ConsoleSession session)
    => session.ControllerAssignments.Select(assignment => assignment.ControllerSlot).OrderBy(slot => slot).ToArray();

  public async Task<StopGameSessionResponse> StopAsync(string sessionToken, string sessionId, CancellationToken cancellationToken = default)
  {
    var userSession = await ResolveValidUserSessionAsync(sessionToken, cancellationToken).ConfigureAwait(false);
    var session = await consoleSessions.GetBySessionIdAsync(sessionId, cancellationToken).ConfigureAwait(false);
    if (session is null)
    {
      throw new InvalidOperationException("Session not found.");
    }

    var isOwner = session.OwnerUserId == userSession.UserId;
    if (!isOwner)
    {
      var isParticipant = session.ControllerAssignments.Any(assignment => assignment.UserId == userSession.UserId);
      if (isParticipant)
      {
        throw new UnauthorizedAccessException(OwnerOnlyStopReason);
      }

      throw new InvalidOperationException("Session not found.");
    }

    logger.LogInformation("[session:{SessionId}] Stop requested.", sessionId);
    await consoleSessions.MarkStoppingAsync(sessionId, cancellationToken).ConfigureAwait(false);
    await runtimeManager.StopRuntimeAsync(session, cancellationToken).ConfigureAwait(false);

    await consoleSessions.MarkStoppedAsync(sessionId, cancellationToken).ConfigureAwait(false);

    logger.LogInformation("[session:{SessionId}] Stopped.", sessionId);
    await PublishSessionEventAsync("SessionStopped", sessionId, session.GameId, cancellationToken).ConfigureAwait(false);

    return new StopGameSessionResponse(true, "stopped");
  }

  public Task<LeaveGameSessionResponse> LeaveAsync(string sessionToken, string sessionId, CancellationToken cancellationToken = default)
    => inputManager.LeaveAsync(sessionToken, sessionId, cancellationToken);

  private Task PublishSessionEventAsync(string type, string sessionId, string gameId, CancellationToken cancellationToken, string? error = null)
  {
    var data = new Dictionary<string, string>
    {
      ["sessionId"] = sessionId,
      ["gameId"] = gameId,
    };

    if (error is not null)
    {
      data["error"] = error;
    }

    return eventSink.PublishAsync(new BackendEventDto(type, DateTimeOffset.UtcNow, data), cancellationToken);
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

  private async Task<NetBoxXeniaProfileDto> LoadLinkedProfileAsync(long userId, CancellationToken cancellationToken)
  {
    var user = await repository.GetUserByIdAsync(userId, cancellationToken).ConfigureAwait(false);
    if (user is null)
    {
      throw new InvalidOperationException("Unable to resolve session owner.");
    }

    if (string.IsNullOrWhiteSpace(user.XeniaProfileId))
    {
      throw new InvalidOperationException("Session owner has no linked Xenia profile.");
    }

    var profile = await xeniaProfiles.GetProfileAsync(user.XeniaProfileId, cancellationToken).ConfigureAwait(false);
    if (profile is null)
    {
      throw new InvalidOperationException("Linked Xenia profile could not be loaded.");
    }

    return profile;
  }

}
