using NetBox.Models;

namespace NetBox.Core.Abstractions;

public interface IConsoleSessionManager
{
  Task<ConsoleSession?> GetBySessionIdAsync(string sessionId, CancellationToken cancellationToken = default);
  Task<ConsoleSession?> GetActiveForOwnerAsync(long ownerUserId, CancellationToken cancellationToken = default);
  Task<ConsoleSession> CreateLaunchingSessionAsync(
    long ownerUserId,
    string gameId,
    string gameTitle,
    string launchPath,
    DateTimeOffset createdAt,
    CancellationToken cancellationToken = default);
  Task MarkLaunchingAsync(string sessionId, CancellationToken cancellationToken = default);
  Task MarkStaleRecoveredAsync(string sessionId, string? lastError, CancellationToken cancellationToken = default);
  Task MarkRunningAsync(string sessionId, string cloudMorphSessionId, string streamUrl, CancellationToken cancellationToken = default);
  Task UpdateStreamBindingAsync(string sessionId, string cloudMorphSessionId, string streamUrl, string? lastError = null, CancellationToken cancellationToken = default);
  Task MarkStreamUnavailableAsync(string sessionId, string streamUrl, string? lastError, CancellationToken cancellationToken = default);
  Task MarkStoppingAsync(string sessionId, CancellationToken cancellationToken = default);
  Task MarkStoppedAsync(string sessionId, CancellationToken cancellationToken = default);
  Task MarkFailedAsync(string sessionId, string lastError, CancellationToken cancellationToken = default);
  Task<ConsoleSessionControllerAssignment?> AttachPlayerAsync(string sessionId, long userId, int controllerSlot, DateTimeOffset joinedAt, CancellationToken cancellationToken = default);
  Task SetVirtualDisplayIdAsync(string sessionId, string? virtualDisplayId, CancellationToken cancellationToken = default);
  Task SetWindowHandleAsync(string sessionId, string? windowHandle, CancellationToken cancellationToken = default);
}