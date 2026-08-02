using Microsoft.Extensions.Logging;
using NetBox.Core.Abstractions;

namespace NetBox.Core.Services;

/// <summary>
/// Basic virtual display provider implementation.
/// Logs display lifecycle events and manages display tracking.
/// 
/// In the future, this can be extended to use:
/// - Windows virtual display drivers (e.g., IddSampleDriver)
/// - Custom display provisioning via CloudMorph
/// - NVIDIA Virtual Display Driver or similar solutions
/// </summary>
public sealed class BasicVirtualDisplayProvider : IVirtualDisplayProvider
{
  private readonly ILogger<BasicVirtualDisplayProvider> logger;
  private readonly Dictionary<string, DisplayInfo> activeDisplays = new();
  private readonly object mutex = new();

  private sealed record DisplayInfo(
    string DisplayId,
    string SessionId,
    string GameTitle,
    DateTimeOffset CreatedAt,
    string Status);

  public BasicVirtualDisplayProvider(ILogger<BasicVirtualDisplayProvider> logger)
  {
    this.logger = logger;
  }

  public Task<string?> ProvisionDisplayAsync(string sessionId, string gameTitle, CancellationToken cancellationToken = default)
  {
    if (string.IsNullOrWhiteSpace(sessionId))
    {
      logger.LogError("[VirtualDisplay] ProvisionDisplayAsync: Invalid sessionId provided");
      return Task.FromResult<string?>(null);
    }

    lock (mutex)
    {
      // Generate a display ID (format: display-{sessionId}-{timestamp})
      var displayId = $"display-{sessionId.Substring(0, Math.Min(8, sessionId.Length))}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

      try
      {
        activeDisplays[displayId] = new DisplayInfo(displayId, sessionId, gameTitle, DateTimeOffset.UtcNow, "active");

        logger.LogInformation(
          "[VirtualDisplay] Display provisioned: displayId={DisplayId}, sessionId={SessionId}, gameTitle={GameTitle}",
          displayId,
          sessionId,
          gameTitle);

        return Task.FromResult<string?>(displayId);
      }
      catch (Exception ex)
      {
        logger.LogError(
          ex,
          "[VirtualDisplay] Failed to provision display for sessionId={SessionId}, gameTitle={GameTitle}",
          sessionId,
          gameTitle);
        return Task.FromResult<string?>(null);
      }
    }
  }

  public Task ReleaseDisplayAsync(string sessionId, string? virtualDisplayId, CancellationToken cancellationToken = default)
  {
    if (string.IsNullOrWhiteSpace(virtualDisplayId))
    {
      logger.LogDebug("[VirtualDisplay] ReleaseDisplayAsync: No display ID to release for sessionId={SessionId}", sessionId);
      return Task.CompletedTask;
    }

    lock (mutex)
    {
      try
      {
        if (activeDisplays.TryGetValue(virtualDisplayId, out var display))
        {
          activeDisplays.Remove(virtualDisplayId);

          logger.LogInformation(
            "[VirtualDisplay] Display released: displayId={DisplayId}, sessionId={SessionId}, gameTitle={GameTitle}, lifetime={LifetimeSeconds}s",
            virtualDisplayId,
            display.SessionId,
            display.GameTitle,
            (int)(DateTimeOffset.UtcNow - display.CreatedAt).TotalSeconds);
        }
        else
        {
          logger.LogWarning(
            "[VirtualDisplay] Attempted to release unknown display: displayId={DisplayId}, sessionId={SessionId}",
            virtualDisplayId,
            sessionId);
        }
      }
      catch (Exception ex)
      {
        logger.LogError(
          ex,
          "[VirtualDisplay] Failed to release display: displayId={DisplayId}, sessionId={SessionId}",
          virtualDisplayId,
          sessionId);
      }
    }

    return Task.CompletedTask;
  }

  public Task<string> GetDisplayStatusAsync(string? virtualDisplayId, CancellationToken cancellationToken = default)
  {
    if (string.IsNullOrWhiteSpace(virtualDisplayId))
    {
      return Task.FromResult("unknown");
    }

    lock (mutex)
    {
      if (activeDisplays.TryGetValue(virtualDisplayId, out var display))
      {
        return Task.FromResult(display.Status);
      }
    }

    return Task.FromResult("inactive");
  }

  public Task CleanupOrphanedDisplaysAsync(CancellationToken cancellationToken = default)
  {
    lock (mutex)
    {
      try
      {
        var now = DateTimeOffset.UtcNow;
        var orphaned = activeDisplays.Values
          .Where(d => (now - d.CreatedAt).TotalHours > 24) // Displays older than 24 hours are considered orphaned
          .ToList();

        foreach (var display in orphaned)
        {
          activeDisplays.Remove(display.DisplayId);
          logger.LogWarning(
            "[VirtualDisplay] Cleaned up orphaned display: displayId={DisplayId}, sessionId={SessionId}, gameTitle={GameTitle}, age={AgeHours}h",
            display.DisplayId,
            display.SessionId,
            display.GameTitle,
            (int)(now - display.CreatedAt).TotalHours);
        }

        if (activeDisplays.Count > 0)
        {
          logger.LogInformation("[VirtualDisplay] Cleanup complete. Active displays: {Count}", activeDisplays.Count);
        }
      }
      catch (Exception ex)
      {
        logger.LogError(ex, "[VirtualDisplay] Failed to cleanup orphaned displays");
      }
    }

    return Task.CompletedTask;
  }

  /// <summary>
  /// Gets diagnostics info about the current virtual display state.
  /// Useful for debugging and monitoring.
  /// </summary>
  public (int ActiveCount, int TotalCreated) GetDiagnostics()
  {
    lock (mutex)
    {
      return (activeDisplays.Count, activeDisplays.Count); // In future, could track total created
    }
  }
}
