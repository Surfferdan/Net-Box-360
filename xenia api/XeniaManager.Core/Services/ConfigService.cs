using XeniaManager.Core.Abstractions;
using XeniaManager.Core.Abstractions.Adapters;
using XeniaManager.Models;

namespace XeniaManager.Core.Services;

public sealed class ConfigService : IConfigService
{
  private readonly IXeniaConfigAdapter configAdapter;
  private readonly IBackendEventSink eventSink;

  public ConfigService(IXeniaConfigAdapter configAdapter, IBackendEventSink eventSink)
  {
    this.configAdapter = configAdapter;
    this.eventSink = eventSink;
  }

  public Task<EmulatorConfigDto> GetConfigAsync(CancellationToken cancellationToken = default)
    => configAdapter.GetConfigAsync(cancellationToken);

  public async Task<EmulatorConfigDto> UpdateConfigAsync(UpdateConfigRequest request, CancellationToken cancellationToken = default)
  {
    var saved = await configAdapter.SaveConfigAsync(request, cancellationToken).ConfigureAwait(false);
    await eventSink.PublishAsync(new BackendEventDto(
      "ConfigurationChanged",
      DateTimeOffset.UtcNow,
      new Dictionary<string, string> { ["entries"] = saved.Values.Count.ToString() }), cancellationToken).ConfigureAwait(false);
    return saved;
  }
}
