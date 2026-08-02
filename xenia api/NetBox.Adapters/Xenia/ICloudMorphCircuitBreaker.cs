namespace NetBox.Adapters.Xenia;

/// <summary>
/// Tracks CloudMorph streamer reachability so repeated failures stop generating
/// new outbound HTTP attempts (and their associated timeouts) until a cooldown elapses.
/// </summary>
public interface ICloudMorphCircuitBreaker
{
  /// <summary>Current breaker state: "closed", "open", or "half-open".</summary>
  string State { get; }

  /// <summary>Number of consecutive failures observed since the last success.</summary>
  int ConsecutiveFailures { get; }

  /// <summary>Returns true when a call should be attempted (breaker closed or cooldown elapsed).</summary>
  bool ShouldAttempt();

  void RecordSuccess();

  void RecordFailure();
}
