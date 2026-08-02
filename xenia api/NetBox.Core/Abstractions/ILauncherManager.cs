using NetBox.Models;

namespace NetBox.Core.Abstractions;

/// <summary>
/// Owns the Xenia process lifecycle: launching, checking whether it is
/// running, and stopping it. Used by <see cref="IRuntimeManager"/> so the
/// orchestrator does not talk to <see cref="IGameLauncher"/> directly.
/// </summary>
public interface ILauncherManager
{
  Task<bool> IsRunningAsync(CancellationToken cancellationToken = default);
  Task<GameLaunchRuntime> LaunchAsync(string launchPath, string? virtualDisplayId, CancellationToken cancellationToken = default);
  Task StopAsync(CancellationToken cancellationToken = default);
}
