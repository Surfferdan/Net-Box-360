using NetBox.Core.Abstractions;
using NetBox.Models;

namespace NetBox.Core.Services;

public sealed class LauncherManager : ILauncherManager
{
  private readonly IGameLauncher gameLauncher;

  public LauncherManager(IGameLauncher gameLauncher)
  {
    this.gameLauncher = gameLauncher;
  }

  public async Task<bool> IsRunningAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      return await gameLauncher.IsRunningAsync(cancellationToken).ConfigureAwait(false);
    }
    catch
    {
      return false;
    }
  }

  public Task<GameLaunchRuntime> LaunchAsync(string launchPath, string? virtualDisplayId, CancellationToken cancellationToken = default)
    => gameLauncher.LaunchGameAsync(launchPath, virtualDisplayId, cancellationToken);

  public Task StopAsync(CancellationToken cancellationToken = default)
    => gameLauncher.StopGameAsync(cancellationToken);
}
