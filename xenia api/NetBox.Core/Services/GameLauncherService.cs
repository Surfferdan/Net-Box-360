using NetBox.Core.Abstractions;
using NetBox.Adapters.Xenia;
using XeniaManager.Core.Services;
using XeniaManager.Models;

namespace NetBox.Core.Services;

public sealed class GameLauncherService : IGameLauncher
{
  private const string StreamHidArgument = "--hid=winkey";
  private const string NetBoxRuntimeArgument = "--netbox";
  private readonly ILauncherService launcherService;
  private readonly IXeniaGameCatalogGateway gameCatalogGateway;
  private readonly IDisplayManager displayManager;

  public GameLauncherService(ILauncherService launcherService, IXeniaGameCatalogGateway gameCatalogGateway, IDisplayManager displayManager)
  {
    this.launcherService = launcherService;
    this.gameCatalogGateway = gameCatalogGateway;
    this.displayManager = displayManager;
  }

  public async Task<ResolvedGameLaunch> ResolveGameAsync(string gameId, CancellationToken cancellationToken = default)
  {
    var trimmed = gameId?.Trim();
    if (string.IsNullOrWhiteSpace(trimmed))
    {
      throw new ArgumentException("gameId is required.", nameof(gameId));
    }

    var catalog = await gameCatalogGateway.GetGamesAsync(cancellationToken).ConfigureAwait(false);
    var selectedGame = catalog.FirstOrDefault(x => x.Id.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
    if (selectedGame is null)
    {
      throw new InvalidOperationException($"Game '{trimmed}' was not found in catalog.");
    }

    return new ResolvedGameLaunch(selectedGame.Id, selectedGame.Name, selectedGame.FullPath);
  }

  public async Task<bool> IsRunningAsync(CancellationToken cancellationToken = default)
  {
    var status = await launcherService.StatusAsync(cancellationToken).ConfigureAwait(false);
    return status.IsRunning;
  }

  public async Task<GameLaunchRuntime> LaunchGameAsync(string launchPath, string? virtualDisplayId = null, CancellationToken cancellationToken = default)
  {
    var launchArguments = $"{NetBoxRuntimeArgument} {StreamHidArgument} \"{launchPath}\"";
    var launchStatus = await launcherService.StartAsync(
      new LauncherStartRequest(ExecutablePath: null, WorkingDirectory: null, Arguments: launchArguments),
      cancellationToken).ConfigureAwait(false);

    if (!launchStatus.IsRunning)
    {
      throw new InvalidOperationException($"Xenia failed to start. Executable: {launchStatus.ExecutablePath ?? "unknown"}");
    }

    var windowHandle = await displayManager.ResolveWindowHandleAsync(launchStatus.ProcessId, cancellationToken).ConfigureAwait(false);
    var resolvedWindowHandle = await displayManager.PlaceWindowAsync(launchStatus.ProcessId, windowHandle, virtualDisplayId, cancellationToken).ConfigureAwait(false);
    return new GameLaunchRuntime(launchStatus.ProcessId, resolvedWindowHandle ?? windowHandle);
  }

  public async Task StopGameAsync(CancellationToken cancellationToken = default)
  {
    _ = await launcherService.StopAsync(cancellationToken).ConfigureAwait(false);
  }
}
