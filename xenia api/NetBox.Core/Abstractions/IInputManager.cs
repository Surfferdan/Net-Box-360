using NetBox.Models;

namespace NetBox.Core.Abstractions;

/// <summary>
/// Owns player-facing session membership: assigning browser controllers to
/// player slots, preventing duplicate assignments, tracking who owns which
/// slot, and detaching players from the stream when they leave.
/// </summary>
public interface IInputManager
{
  Task<JoinGameSessionResponse> JoinAsync(string sessionToken, string sessionId, CancellationToken cancellationToken = default);

  Task<LeaveGameSessionResponse> LeaveAsync(string sessionToken, string sessionId, CancellationToken cancellationToken = default);

  /// <summary>
  /// Validates the session token and confirms the caller is a participant
  /// (owner or joined player) of <paramref name="sessionId"/>, then returns
  /// their assigned NetBox player slot index (0-3, i.e. controller slot - 1)
  /// for the Phase 13 browser-gamepad input bridge. Throws
  /// <see cref="UnauthorizedAccessException"/> if the token is missing/
  /// invalid/expired, or <see cref="InvalidOperationException"/> if the
  /// session doesn't exist or the caller has no assignment in it.
  /// </summary>
  Task<int> ResolvePlayerSlotAsync(string sessionToken, string sessionId, CancellationToken cancellationToken = default);
}
