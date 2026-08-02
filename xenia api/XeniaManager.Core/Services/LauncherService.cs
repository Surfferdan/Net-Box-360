using XeniaManager.Core.Abstractions;
using XeniaManager.Core.Abstractions.Adapters;
using XeniaManager.Models;

namespace XeniaManager.Core.Services;

public sealed class LauncherService : ILauncherService
{
  private readonly IXeniaLauncherAdapter launcherAdapter;
  private readonly IBackendEventSink eventSink;

  public LauncherService(IXeniaLauncherAdapter launcherAdapter, IBackendEventSink eventSink)
  {
    this.launcherAdapter = launcherAdapter;
    this.eventSink = eventSink;
  }

  public async Task<LauncherStatusDto> StartAsync(LauncherStartRequest request, CancellationToken cancellationToken = default)
  {
    var status = await launcherAdapter.StartAsync(request, cancellationToken).ConfigureAwait(false);
    if (status.IsRunning)
    {
      await eventSink.PublishAsync(new BackendEventDto(
        "XeniaStarted",
        DateTimeOffset.UtcNow,
        new Dictionary<string, string> { ["processId"] = status.ProcessId?.ToString() ?? string.Empty }), cancellationToken).ConfigureAwait(false);
    }
    return status;
  }

  public async Task<LauncherStatusDto> StopAsync(CancellationToken cancellationToken = default)
  {
    var status = await launcherAdapter.StopAsync(cancellationToken).ConfigureAwait(false);
    await eventSink.PublishAsync(new BackendEventDto("XeniaStopped", DateTimeOffset.UtcNow, new Dictionary<string, string>()), cancellationToken).ConfigureAwait(false);
    return status;
  }

  public Task<LauncherStatusDto> StatusAsync(CancellationToken cancellationToken = default)
    => launcherAdapter.GetStatusAsync(cancellationToken);
}
