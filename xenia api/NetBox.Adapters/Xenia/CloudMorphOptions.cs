namespace NetBox.Adapters.Xenia;

public sealed class CloudMorphOptions
{
  public string BaseUrl { get; set; } = "http://127.0.0.1:7780";
  public string StreamBaseUrl { get; set; } = "webrtc://127.0.0.1:7777/session";
  public bool EnableDedicatedWorkers { get; set; }
  public bool AllowWorkerReuseWhenExhausted { get; set; } = true;
  public string[] DedicatedWorkerUrls { get; set; } = Array.Empty<string>();

  /// <summary>Per-request timeout applied to every CloudMorph control-plane call.</summary>
  public int RequestTimeoutSeconds { get; set; } = 4;

  /// <summary>Additional attempts made for the stream-start call before falling back.</summary>
  public int StartStreamRetryCount { get; set; } = 1;

  /// <summary>Consecutive failures before the circuit breaker opens and short-circuits calls.</summary>
  public int CircuitBreakerFailureThreshold { get; set; } = 3;

  /// <summary>How long the breaker stays open before allowing a half-open trial call.</summary>
  public int CircuitBreakerOpenSeconds { get; set; } = 15;
}
