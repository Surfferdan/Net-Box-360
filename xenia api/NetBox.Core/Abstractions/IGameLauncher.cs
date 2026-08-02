using NetBox.Models;

namespace NetBox.Core.Abstractions;

public interface IGameLauncher
{
  Task<ResolvedGameLaunch> ResolveGameAsync(string gameId, CancellationToken cancellationToken = default);
  Task<bool> IsRunningAsync(CancellationToken cancellationToken = default);
  Task<GameLaunchRuntime> LaunchGameAsync(string launchPath, string? virtualDisplayId = null, CancellationToken cancellationToken = default);
  Task StopGameAsync(CancellationToken cancellationToken = default);
}

public sealed record ResolvedGameLaunch(string GameId, string GameTitle, string LaunchPath);
public sealed record GameLaunchRuntime(int? ProcessId, string? WindowHandle);
