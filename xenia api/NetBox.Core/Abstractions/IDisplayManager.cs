namespace NetBox.Core.Abstractions;

/// <summary>
/// Owns virtual display provisioning/release AND the monitor-assignment /
/// window-placement policy for a session. Used by <see cref="IRuntimeManager"/>
/// and <see cref="IGameLauncher"/> so neither talks to
/// <see cref="IVirtualDisplayProvider"/> or raw Win32 window/monitor APIs
/// directly - this manager is the single owner of "which monitor does this
/// session's game window belong on, and how does it get placed there".
/// </summary>
public interface IDisplayManager
{
  Task<string?> ProvisionAsync(string sessionId, string gameTitle, CancellationToken cancellationToken = default);
  Task ReleaseAsync(string sessionId, string? virtualDisplayId, CancellationToken cancellationToken = default);

  /// <summary>
  /// Polls the given process for its main window handle, retrying briefly
  /// while the process finishes creating its window.
  /// </summary>
  /// <returns>Window handle formatted as "0x{hex}", or null if unresolved.</returns>
  Task<string?> ResolveWindowHandleAsync(int? processId, CancellationToken cancellationToken = default);

  /// <summary>
  /// Applies the monitor-assignment strategy for <paramref name="virtualDisplayId"/>
  /// (parsing its assigned slot/monitor token and resolving the concrete
  /// monitor rect) and repositions the process's window onto that monitor.
  /// No-ops (returning <paramref name="windowHandle"/> unchanged) when not on
  /// Windows, when no virtual display was assigned, or when the target
  /// monitor cannot be resolved.
  /// </summary>
  /// <returns>The last resolved window handle, formatted as "0x{hex}".</returns>
  Task<string?> PlaceWindowAsync(int? processId, string? windowHandle, string? virtualDisplayId, CancellationToken cancellationToken = default);
}
