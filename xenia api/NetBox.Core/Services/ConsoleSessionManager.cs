using NetBox.Core.Abstractions;
using NetBox.Data.Repositories;
using NetBox.Models;

namespace NetBox.Core.Services;

public sealed class ConsoleSessionManager : IConsoleSessionManager
{
  private readonly INetBoxRepository repository;

  public ConsoleSessionManager(INetBoxRepository repository)
  {
    this.repository = repository;
  }

  public Task<ConsoleSession?> GetBySessionIdAsync(string sessionId, CancellationToken cancellationToken = default)
    => LoadAsync(() => repository.GetGameSessionBySessionIdAsync(sessionId, cancellationToken), cancellationToken);

  public Task<ConsoleSession?> GetActiveForOwnerAsync(long ownerUserId, CancellationToken cancellationToken = default)
    => LoadAsync(() => repository.GetActiveGameSessionForUserAsync(ownerUserId, cancellationToken), cancellationToken);

  public async Task<ConsoleSession> CreateLaunchingSessionAsync(
    long ownerUserId,
    string gameId,
    string gameTitle,
    string launchPath,
    DateTimeOffset createdAt,
    CancellationToken cancellationToken = default)
  {
    var sessionId = Guid.NewGuid().ToString("N");
    _ = await repository.CreateGameSessionAsync(
      sessionId,
      ownerUserId,
      gameId,
      gameTitle,
      launchPath,
      "pending",
      createdAt,
      cancellationToken).ConfigureAwait(false);

    _ = await repository.AddGameSessionPlayerAsync(sessionId, ownerUserId, 1, createdAt, cancellationToken).ConfigureAwait(false);

    var created = await GetBySessionIdAsync(sessionId, cancellationToken).ConfigureAwait(false);
    return created ?? throw new InvalidOperationException("Failed to create console session.");
  }

  public async Task MarkLaunchingAsync(string sessionId, CancellationToken cancellationToken = default)
  {
    var session = await RequireAsync(sessionId, cancellationToken).ConfigureAwait(false);
    RuntimeSessionStateMachine.EnsureValidTransition(RuntimeSessionStateMachine.Parse(session.ProcessState), RuntimeSessionState.Launching);
    await repository.UpdateGameSessionAsync(
      sessionId,
      RuntimeSessionStateMachine.ToWireString(RuntimeSessionState.Launching),
      session.StartedAt,
      session.StoppedAt,
      session.CloudMorphSessionId,
      session.StreamUrl,
      null,
      session.VirtualDisplayId,
      session.WindowHandle,
      cancellationToken).ConfigureAwait(false);
  }

  public async Task MarkStaleRecoveredAsync(string sessionId, string? lastError, CancellationToken cancellationToken = default)
  {
    var session = await RequireAsync(sessionId, cancellationToken).ConfigureAwait(false);
    RuntimeSessionStateMachine.EnsureValidTransition(RuntimeSessionStateMachine.Parse(session.ProcessState), RuntimeSessionState.Stopped);
    await repository.UpdateGameSessionAsync(
      sessionId,
      RuntimeSessionStateMachine.ToWireString(RuntimeSessionState.Stopped),
      session.StartedAt,
      DateTimeOffset.UtcNow,
      session.CloudMorphSessionId,
      session.StreamUrl,
      lastError,
      session.VirtualDisplayId,
      session.WindowHandle,
      cancellationToken).ConfigureAwait(false);
  }

  public async Task MarkRunningAsync(string sessionId, string cloudMorphSessionId, string streamUrl, CancellationToken cancellationToken = default)
  {
    var session = await RequireAsync(sessionId, cancellationToken).ConfigureAwait(false);
    RuntimeSessionStateMachine.EnsureValidTransition(RuntimeSessionStateMachine.Parse(session.ProcessState), RuntimeSessionState.Running);
    await repository.UpdateGameSessionAsync(
      sessionId,
      RuntimeSessionStateMachine.ToWireString(RuntimeSessionState.Running),
      DateTimeOffset.UtcNow,
      null,
      cloudMorphSessionId,
      streamUrl,
      null,
      session.VirtualDisplayId,
      session.WindowHandle,
      cancellationToken).ConfigureAwait(false);
  }

  public async Task UpdateStreamBindingAsync(string sessionId, string cloudMorphSessionId, string streamUrl, string? lastError = null, CancellationToken cancellationToken = default)
  {
    var session = await RequireAsync(sessionId, cancellationToken).ConfigureAwait(false);
    await repository.UpdateGameSessionAsync(
      sessionId,
      session.ProcessState,
      session.StartedAt,
      session.StoppedAt,
      cloudMorphSessionId,
      streamUrl,
      lastError,
      session.VirtualDisplayId,
      session.WindowHandle,
      cancellationToken).ConfigureAwait(false);
  }

  public async Task SetVirtualDisplayIdAsync(string sessionId, string? virtualDisplayId, CancellationToken cancellationToken = default)
  {
    var session = await RequireAsync(sessionId, cancellationToken).ConfigureAwait(false);
    await repository.UpdateGameSessionAsync(
      sessionId,
      session.ProcessState,
      session.StartedAt,
      session.StoppedAt,
      session.CloudMorphSessionId,
      session.StreamUrl,
      null,
      virtualDisplayId,
      session.WindowHandle,
      cancellationToken).ConfigureAwait(false);
  }

  public async Task SetWindowHandleAsync(string sessionId, string? windowHandle, CancellationToken cancellationToken = default)
  {
    var session = await RequireAsync(sessionId, cancellationToken).ConfigureAwait(false);
    await repository.UpdateGameSessionAsync(
      sessionId,
      session.ProcessState,
      session.StartedAt,
      session.StoppedAt,
      session.CloudMorphSessionId,
      session.StreamUrl,
      null,
      session.VirtualDisplayId,
      windowHandle,
      cancellationToken).ConfigureAwait(false);
  }

  public async Task MarkStreamUnavailableAsync(string sessionId, string streamUrl, string? lastError, CancellationToken cancellationToken = default)
  {
    var session = await RequireAsync(sessionId, cancellationToken).ConfigureAwait(false);
    await repository.UpdateGameSessionAsync(
      sessionId,
      session.ProcessState,
      session.StartedAt,
      session.StoppedAt,
      session.CloudMorphSessionId,
      streamUrl,
      lastError,
      session.VirtualDisplayId,
      session.WindowHandle,
      cancellationToken).ConfigureAwait(false);
  }

  public async Task MarkStoppingAsync(string sessionId, CancellationToken cancellationToken = default)
  {
    var session = await RequireAsync(sessionId, cancellationToken).ConfigureAwait(false);
    RuntimeSessionStateMachine.EnsureValidTransition(RuntimeSessionStateMachine.Parse(session.ProcessState), RuntimeSessionState.Stopping);
    await repository.UpdateGameSessionAsync(
      sessionId,
      RuntimeSessionStateMachine.ToWireString(RuntimeSessionState.Stopping),
      session.StartedAt,
      null,
      session.CloudMorphSessionId,
      session.StreamUrl,
      null,
      session.VirtualDisplayId,
      session.WindowHandle,
      cancellationToken).ConfigureAwait(false);
  }

  public async Task MarkStoppedAsync(string sessionId, CancellationToken cancellationToken = default)
  {
    var session = await RequireAsync(sessionId, cancellationToken).ConfigureAwait(false);
    RuntimeSessionStateMachine.EnsureValidTransition(RuntimeSessionStateMachine.Parse(session.ProcessState), RuntimeSessionState.Stopped);
    await repository.UpdateGameSessionAsync(
      sessionId,
      RuntimeSessionStateMachine.ToWireString(RuntimeSessionState.Stopped),
      session.StartedAt,
      DateTimeOffset.UtcNow,
      session.CloudMorphSessionId,
      session.StreamUrl,
      null,
      session.VirtualDisplayId,
      session.WindowHandle,
      cancellationToken).ConfigureAwait(false);
  }

  public async Task MarkFailedAsync(string sessionId, string lastError, CancellationToken cancellationToken = default)
  {
    var session = await RequireAsync(sessionId, cancellationToken).ConfigureAwait(false);
    RuntimeSessionStateMachine.EnsureValidTransition(RuntimeSessionStateMachine.Parse(session.ProcessState), RuntimeSessionState.Failed);
    await repository.UpdateGameSessionAsync(
      sessionId,
      RuntimeSessionStateMachine.ToWireString(RuntimeSessionState.Failed),
      null,
      DateTimeOffset.UtcNow,
      null,
      null,
      lastError,
      session.VirtualDisplayId,
      session.WindowHandle,
      cancellationToken).ConfigureAwait(false);
  }

  public async Task<ConsoleSessionControllerAssignment?> AttachPlayerAsync(string sessionId, long userId, int controllerSlot, DateTimeOffset joinedAt, CancellationToken cancellationToken = default)
  {
    _ = await RequireAsync(sessionId, cancellationToken).ConfigureAwait(false);

    if (controllerSlot <= 0)
    {
      return null;
    }

    var claimed = await repository.ClaimGameSessionSlotAsync(sessionId, userId, controllerSlot, joinedAt, cancellationToken).ConfigureAwait(false);
    if (!claimed)
    {
      return null;
    }

    return new ConsoleSessionControllerAssignment(userId, controllerSlot, joinedAt);
  }

  private async Task<ConsoleSessionRecord> RequireAsync(string sessionId, CancellationToken cancellationToken)
  {
    var session = await GetBySessionIdAsync(sessionId, cancellationToken).ConfigureAwait(false);
    if (session is null)
    {
      throw new InvalidOperationException("Session not found.");
    }

    return new ConsoleSessionRecord(session);
  }

  private async Task<ConsoleSession?> LoadAsync(
    Func<Task<GameSessionRecordDto?>> fetch,
    CancellationToken cancellationToken)
  {
    var record = await fetch().ConfigureAwait(false);
    if (record is null)
    {
      return null;
    }

    var players = await repository.ListGameSessionPlayersAsync(record.SessionId, cancellationToken).ConfigureAwait(false);
    var assignments = players
      .OrderBy(player => player.ControllerSlot)
      .Select(player => new ConsoleSessionControllerAssignment(player.UserId, player.ControllerSlot, player.JoinedAt))
      .ToArray();

    return new ConsoleSession(
      record.SessionId,
      record.UserId,
      record.GameId,
      record.GameTitle,
      record.LaunchPath,
      record.Status,
      ResolveStreamState(record),
      assignments,
      record.StreamUrl,
      record.CloudMorphSessionId,
      record.CreatedAt,
      record.StartedAt,
      record.StoppedAt,
      record.LastError,
      record.VirtualDisplayId,
      record.WindowHandle);
  }

  private static string ResolveStreamState(GameSessionRecordDto record)
  {
    if (!string.IsNullOrWhiteSpace(record.StreamUrl))
    {
      return "ready";
    }

    var state = RuntimeSessionStateMachine.Parse(record.Status);
    if (state == RuntimeSessionState.Failed)
    {
      return "failed";
    }

    if (state is RuntimeSessionState.Stopped or RuntimeSessionState.Stopping)
    {
      return "offline";
    }

    return "pending";
  }

  private sealed record ConsoleSessionRecord(ConsoleSession Session)
  {
    public DateTimeOffset? StartedAt => Session.StartedAt;
    public DateTimeOffset? StoppedAt => Session.StoppedAt;
    public string? CloudMorphSessionId => Session.CloudMorphSessionId;
    public string? StreamUrl => Session.StreamUrl;
    public string ProcessState => Session.ProcessState;
    public string? VirtualDisplayId => Session.VirtualDisplayId;
    public string? WindowHandle => Session.WindowHandle;
    public string? LastError => Session.LastError;
  }
}
