using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetBox.Core.Abstractions;
using NetBox.Models;
using XeniaManager.Core.Abstractions;
using XeniaManager.Models;

namespace NetBox.Core.Services;

/// <summary>
/// Orchestrates the runtime lifecycle by coordinating focused sub-managers
/// (launcher, display, audio, stream) rather than talking to adapters
/// directly. RuntimeManager decides "what happens next"; the sub-managers
/// know "how" to do their part.
/// </summary>
public sealed class RuntimeManager : IRuntimeManager
{
  private const string FallbackStreamPage = "/stream-unavailable.html";

  private readonly IConsoleSessionManager consoleSessions;
  private readonly ILauncherManager launcherManager;
  private readonly IDisplayManager displayManager;
  private readonly IAudioManager audioManager;
  private readonly IStreamManager streamManager;
  private readonly IBackendEventSink eventSink;
  private readonly VirtualDisplayOptions virtualDisplayOptions;
  private readonly AudioRoutingOptions audioRoutingOptions;
  private readonly ILogger<RuntimeManager> logger;

  public RuntimeManager(
    IConsoleSessionManager consoleSessions,
    ILauncherManager launcherManager,
    IDisplayManager displayManager,
    IAudioManager audioManager,
    IStreamManager streamManager,
    IBackendEventSink eventSink,
    IOptions<VirtualDisplayOptions> virtualDisplayOptions,
    IOptions<AudioRoutingOptions> audioRoutingOptions,
    ILogger<RuntimeManager> logger)
  {
    this.consoleSessions = consoleSessions;
    this.launcherManager = launcherManager;
    this.displayManager = displayManager;
    this.audioManager = audioManager;
    this.streamManager = streamManager;
    this.eventSink = eventSink;
    this.virtualDisplayOptions = virtualDisplayOptions.Value;
    this.audioRoutingOptions = audioRoutingOptions.Value;
    this.logger = logger;
  }

  public Task<bool> IsLauncherRunningAsync(CancellationToken cancellationToken = default)
    => launcherManager.IsRunningAsync(cancellationToken);

  public async Task<RuntimeStartResult> StartRuntimeAsync(ConsoleSession session, CancellationToken cancellationToken = default)
  {
    await consoleSessions.MarkLaunchingAsync(session.SessionId, cancellationToken).ConfigureAwait(false);
    logger.LogInformation("[session:{SessionId}] Transitioned to launching state.", session.SessionId);

    var audioRoute = await audioManager.PrepareAsync(cancellationToken).ConfigureAwait(false);
    if (audioRoute.RoutedToVirtualSink)
    {
      logger.LogInformation("[session:{SessionId}] Audio route prepared: virtual sink active, captureInput={CaptureInput}.", session.SessionId, audioRoute.CaptureInputDevice ?? "default");
    }
    else if (!string.IsNullOrWhiteSpace(audioRoute.CaptureInputDevice))
    {
      logger.LogInformation("[session:{SessionId}] Audio capture input override active: {CaptureInput}.", session.SessionId, audioRoute.CaptureInputDevice);
    }

    if (!string.IsNullOrWhiteSpace(audioRoute.DegradedReason))
    {
      await PublishAudioEventAsync("AudioRouteDegraded", session.SessionId, audioRoute.DegradedReason, cancellationToken).ConfigureAwait(false);
    }

    var displayId = await displayManager.ProvisionAsync(session.SessionId, session.GameTitle, cancellationToken).ConfigureAwait(false);
    if (!string.IsNullOrWhiteSpace(displayId))
    {
      await consoleSessions.SetVirtualDisplayIdAsync(session.SessionId, displayId, cancellationToken).ConfigureAwait(false);
      logger.LogInformation("[session:{SessionId}] Virtual display provisioned: displayId={DisplayId}.", session.SessionId, displayId);
    }
    else
    {
      logger.LogWarning("[session:{SessionId}] Virtual display provisioning failed or not available.", session.SessionId);

      if (virtualDisplayOptions.Enabled && virtualDisplayOptions.RequireService && !virtualDisplayOptions.UseSyntheticFallback)
      {
        throw new InvalidOperationException(
          "Virtual display provisioning is required but unavailable. Start the API as Administrator and verify the VirtualDrivers VDD service is installed and healthy.");
      }
    }

    var launchRuntime = await launcherManager.LaunchAsync(session.LaunchPath, displayId, cancellationToken).ConfigureAwait(false);
    if (!string.IsNullOrWhiteSpace(launchRuntime.WindowHandle))
    {
      await consoleSessions.SetWindowHandleAsync(session.SessionId, launchRuntime.WindowHandle, cancellationToken).ConfigureAwait(false);
      logger.LogInformation(
        "[session:{SessionId}] Xenia window captured: processId={ProcessId}, windowHandle={WindowHandle}, displayId={DisplayId}.",
        session.SessionId,
        launchRuntime.ProcessId,
        launchRuntime.WindowHandle,
        displayId);
    }
    else
    {
      logger.LogWarning(
        "[session:{SessionId}] Xenia launched without resolvable window handle: processId={ProcessId}, displayId={DisplayId}.",
        session.SessionId,
        launchRuntime.ProcessId,
        displayId);
    }

    logger.LogInformation("[session:{SessionId}] Launcher reports Xenia running.", session.SessionId);
    var muted = await audioManager.ApplyGameLocalMuteAsync(launchRuntime.ProcessId, cancellationToken).ConfigureAwait(false);
    if (!muted && audioRoutingOptions.MuteHostGamePlayback && launchRuntime.ProcessId is > 0)
    {
      await PublishAudioEventAsync("AudioMuteFailed", session.SessionId, "local-audio-session-not-found", cancellationToken).ConfigureAwait(false);
    }

    CloudStreamStartResult resolvedStream;
    CloudMorphStreamStatus resolvedStatus;
    try
    {
      (resolvedStream, resolvedStatus) = await streamManager.StartAndWaitForHealthyAsync(
        session.SessionId,
        session.GameId,
        session.GameTitle,
        "desktop-audio-session",
        "Xenia",
        audioRoute.CaptureInputDevice,
        cancellationToken).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
      logger.LogWarning(ex, "[session:{SessionId}] Stream bootstrap failed; continuing with fallback URL.", session.SessionId);
      resolvedStream = new CloudStreamStartResult($"cm-{session.SessionId}", BuildFallbackStreamUrl(session.SessionId, session.GameId), "offline");
      resolvedStatus = new CloudMorphStreamStatus(resolvedStream.CloudMorphSessionId, "offline", ex.Message);
    }

    var finalStreamUrl = resolvedStream.StreamUrl;
    if (streamManager.IsBroken(resolvedStatus.Status))
    {
      finalStreamUrl = BuildFallbackStreamUrl(session.SessionId, session.GameId);
      await consoleSessions.MarkStreamUnavailableAsync(session.SessionId, finalStreamUrl, resolvedStatus.Error ?? "Cloud stream endpoint unavailable.", cancellationToken).ConfigureAwait(false);
      await PublishStreamEventAsync("StreamFailed", session.SessionId, resolvedStream.CloudMorphSessionId, resolvedStatus.Status, resolvedStatus.Error, cancellationToken).ConfigureAwait(false);
    }
    else
    {
      await PublishStreamEventAsync("StreamHealthy", session.SessionId, resolvedStream.CloudMorphSessionId, resolvedStatus.Status, null, cancellationToken).ConfigureAwait(false);
    }

    await consoleSessions.MarkRunningAsync(session.SessionId, resolvedStream.CloudMorphSessionId, finalStreamUrl, cancellationToken).ConfigureAwait(false);

    logger.LogInformation(
      "[session:{SessionId}] Stream ready: cloudMorphSessionId={CloudMorphSessionId}, controllerStatus={ControllerStatus}, streamHealth={StreamHealth}.",
      session.SessionId,
      resolvedStream.CloudMorphSessionId,
      resolvedStream.ControllerStatus,
      resolvedStatus.Status);

    return new RuntimeStartResult(
      resolvedStream.CloudMorphSessionId,
      finalStreamUrl,
      resolvedStream.ControllerStatus,
      resolvedStatus.Status);
  }

  public async Task<RuntimeReconnectResult> EnsureSessionRuntimeAsync(ConsoleSession session, CancellationToken cancellationToken = default)
  {
    var controllerStatus = "live";
    var streamUrl = session.StreamUrl;
    var streamHealth = "unknown";
    var streamId = session.CloudMorphSessionId ?? string.Empty;

    if (!string.IsNullOrWhiteSpace(session.CloudMorphSessionId))
    {
      try
      {
        var stream = new CloudStreamStartResult(session.CloudMorphSessionId, session.StreamUrl ?? BuildFallbackStreamUrl(session.SessionId, session.GameId), "connecting");
        var healed = await streamManager.EnsureHealthyAsync(
          session.SessionId,
          session.GameId,
          session.GameTitle,
          stream,
          "desktop-audio-session",
          "Xenia",
          audioManager.ResolvePreferredCaptureInputDevice(),
          cancellationToken).ConfigureAwait(false);

        controllerStatus = healed.Stream.ControllerStatus;
        streamHealth = healed.Status.Status;
        streamUrl = healed.Stream.StreamUrl;
        streamId = healed.Stream.CloudMorphSessionId;

        if (streamManager.IsBroken(streamHealth))
        {
          streamUrl = BuildFallbackStreamUrl(session.SessionId, session.GameId);
          await consoleSessions.MarkStreamUnavailableAsync(session.SessionId, streamUrl, healed.Status.Error ?? "Cloud stream endpoint unavailable.", cancellationToken).ConfigureAwait(false);
          await PublishStreamEventAsync("StreamFailed", session.SessionId, streamId, streamHealth, healed.Status.Error, cancellationToken).ConfigureAwait(false);
        }
        else
        {
          await consoleSessions.UpdateStreamBindingAsync(session.SessionId, healed.Stream.CloudMorphSessionId, streamUrl, null, cancellationToken).ConfigureAwait(false);
          await PublishStreamEventAsync("StreamHealthy", session.SessionId, streamId, streamHealth, null, cancellationToken).ConfigureAwait(false);
        }
      }
      catch (Exception ex)
      {
        streamHealth = "offline";
        controllerStatus = "offline";
        streamUrl = BuildFallbackStreamUrl(session.SessionId, session.GameId);
        await consoleSessions.MarkStreamUnavailableAsync(session.SessionId, streamUrl, ex.Message, cancellationToken).ConfigureAwait(false);
        await PublishStreamEventAsync("StreamFailed", session.SessionId, session.CloudMorphSessionId ?? string.Empty, streamHealth, ex.Message, cancellationToken).ConfigureAwait(false);
        logger.LogWarning(ex, "[session:{SessionId}] Active reconnect failed; returning fallback URL.", session.SessionId);
      }
    }

    return new RuntimeReconnectResult(
      streamId,
      streamUrl ?? BuildFallbackStreamUrl(session.SessionId, session.GameId),
      controllerStatus,
      streamHealth);
  }

  public async Task<CloudMorphStreamStatus> ResolveStreamHealthAsync(ConsoleSession session, CancellationToken cancellationToken = default)
  {
    if (string.IsNullOrWhiteSpace(session.CloudMorphSessionId))
    {
      return new CloudMorphStreamStatus(string.Empty, "offline", session.LastError);
    }

    try
    {
      return await streamManager.GetStatusAsync(session.CloudMorphSessionId, cancellationToken).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
      return new CloudMorphStreamStatus(session.CloudMorphSessionId, "unknown", ex.Message);
    }
  }

  public async Task StopRuntimeAsync(ConsoleSession session, CancellationToken cancellationToken = default)
  {
    if (!string.IsNullOrWhiteSpace(session.CloudMorphSessionId))
    {
      foreach (var assignment in session.ControllerAssignments)
      {
        try
        {
          await streamManager.DetachPlayerAsync(session.CloudMorphSessionId, assignment.UserId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
          logger.LogWarning(ex, "[session:{SessionId}] DisconnectPlayerAsync threw for user {UserId}; continuing stop flow.", session.SessionId, assignment.UserId);
        }
      }

      try
      {
        await streamManager.StopAsync(session.CloudMorphSessionId, cancellationToken).ConfigureAwait(false);
      }
      catch (Exception ex)
      {
        logger.LogWarning(ex, "[session:{SessionId}] StopStreamAsync threw; continuing with launcher stop.", session.SessionId);
      }
    }

    try
    {
      await launcherManager.StopAsync(cancellationToken).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
      logger.LogWarning(ex, "[session:{SessionId}] StopGameAsync threw during session stop.", session.SessionId);
    }

    await CleanupSessionDevicesAsync(session, cancellationToken).ConfigureAwait(false);
  }

  public async Task CleanupFailedStartAsync(string sessionId, CancellationToken cancellationToken = default)
  {
    var failedSession = await consoleSessions.GetBySessionIdAsync(sessionId, cancellationToken).ConfigureAwait(false);
    if (failedSession is not null)
    {
      await CleanupSessionDevicesAsync(failedSession, cancellationToken).ConfigureAwait(false);
    }
  }

  public async Task CleanupStaleRuntimeAsync(ConsoleSession session, CancellationToken cancellationToken = default)
  {
    logger.LogWarning(
      "[session:{SessionId}] Cleaning stale active session (status={Status}).",
      session.SessionId,
      session.ProcessState);

    try
    {
      if (!string.IsNullOrWhiteSpace(session.CloudMorphSessionId))
      {
        await streamManager.StopAsync(session.CloudMorphSessionId, cancellationToken).ConfigureAwait(false);
      }
    }
    catch
    {
      // Best-effort cleanup.
    }

    try
    {
      await launcherManager.StopAsync(cancellationToken).ConfigureAwait(false);
    }
    catch
    {
      // Best-effort cleanup.
    }

    await CleanupSessionDevicesAsync(session, cancellationToken).ConfigureAwait(false);
    await consoleSessions.MarkStaleRecoveredAsync(session.SessionId, "Recovered stale active session.", cancellationToken).ConfigureAwait(false);
  }

  private async Task CleanupSessionDevicesAsync(ConsoleSession session, CancellationToken cancellationToken)
  {
    if (!string.IsNullOrWhiteSpace(session.VirtualDisplayId))
    {
      try
      {
        await displayManager.ReleaseAsync(session.SessionId, session.VirtualDisplayId, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("[session:{SessionId}] Virtual display released: displayId={DisplayId}.", session.SessionId, session.VirtualDisplayId);
      }
      catch (Exception ex)
      {
        logger.LogWarning(ex, "[session:{SessionId}] Virtual display release failed: displayId={DisplayId}.", session.SessionId, session.VirtualDisplayId);
      }
    }

    try
    {
      await audioManager.RestoreAsync(cancellationToken).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
      logger.LogWarning(ex, "[session:{SessionId}] Audio route restore failed.", session.SessionId);
    }
  }

  private Task PublishStreamEventAsync(string type, string sessionId, string cloudMorphSessionId, string streamHealth, string? error, CancellationToken cancellationToken)
  {
    var data = new Dictionary<string, string>
    {
      ["sessionId"] = sessionId,
      ["cloudMorphSessionId"] = cloudMorphSessionId,
      ["streamHealth"] = streamHealth,
    };

    if (error is not null)
    {
      data["error"] = error;
    }

    return eventSink.PublishAsync(new BackendEventDto(type, DateTimeOffset.UtcNow, data), cancellationToken);
  }

  private Task PublishAudioEventAsync(string type, string sessionId, string reason, CancellationToken cancellationToken)
  {
    var data = new Dictionary<string, string>
    {
      ["sessionId"] = sessionId,
      ["reason"] = reason,
    };

    return eventSink.PublishAsync(new BackendEventDto(type, DateTimeOffset.UtcNow, data), cancellationToken);
  }

  private static string BuildFallbackStreamUrl(string sessionId, string gameId)
    => $"{FallbackStreamPage}?sessionId={Uri.EscapeDataString(sessionId)}&gameId={Uri.EscapeDataString(gameId)}";
}
