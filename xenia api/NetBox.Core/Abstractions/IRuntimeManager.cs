using NetBox.Models;

namespace NetBox.Core.Abstractions;

public interface IRuntimeManager
{
  Task<bool> IsLauncherRunningAsync(CancellationToken cancellationToken = default);
  Task<RuntimeStartResult> StartRuntimeAsync(ConsoleSession session, CancellationToken cancellationToken = default);
  Task<RuntimeReconnectResult> EnsureSessionRuntimeAsync(ConsoleSession session, CancellationToken cancellationToken = default);
  Task<CloudMorphStreamStatus> ResolveStreamHealthAsync(ConsoleSession session, CancellationToken cancellationToken = default);
  Task StopRuntimeAsync(ConsoleSession session, CancellationToken cancellationToken = default);
  Task CleanupFailedStartAsync(string sessionId, CancellationToken cancellationToken = default);
  Task CleanupStaleRuntimeAsync(ConsoleSession session, CancellationToken cancellationToken = default);
}

public sealed record RuntimeStartResult(
  string CloudMorphSessionId,
  string StreamUrl,
  string ControllerStatus,
  string StreamHealth);

public sealed record RuntimeReconnectResult(
  string CloudMorphSessionId,
  string StreamUrl,
  string ControllerStatus,
  string StreamHealth);
