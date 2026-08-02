using Microsoft.Extensions.Logging;
using NetBox.Adapters.Xenia;
using NetBox.Core.Abstractions;
using NetBox.Models;

namespace NetBox.Core.Services;

public sealed class StreamManager : IStreamManager
{
  private const int StreamReadinessPollAttempts = 6;
  private const int StreamReadinessPollDelayMs = 500;
  private const int StreamReconnectAttempts = 2;

  private readonly ICloudMorphAdapter cloudMorphAdapter;
  private readonly ILogger<StreamManager> logger;

  public StreamManager(ICloudMorphAdapter cloudMorphAdapter, ILogger<StreamManager> logger)
  {
    this.cloudMorphAdapter = cloudMorphAdapter;
    this.logger = logger;
  }

  public async Task<(CloudStreamStartResult Stream, CloudMorphStreamStatus Status)> StartAndWaitForHealthyAsync(
    string sessionId,
    string gameId,
    string gameTitle,
    string captureMode,
    string targetWindowTitle,
    string? audioInputDevice,
    CancellationToken cancellationToken = default)
  {
    var stream = await cloudMorphAdapter.StartStreamAsync(
      sessionId,
      gameId,
      gameTitle,
      captureMode,
      targetWindowTitle,
      audioInputDevice,
      cancellationToken).ConfigureAwait(false);

    return await EnsureHealthyAsync(sessionId, gameId, gameTitle, stream, captureMode, targetWindowTitle, audioInputDevice, cancellationToken).ConfigureAwait(false);
  }

  public async Task<(CloudStreamStartResult Stream, CloudMorphStreamStatus Status)> EnsureHealthyAsync(
    string sessionId,
    string gameId,
    string gameTitle,
    CloudStreamStartResult initialStream,
    string captureMode,
    string targetWindowTitle,
    string? audioInputDevice,
    CancellationToken cancellationToken = default)
  {
    var stream = initialStream;
    CloudMorphStreamStatus latestStatus = new(stream.CloudMorphSessionId, "unknown", "no-status-yet");

    for (var reconnectAttempt = 0; reconnectAttempt <= StreamReconnectAttempts; reconnectAttempt++)
    {
      latestStatus = await WaitForHealthyStatusAsync(stream.CloudMorphSessionId, cancellationToken).ConfigureAwait(false);
      if (IsHealthy(latestStatus.Status))
      {
        var controllerStatus = latestStatus.Status.Equals("live", StringComparison.OrdinalIgnoreCase) ? "game" : latestStatus.Status;
        return (stream with { ControllerStatus = controllerStatus }, latestStatus);
      }

      if (reconnectAttempt == StreamReconnectAttempts)
      {
        break;
      }

      logger.LogWarning(
        "[session:{SessionId}] Stream health not ready (status={Status}, error={Error}); reconnect attempt {Attempt}/{MaxAttempts}.",
        sessionId,
        latestStatus.Status,
        latestStatus.Error,
        reconnectAttempt + 1,
        StreamReconnectAttempts);

      stream = await cloudMorphAdapter.ReconnectAsync(
        sessionId,
        gameId,
        gameTitle,
        captureMode,
        targetWindowTitle,
        audioInputDevice,
        cancellationToken).ConfigureAwait(false);
    }

    return (stream, latestStatus);
  }

  public Task<CloudMorphStreamStatus> GetStatusAsync(string cloudMorphSessionId, CancellationToken cancellationToken = default)
    => cloudMorphAdapter.GetStatusAsync(cloudMorphSessionId, cancellationToken);

  public Task StopAsync(string cloudMorphSessionId, CancellationToken cancellationToken = default)
    => cloudMorphAdapter.StopStreamAsync(cloudMorphSessionId, cancellationToken);

  public Task DetachPlayerAsync(string cloudMorphSessionId, long userId, CancellationToken cancellationToken = default)
    => cloudMorphAdapter.DetachSessionAsync(cloudMorphSessionId, userId.ToString(), cancellationToken);

  public Task ConnectPlayerAsync(string cloudMorphSessionId, long userId, int controllerSlot, CancellationToken cancellationToken = default)
    => cloudMorphAdapter.ConnectPlayerAsync(cloudMorphSessionId, userId.ToString(), controllerSlot, cancellationToken);

  public bool IsHealthy(string status) => IsHealthyStreamHealth(status);

  public bool IsBroken(string status) => IsBrokenStreamHealth(status);

  private async Task<CloudMorphStreamStatus> WaitForHealthyStatusAsync(string cloudMorphSessionId, CancellationToken cancellationToken)
  {
    CloudMorphStreamStatus latest = new(cloudMorphSessionId, "unknown", "no-status-yet");

    for (var attempt = 1; attempt <= StreamReadinessPollAttempts; attempt++)
    {
      latest = await cloudMorphAdapter.GetStatusAsync(cloudMorphSessionId, cancellationToken).ConfigureAwait(false);
      if (IsHealthyStreamHealth(latest.Status) || IsBrokenStreamHealth(latest.Status))
      {
        return latest;
      }

      if (attempt < StreamReadinessPollAttempts)
      {
        await Task.Delay(StreamReadinessPollDelayMs, cancellationToken).ConfigureAwait(false);
      }
    }

    return latest;
  }

  private static bool IsHealthyStreamHealth(string status)
    => status.Equals("live", StringComparison.OrdinalIgnoreCase)
       || status.Equals("running", StringComparison.OrdinalIgnoreCase)
       || status.Equals("game", StringComparison.OrdinalIgnoreCase)
       || status.Equals("ready", StringComparison.OrdinalIgnoreCase)
       || status.Equals("connected", StringComparison.OrdinalIgnoreCase);

  private static bool IsBrokenStreamHealth(string status)
    => status.Equals("unknown", StringComparison.OrdinalIgnoreCase)
       || status.Equals("offline", StringComparison.OrdinalIgnoreCase)
       || status.Equals("failed", StringComparison.OrdinalIgnoreCase)
       || status.Equals("stopped", StringComparison.OrdinalIgnoreCase)
       || status.Equals("capture-timeout", StringComparison.OrdinalIgnoreCase);
}
