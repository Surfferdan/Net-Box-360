namespace NetBox.Core.Abstractions;

/// <summary>
/// Owns audio routing policy for a session: preparing/restoring the audio
/// route and applying local mute to the game process. Used by
/// <see cref="IRuntimeManager"/> so the orchestrator does not talk to
/// <see cref="IAudioDeviceRouter"/> or <see cref="IProcessAudioPolicy"/> directly.
/// </summary>
public interface IAudioManager
{
  Task<AudioRouteResult> PrepareAsync(CancellationToken cancellationToken = default);
  Task RestoreAsync(CancellationToken cancellationToken = default);

  /// <returns>true if the game's local playback was found and muted, false otherwise.</returns>
  Task<bool> ApplyGameLocalMuteAsync(int? processId, CancellationToken cancellationToken = default);
  string? ResolvePreferredCaptureInputDevice();
}
