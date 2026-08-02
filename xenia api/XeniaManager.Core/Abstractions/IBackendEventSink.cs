using XeniaManager.Models;

namespace XeniaManager.Core.Abstractions;

public interface IBackendEventSink
{
  Task PublishAsync(BackendEventDto evt, CancellationToken cancellationToken = default);
}
