using NetBox.Models;

namespace NetBox.Core.Abstractions;

/// <summary>
/// Owns the CloudMorph media-plane bridge: starting/reconnecting streams,
/// waiting for stream health, and stopping/detaching. Used by
/// <see cref="IRuntimeManager"/> so the orchestrator does not talk to
/// the CloudMorph adapter directly.
/// </summary>
public interface IStreamManager
{
  Task<(CloudStreamStartResult Stream, CloudMorphStreamStatus Status)> StartAndWaitForHealthyAsync(
    string sessionId,
    string gameId,
    string gameTitle,
    string captureMode,
    string targetWindowTitle,
    string? audioInputDevice,
    CancellationToken cancellationToken = default);

  Task<(CloudStreamStartResult Stream, CloudMorphStreamStatus Status)> EnsureHealthyAsync(
    string sessionId,
    string gameId,
    string gameTitle,
    CloudStreamStartResult initialStream,
    string captureMode,
    string targetWindowTitle,
    string? audioInputDevice,
    CancellationToken cancellationToken = default);

  Task<CloudMorphStreamStatus> GetStatusAsync(string cloudMorphSessionId, CancellationToken cancellationToken = default);
  Task StopAsync(string cloudMorphSessionId, CancellationToken cancellationToken = default);
  Task DetachPlayerAsync(string cloudMorphSessionId, long userId, CancellationToken cancellationToken = default);
  Task ConnectPlayerAsync(string cloudMorphSessionId, long userId, int controllerSlot, CancellationToken cancellationToken = default);

  bool IsHealthy(string status);
  bool IsBroken(string status);
}
