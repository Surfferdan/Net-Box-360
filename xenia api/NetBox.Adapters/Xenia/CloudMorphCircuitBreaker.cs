using Microsoft.Extensions.Options;

namespace NetBox.Adapters.Xenia;

/// <summary>
/// Simple consecutive-failure circuit breaker. Singleton so state is shared across
/// every request-scoped <see cref="CloudMorphAdapter"/> instance created by the HttpClientFactory.
/// </summary>
public sealed class CloudMorphCircuitBreaker : ICloudMorphCircuitBreaker
{
  private readonly object gate = new();
  private readonly int failureThreshold;
  private readonly TimeSpan openDuration;
  private int consecutiveFailures;
  private bool isOpen;
  private DateTimeOffset openedAt;

  public CloudMorphCircuitBreaker(IOptions<CloudMorphOptions> options)
  {
    var value = options.Value;
    failureThreshold = Math.Max(1, value.CircuitBreakerFailureThreshold);
    openDuration = TimeSpan.FromSeconds(Math.Max(1, value.CircuitBreakerOpenSeconds));
  }

  public int ConsecutiveFailures
  {
    get { lock (gate) { return consecutiveFailures; } }
  }

  public string State
  {
    get
    {
      lock (gate)
      {
        if (isOpen)
        {
          return DateTimeOffset.UtcNow - openedAt >= openDuration ? "half-open" : "open";
        }

        return consecutiveFailures > 0 ? "half-open" : "closed";
      }
    }
  }

  public bool ShouldAttempt()
  {
    lock (gate)
    {
      if (!isOpen)
      {
        return true;
      }

      // Cooldown elapsed: allow a single trial ("half-open") request through.
      return DateTimeOffset.UtcNow - openedAt >= openDuration;
    }
  }

  public void RecordSuccess()
  {
    lock (gate)
    {
      consecutiveFailures = 0;
      isOpen = false;
    }
  }

  public void RecordFailure()
  {
    lock (gate)
    {
      consecutiveFailures++;
      if (consecutiveFailures >= failureThreshold)
      {
        isOpen = true;
        openedAt = DateTimeOffset.UtcNow;
      }
    }
  }
}
