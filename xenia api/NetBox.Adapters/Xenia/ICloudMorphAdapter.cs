using NetBox.Models;

namespace NetBox.Adapters.Xenia;

public interface ICloudMorphAdapter
{
  Task<CloudStreamStartResult> StartStreamAsync(
    string sessionId,
    string gameId,
    string gameTitle,
    string? captureMode = null,
    string? targetWindowTitle = null,
    string? audioInputDevice = null,
    CancellationToken cancellationToken = default);

  Task<CloudStreamStartResult> CreateStreamAsync(
    string sessionId,
    string gameId,
    string gameTitle,
    string? captureMode = null,
    string? targetWindowTitle = null,
    string? audioInputDevice = null,
    CancellationToken cancellationToken = default);

  Task StopStreamAsync(string cloudMorphSessionId, CancellationToken cancellationToken = default);
  Task<CloudMorphStreamStatus> GetStatusAsync(string cloudMorphSessionId, CancellationToken cancellationToken = default);
  Task<CloudStreamStartResult> ReconnectAsync(
    string sessionId,
    string gameId,
    string gameTitle,
    string? captureMode = null,
    string? targetWindowTitle = null,
    string? audioInputDevice = null,
    CancellationToken cancellationToken = default);

  Task AttachSessionAsync(
    string cloudMorphSessionId,
    string userId,
    int controllerSlot,
    CancellationToken cancellationToken = default);

  Task DetachSessionAsync(
    string cloudMorphSessionId,
    string userId,
    CancellationToken cancellationToken = default);

  Task<string> GetStreamStatusAsync(string cloudMorphSessionId, CancellationToken cancellationToken = default);

  Task ConnectPlayerAsync(
    string cloudMorphSessionId,
    string userId,
    int controllerSlot,
    CancellationToken cancellationToken = default);

  Task DisconnectPlayerAsync(
    string cloudMorphSessionId,
    string userId,
    CancellationToken cancellationToken = default);

  Task SendInputAsync(
    string cloudMorphSessionId,
    string userId,
    string inputType,
    string payload,
    CancellationToken cancellationToken = default);

  Task<CloudMorphHealthResponse> GetHealthAsync(CancellationToken cancellationToken = default);
}
