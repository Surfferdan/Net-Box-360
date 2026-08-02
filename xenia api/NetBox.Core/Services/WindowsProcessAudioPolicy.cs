using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NAudio.CoreAudioApi;
using NetBox.Core.Abstractions;

namespace NetBox.Core.Services;

public sealed class WindowsProcessAudioPolicy : IProcessAudioPolicy
{
  private readonly AudioRoutingOptions options;
  private readonly ILogger<WindowsProcessAudioPolicy> logger;

  public WindowsProcessAudioPolicy(IOptions<AudioRoutingOptions> options, ILogger<WindowsProcessAudioPolicy> logger)
  {
    this.options = options.Value;
    this.logger = logger;
  }

  public async Task<bool> TryApplyGameLocalMuteAsync(int? processId, CancellationToken cancellationToken = default)
  {
    if (!options.MuteHostGamePlayback)
    {
      return false;
    }

    if (!OperatingSystem.IsWindows() || processId is null || processId <= 0)
    {
      return false;
    }

    var attempts = Math.Max(1, options.SessionDetectAttempts);
    var delayMs = Math.Max(25, options.SessionDetectDelayMs);

    for (var attempt = 1; attempt <= attempts; attempt++)
    {
      cancellationToken.ThrowIfCancellationRequested();

      if (TryMuteProcessSession(processId.Value))
      {
        logger.LogInformation("Muted local audio playback for process {ProcessId}.", processId.Value);
        return true;
      }

      if (attempt < attempts)
      {
        await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
      }
    }

    logger.LogWarning(
      "Could not find an active render audio session for process {ProcessId}; host-side game audio may still be audible.",
      processId.Value);
    return false;
  }

  private static bool TryMuteProcessSession(int processId)
  {
    try
    {
      using var deviceEnumerator = new MMDeviceEnumerator();
      using var renderDevice = deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
      var sessionCollection = renderDevice.AudioSessionManager.Sessions;

      for (var i = 0; i < sessionCollection.Count; i++)
      {
        using var control = sessionCollection[i];
        if (control is null || control.GetProcessID != processId)
        {
          continue;
        }

        var volume = control.SimpleAudioVolume;
        volume.Volume = 0f;
        volume.Mute = true;
        return true;
      }
    }
    catch
    {
      return false;
    }

    return false;
  }
}
