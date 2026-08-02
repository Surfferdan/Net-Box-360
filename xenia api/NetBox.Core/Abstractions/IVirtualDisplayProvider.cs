namespace NetBox.Core.Abstractions;

/// <summary>
/// Manages virtual display lifecycle for game sessions.
/// Provisions virtual displays on session start and releases them on session stop.
/// </summary>
public interface IVirtualDisplayProvider
{
  /// <summary>
  /// Creates and provisions a virtual display for the given session.
  /// </summary>
  /// <param name="sessionId">Unique session identifier</param>
  /// <param name="gameTitle">Title of the game being played, for logging</param>
  /// <param name="cancellationToken">Cancellation token</param>
  /// <returns>Virtual display ID if successful, null if provisioning fails</returns>
  Task<string?> ProvisionDisplayAsync(string sessionId, string gameTitle, CancellationToken cancellationToken = default);

  /// <summary>
  /// Releases and cleans up a virtual display.
  /// Must be idempotent - calling with a non-existent display ID should not error.
  /// </summary>
  /// <param name="sessionId">Unique session identifier</param>
  /// <param name="virtualDisplayId">Virtual display identifier to release</param>
  /// <param name="cancellationToken">Cancellation token</param>
  Task ReleaseDisplayAsync(string sessionId, string? virtualDisplayId, CancellationToken cancellationToken = default);

  /// <summary>
  /// Gets the status of a provisioned virtual display.
  /// </summary>
  /// <param name="virtualDisplayId">Virtual display identifier</param>
  /// <param name="cancellationToken">Cancellation token</param>
  /// <returns>Display status (e.g., "active", "inactive", "unknown")</returns>
  Task<string> GetDisplayStatusAsync(string? virtualDisplayId, CancellationToken cancellationToken = default);

  /// <summary>
  /// Performs cleanup of any orphaned/leaked displays from previous sessions.
  /// Called during initialization to ensure clean state.
  /// </summary>
  /// <param name="cancellationToken">Cancellation token</param>
  Task CleanupOrphanedDisplaysAsync(CancellationToken cancellationToken = default);
}
