using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using XeniaManager.Core.Abstractions;
using XeniaManager.Models;

namespace XeniaManager.Api.Events;

public sealed class BackendEventHub : IBackendEventSink
{
  private readonly ConcurrentDictionary<Guid, Channel<BackendEventDto>> subscribers = new();

  public Task PublishAsync(BackendEventDto evt, CancellationToken cancellationToken = default)
  {
    foreach (var subscriber in subscribers.Values)
    {
      _ = subscriber.Writer.TryWrite(evt);
    }

    return Task.CompletedTask;
  }

  public (Guid id, ChannelReader<BackendEventDto> reader) Subscribe()
  {
    var id = Guid.NewGuid();
    var channel = Channel.CreateUnbounded<BackendEventDto>();
    subscribers[id] = channel;
    return (id, channel.Reader);
  }

  public void Unsubscribe(Guid id)
  {
    if (subscribers.TryRemove(id, out var channel))
    {
      channel.Writer.TryComplete();
    }
  }

  public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
}
