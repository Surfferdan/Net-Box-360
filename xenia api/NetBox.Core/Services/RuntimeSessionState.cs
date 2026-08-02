namespace NetBox.Core.Services;

/// <summary>
/// Explicit runtime process states for a console session, replacing the
/// implicit string comparisons that used to be scattered across
/// <see cref="ConsoleSessionManager"/>, <see cref="RuntimeManager"/>, and
/// <see cref="GameSessionService"/>. The wire/storage representation
/// (the <c>ProcessState</c> string persisted in <c>GameSessions.Status</c>
/// and returned to the frontend) is unchanged - see
/// <see cref="RuntimeSessionStateMachine.ToWireString"/>/<see cref="RuntimeSessionStateMachine.Parse"/>.
/// </summary>
public enum RuntimeSessionState
{
  Pending,
  Launching,
  Running,
  Stopping,
  Stopped,
  Failed,
}

/// <summary>
/// Thrown when a caller attempts to move a console session's runtime state
/// through a transition that is not part of the legal state graph
/// (see <see cref="RuntimeSessionStateMachine"/>).
/// </summary>
public sealed class InvalidRuntimeStateTransitionException : InvalidOperationException
{
  public InvalidRuntimeStateTransitionException(RuntimeSessionState from, RuntimeSessionState to)
    : base($"Invalid runtime state transition: {from} -> {to}.")
  {
    From = from;
    To = to;
  }

  public RuntimeSessionState From { get; }

  public RuntimeSessionState To { get; }
}

/// <summary>
/// Defines the legal runtime session state graph and centralizes the
/// "is this state active/resumable/terminal" checks that used to be
/// duplicated as ad hoc string comparisons.
///
/// State graph:
///   Pending   -> Launching, Failed, Stopping, Stopped
///   Launching -> Running, Failed, Stopping, Stopped
///   Running   -> Stopping, Stopped, Failed
///   Stopping  -> Stopped, Failed
///   Stopped   -> (terminal)
///   Failed    -> (terminal)
///
/// Self-transitions (from == to) are always treated as a no-op and allowed,
/// since several call sites re-apply the current state defensively.
/// </summary>
public static class RuntimeSessionStateMachine
{
  private static readonly IReadOnlyDictionary<RuntimeSessionState, RuntimeSessionState[]> LegalTransitions =
    new Dictionary<RuntimeSessionState, RuntimeSessionState[]>
    {
      [RuntimeSessionState.Pending] = new[] { RuntimeSessionState.Launching, RuntimeSessionState.Failed, RuntimeSessionState.Stopping, RuntimeSessionState.Stopped },
      [RuntimeSessionState.Launching] = new[] { RuntimeSessionState.Running, RuntimeSessionState.Failed, RuntimeSessionState.Stopping, RuntimeSessionState.Stopped },
      [RuntimeSessionState.Running] = new[] { RuntimeSessionState.Stopping, RuntimeSessionState.Stopped, RuntimeSessionState.Failed },
      [RuntimeSessionState.Stopping] = new[] { RuntimeSessionState.Stopped, RuntimeSessionState.Failed },
      [RuntimeSessionState.Stopped] = Array.Empty<RuntimeSessionState>(),
      [RuntimeSessionState.Failed] = Array.Empty<RuntimeSessionState>(),
    };

  /// <summary>
  /// True for states where the session still owns a live (or launching) runtime.
  /// Mirrors the SQL predicate in SqliteNetBoxRepository.GetActiveGameSessionForUserAsync
  /// (Status IN ('pending','launching','running','stopping')) - keep both in sync.
  /// </summary>
  public static bool IsActive(RuntimeSessionState state) =>
    state is RuntimeSessionState.Pending or RuntimeSessionState.Launching or RuntimeSessionState.Running or RuntimeSessionState.Stopping;

  public static bool IsTerminal(RuntimeSessionState state) => !IsActive(state);

  /// <summary>
  /// True only for the states from which an existing stream/launcher can be
  /// reused/reconnected without going through stale-session recovery first.
  /// Deliberately excludes Stopping (a session already tearing down should
  /// never be handed back to a new caller as "resumable").
  /// </summary>
  public static bool CanResumeStream(RuntimeSessionState state) =>
    state is RuntimeSessionState.Pending or RuntimeSessionState.Launching or RuntimeSessionState.Running;

  public static RuntimeSessionState Parse(string status)
  {
    return status?.Trim().ToLowerInvariant() switch
    {
      "pending" => RuntimeSessionState.Pending,
      "launching" => RuntimeSessionState.Launching,
      "running" => RuntimeSessionState.Running,
      "stopping" => RuntimeSessionState.Stopping,
      "stopped" => RuntimeSessionState.Stopped,
      "failed" => RuntimeSessionState.Failed,
      _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown runtime session state."),
    };
  }

  public static string ToWireString(RuntimeSessionState state) => state switch
  {
    RuntimeSessionState.Pending => "pending",
    RuntimeSessionState.Launching => "launching",
    RuntimeSessionState.Running => "running",
    RuntimeSessionState.Stopping => "stopping",
    RuntimeSessionState.Stopped => "stopped",
    RuntimeSessionState.Failed => "failed",
    _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
  };

  /// <summary>
  /// Validates that moving from <paramref name="from"/> to <paramref name="to"/>
  /// is legal. Throws <see cref="InvalidRuntimeStateTransitionException"/> if not.
  /// Same-state transitions are always treated as a no-op and allowed.
  /// </summary>
  public static void EnsureValidTransition(RuntimeSessionState from, RuntimeSessionState to)
  {
    if (from == to)
    {
      return;
    }

    if (!LegalTransitions.TryGetValue(from, out var allowed) || !allowed.Contains(to))
    {
      throw new InvalidRuntimeStateTransitionException(from, to);
    }
  }
}
