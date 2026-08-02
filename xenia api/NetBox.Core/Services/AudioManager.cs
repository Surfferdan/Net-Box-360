using NetBox.Core.Abstractions;

namespace NetBox.Core.Services;

public sealed class AudioManager : IAudioManager
{
  private readonly IAudioDeviceRouter audioDeviceRouter;
  private readonly IProcessAudioPolicy processAudioPolicy;

  public AudioManager(IAudioDeviceRouter audioDeviceRouter, IProcessAudioPolicy processAudioPolicy)
  {
    this.audioDeviceRouter = audioDeviceRouter;
    this.processAudioPolicy = processAudioPolicy;
  }

  public Task<AudioRouteResult> PrepareAsync(CancellationToken cancellationToken = default)
    => audioDeviceRouter.PrepareForSessionAsync(cancellationToken);

  public Task RestoreAsync(CancellationToken cancellationToken = default)
    => audioDeviceRouter.RestoreAfterSessionAsync(cancellationToken);

  public Task<bool> ApplyGameLocalMuteAsync(int? processId, CancellationToken cancellationToken = default)
    => processAudioPolicy.TryApplyGameLocalMuteAsync(processId, cancellationToken);

  public string? ResolvePreferredCaptureInputDevice()
    => audioDeviceRouter.ResolvePreferredCaptureInputDevice();
}
