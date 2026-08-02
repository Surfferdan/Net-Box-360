using Microsoft.Extensions.Options;

namespace NetBox.Adapters.Xenia;

public sealed class CloudMorphWorkerRouter : ICloudMorphWorkerRouter
{
  private readonly object gate = new();
  private readonly List<string> workerUrls;
  private readonly Dictionary<string, string> sessionToWorker = new(StringComparer.OrdinalIgnoreCase);
  private readonly bool enabled;
  private readonly bool allowReuseWhenExhausted;
  private int nextIndex;

  public CloudMorphWorkerRouter(IOptions<CloudMorphOptions> options)
  {
    var value = options.Value;
    enabled = value.EnableDedicatedWorkers;
    allowReuseWhenExhausted = value.AllowWorkerReuseWhenExhausted;

    workerUrls = (value.DedicatedWorkerUrls ?? Array.Empty<string>())
      .Where(url => Uri.TryCreate(url, UriKind.Absolute, out _))
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .ToList();
  }

  public string? AcquireWorker(string sessionId)
  {
    if (!enabled || workerUrls.Count == 0 || string.IsNullOrWhiteSpace(sessionId))
    {
      return null;
    }

    lock (gate)
    {
      if (sessionToWorker.TryGetValue(sessionId, out var existing))
      {
        return existing;
      }

      var inUse = new HashSet<string>(sessionToWorker.Values, StringComparer.OrdinalIgnoreCase);
      for (var i = 0; i < workerUrls.Count; i++)
      {
        var idx = (nextIndex + i) % workerUrls.Count;
        var candidate = workerUrls[idx];
        if (inUse.Contains(candidate))
        {
          continue;
        }

        nextIndex = (idx + 1) % workerUrls.Count;
        sessionToWorker[sessionId] = candidate;
        return candidate;
      }

      if (!allowReuseWhenExhausted)
      {
        return null;
      }

      var fallback = workerUrls[nextIndex % workerUrls.Count];
      nextIndex = (nextIndex + 1) % workerUrls.Count;
      sessionToWorker[sessionId] = fallback;
      return fallback;
    }
  }

  public void ReleaseWorker(string sessionId)
  {
    if (string.IsNullOrWhiteSpace(sessionId))
    {
      return;
    }

    lock (gate)
    {
      _ = sessionToWorker.Remove(sessionId);
    }
  }
}
