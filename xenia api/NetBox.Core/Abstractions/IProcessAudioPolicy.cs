namespace NetBox.Core.Abstractions;

public interface IProcessAudioPolicy
{
  /// <returns>
  /// true if the game's local playback session was found and muted; false if
  /// muting was skipped (disabled/non-Windows/no process) or the audio
  /// session could not be located after all detection attempts.
  /// </returns>
  Task<bool> TryApplyGameLocalMuteAsync(int? processId, CancellationToken cancellationToken = default);
}
