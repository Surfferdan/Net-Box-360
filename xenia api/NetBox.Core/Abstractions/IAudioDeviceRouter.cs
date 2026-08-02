namespace NetBox.Core.Abstractions;

public interface IAudioDeviceRouter
{
  Task<AudioRouteResult> PrepareForSessionAsync(CancellationToken cancellationToken = default);
  Task RestoreAfterSessionAsync(CancellationToken cancellationToken = default);
  string? ResolvePreferredCaptureInputDevice();
}

/// <summary>
/// <paramref name="DegradedReason"/> is null when audio routing is fully
/// healthy (or not requested/applicable). When routing fell back to a
/// degraded state (virtual sink missing, endpoint switch unsupported/failed),
/// it holds a short machine-readable diagnostic code so callers can surface
/// or alert on it without re-deriving the reason from log text.
/// </summary>
public sealed record AudioRouteResult(bool RoutedToVirtualSink, string? CaptureInputDevice, string? DegradedReason = null);
