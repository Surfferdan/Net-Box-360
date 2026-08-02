using NetBox.Core.Services;
using Xunit;

namespace NetBox.Tests;

public sealed class ConsoleSessionManagerTests
{
  [Fact]
  public async Task CreateLaunchingSessionAsync_CreatesConsoleSessionWithOwnerAndController()
  {
    var repository = new TestNetBoxRepository();
    var manager = new ConsoleSessionManager(repository);

    var createdAt = DateTimeOffset.UtcNow;
    var session = await manager.CreateLaunchingSessionAsync(
      ownerUserId: 42,
      gameId: "halo4",
      gameTitle: "Halo 4",
      launchPath: "C:/games/halo4.iso",
      createdAt: createdAt);

    Assert.Equal(42, session.OwnerUserId);
    Assert.Equal("halo4", session.GameId);
    Assert.Equal("Halo 4", session.GameTitle);
    Assert.Equal("pending", session.ProcessState);
    Assert.Equal("pending", session.StreamState);
    Assert.Single(session.ControllerAssignments);
    Assert.Equal(1, session.ControllerAssignments[0].ControllerSlot);
    Assert.Equal(42, session.ControllerAssignments[0].UserId);
  }

  [Fact]
  public async Task MarkRunningAsync_UpdatesStreamStateAndUrl()
  {
    var repository = new TestNetBoxRepository();
    var manager = new ConsoleSessionManager(repository);

    var session = await manager.CreateLaunchingSessionAsync(
      ownerUserId: 100,
      gameId: "deadspace",
      gameTitle: "Dead Space",
      launchPath: "C:/games/deadspace.iso",
      createdAt: DateTimeOffset.UtcNow);

    await manager.MarkLaunchingAsync(session.SessionId, CancellationToken.None);

    await manager.MarkRunningAsync(session.SessionId, "cloud-1", "ws://localhost:3000/session", CancellationToken.None);

    var refreshed = await manager.GetBySessionIdAsync(session.SessionId);
    Assert.NotNull(refreshed);
    Assert.Equal("running", refreshed!.ProcessState);
    Assert.Equal("ready", refreshed.StreamState);
    Assert.Equal("cloud-1", refreshed.CloudMorphSessionId);
    Assert.Equal("ws://localhost:3000/session", refreshed.StreamUrl);
  }

  [Fact]
  public async Task MarkStoppedAsync_TransitionsProcessState()
  {
    var repository = new TestNetBoxRepository();
    var manager = new ConsoleSessionManager(repository);

    var session = await manager.CreateLaunchingSessionAsync(
      ownerUserId: 5,
      gameId: "forza",
      gameTitle: "Forza",
      launchPath: "C:/games/forza.xex",
      createdAt: DateTimeOffset.UtcNow);

    await manager.MarkStoppingAsync(session.SessionId);
    await manager.MarkStoppedAsync(session.SessionId);

    var stopped = await manager.GetBySessionIdAsync(session.SessionId);
    Assert.NotNull(stopped);
    Assert.Equal("stopped", stopped!.ProcessState);
    Assert.Equal("offline", stopped.StreamState);
    Assert.NotNull(stopped.StoppedAt);
  }

  [Fact]
  public async Task AttachPlayerAsync_ClaimsRequestedSlotAndRejectsConflicts()
  {
    var repository = new TestNetBoxRepository();
    var manager = new ConsoleSessionManager(repository);

    var session = await manager.CreateLaunchingSessionAsync(
      ownerUserId: 10,
      gameId: "gears",
      gameTitle: "Gears of War",
      launchPath: "C:/games/gears.iso",
      createdAt: DateTimeOffset.UtcNow);

    var first = await manager.AttachPlayerAsync(session.SessionId, 99, 2, DateTimeOffset.UtcNow, CancellationToken.None);
    Assert.NotNull(first);
    Assert.Equal(99, first!.UserId);
    Assert.Equal(2, first.ControllerSlot);

    var conflict = await manager.AttachPlayerAsync(session.SessionId, 100, 2, DateTimeOffset.UtcNow, CancellationToken.None);
    Assert.Null(conflict);
  }

  [Fact]
  public async Task ClaimSlotAsync_PreservesOwnerSlotAndReleasesGuestSlot()
  {
    var repository = new TestNetBoxRepository();
    var manager = new ConsoleSessionManager(repository);

    var session = await manager.CreateLaunchingSessionAsync(
      ownerUserId: 10,
      gameId: "gears",
      gameTitle: "Gears of War",
      launchPath: "C:/games/gears.iso",
      createdAt: DateTimeOffset.UtcNow);

    var claimed = await repository.ClaimGameSessionSlotAsync(session.SessionId, 99, 2, DateTimeOffset.UtcNow, CancellationToken.None);
    Assert.True(claimed);

    var assigned = await repository.GetGameSessionSlotAssignmentAsync(session.SessionId, 2, CancellationToken.None);
    Assert.NotNull(assigned);
    Assert.Equal(99, assigned!.UserId);

    var released = await repository.ReleaseGameSessionSlotAsync(session.SessionId, 2, CancellationToken.None);
    Assert.True(released);

    var cleared = await repository.GetGameSessionSlotAssignmentAsync(session.SessionId, 2, CancellationToken.None);
    Assert.Null(cleared);
  }

  [Fact]
  public async Task MarkRunningAsync_ThrowsWhenSessionNeverTransitionedThroughLaunching()
  {
    var repository = new TestNetBoxRepository();
    var manager = new ConsoleSessionManager(repository);

    var session = await manager.CreateLaunchingSessionAsync(
      ownerUserId: 7,
      gameId: "fable",
      gameTitle: "Fable",
      launchPath: "C:/games/fable.iso",
      createdAt: DateTimeOffset.UtcNow);

    // Session is still "pending" - going straight to "running" skips the
    // required "launching" step and must be rejected by the state machine.
    var exception = await Assert.ThrowsAsync<InvalidRuntimeStateTransitionException>(
      () => manager.MarkRunningAsync(session.SessionId, "cloud-1", "ws://localhost:3000/session", CancellationToken.None));

    Assert.Equal(RuntimeSessionState.Pending, exception.From);
    Assert.Equal(RuntimeSessionState.Running, exception.To);

    var unchanged = await manager.GetBySessionIdAsync(session.SessionId);
    Assert.Equal("pending", unchanged!.ProcessState);
  }

  [Fact]
  public async Task MarkRunningAsync_ThrowsWhenSessionIsAlreadyStopped()
  {
    var repository = new TestNetBoxRepository();
    var manager = new ConsoleSessionManager(repository);

    var session = await manager.CreateLaunchingSessionAsync(
      ownerUserId: 8,
      gameId: "banjo",
      gameTitle: "Banjo-Kazooie",
      launchPath: "C:/games/banjo.iso",
      createdAt: DateTimeOffset.UtcNow);

    await manager.MarkStoppingAsync(session.SessionId);
    await manager.MarkStoppedAsync(session.SessionId);

    // Stopped is a terminal state - nothing should be able to resurrect it.
    await Assert.ThrowsAsync<InvalidRuntimeStateTransitionException>(
      () => manager.MarkRunningAsync(session.SessionId, "cloud-1", "ws://localhost:3000/session", CancellationToken.None));
    await Assert.ThrowsAsync<InvalidRuntimeStateTransitionException>(
      () => manager.MarkLaunchingAsync(session.SessionId, CancellationToken.None));
  }

  [Fact]
  public async Task MarkStoppingAsync_IsIdempotentForAlreadyStoppingSession()
  {
    var repository = new TestNetBoxRepository();
    var manager = new ConsoleSessionManager(repository);

    var session = await manager.CreateLaunchingSessionAsync(
      ownerUserId: 9,
      gameId: "perfectdark",
      gameTitle: "Perfect Dark",
      launchPath: "C:/games/perfectdark.iso",
      createdAt: DateTimeOffset.UtcNow);

    await manager.MarkStoppingAsync(session.SessionId);

    // Calling stopping again on an already-stopping session is a no-op, not an error.
    var exception = await Record.ExceptionAsync(() => manager.MarkStoppingAsync(session.SessionId));
    Assert.Null(exception);

    var current = await manager.GetBySessionIdAsync(session.SessionId);
    Assert.Equal("stopping", current!.ProcessState);
  }
}
