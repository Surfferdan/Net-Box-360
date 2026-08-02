using NetBox.Models;

namespace NetBox.Core.Abstractions;

public interface IGameSessionService
{
  Task<StartGameSessionResponse> StartAsync(string sessionToken, StartGameSessionRequest request, CancellationToken cancellationToken = default);
  Task<GameSessionStatusResponse?> ReconnectAsync(string sessionToken, CancellationToken cancellationToken = default);
  Task<GameSessionStatusResponse?> GetAsync(string sessionToken, string sessionId, CancellationToken cancellationToken = default);
  Task<StopGameSessionResponse> StopAsync(string sessionToken, string sessionId, CancellationToken cancellationToken = default);
  Task<LeaveGameSessionResponse> LeaveAsync(string sessionToken, string sessionId, CancellationToken cancellationToken = default);
}
