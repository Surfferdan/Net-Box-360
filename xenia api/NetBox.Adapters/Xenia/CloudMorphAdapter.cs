using System.Net.Http.Json;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetBox.Models;

namespace NetBox.Adapters.Xenia;

/// <summary>
/// Media-plane control client: talks to the CloudMorph streamer's REST control
/// endpoints (health, streams start/stop/status). All calls are timeout-bounded
/// and gated by <see cref="ICloudMorphCircuitBreaker"/> so an unreachable or
/// crash-looping streamer degrades to the fallback page instead of blocking or
/// crashing the API process.
/// </summary>
public sealed class CloudMorphAdapter : ICloudMorphAdapter
{
  private const string FallbackStreamPage = "/stream-unavailable.html";
  private const string DefaultCaptureMode = "desktop";
  private const string DefaultTargetWindowTitle = "Xenia";
  private readonly CloudMorphOptions options;
  private readonly HttpClient httpClient;
  private readonly ICloudMorphWorkerRouter workerRouter;
  private readonly ICloudMorphCircuitBreaker circuitBreaker;
  private readonly ILogger<CloudMorphAdapter> logger;
  private readonly ConcurrentDictionary<string, (string SessionId, string? WorkerUrl)> streamToSession = new(StringComparer.OrdinalIgnoreCase);

  public CloudMorphAdapter(
    HttpClient httpClient,
    IOptions<CloudMorphOptions> options,
    ICloudMorphWorkerRouter workerRouter,
    ICloudMorphCircuitBreaker circuitBreaker,
    ILogger<CloudMorphAdapter> logger)
  {
    this.httpClient = httpClient;
    this.options = options.Value;
    this.workerRouter = workerRouter;
    this.circuitBreaker = circuitBreaker;
    this.logger = logger;
  }

  public Task<CloudStreamStartResult> StartStreamAsync(string sessionId, string gameId, string gameTitle, CancellationToken cancellationToken = default)
    => StartStreamInternalAsync(sessionId, gameId, gameTitle, DefaultCaptureMode, DefaultTargetWindowTitle, null, cancellationToken);

  public Task<CloudStreamStartResult> CreateStreamAsync(
    string sessionId,
    string gameId,
    string gameTitle,
    string? captureMode = null,
    string? targetWindowTitle = null,
    string? audioInputDevice = null,
    CancellationToken cancellationToken = default)
    => StartStreamAsync(sessionId, gameId, gameTitle, captureMode, targetWindowTitle, audioInputDevice, cancellationToken);

  public Task<CloudStreamStartResult> StartStreamAsync(
    string sessionId,
    string gameId,
    string gameTitle,
    string? captureMode = null,
    string? targetWindowTitle = null,
    string? audioInputDevice = null,
    CancellationToken cancellationToken = default)
    => StartStreamInternalAsync(
      sessionId,
      gameId,
      gameTitle,
      string.IsNullOrWhiteSpace(captureMode) ? DefaultCaptureMode : captureMode,
      string.IsNullOrWhiteSpace(targetWindowTitle) ? DefaultTargetWindowTitle : targetWindowTitle,
      audioInputDevice,
      cancellationToken);

  public async Task StopStreamAsync(string cloudMorphSessionId, CancellationToken cancellationToken = default)
  {
    var workerInfo = streamToSession.TryGetValue(cloudMorphSessionId, out var value) ? value : (SessionId: string.Empty, WorkerUrl: null);

    if (!circuitBreaker.ShouldAttempt())
    {
      logger.LogWarning("CloudMorph circuit breaker open; skipping stop call for {StreamId} and releasing local resources only.", cloudMorphSessionId);
      ReleaseLocalState(cloudMorphSessionId, workerInfo.SessionId);
      return;
    }

    var (client, shouldDispose) = CreateClient(workerInfo.WorkerUrl);

    try
    {
      using var cts = CreateTimeoutToken(cancellationToken);
      using var response = await client.PostAsJsonAsync("/streams/stop", new { streamId = cloudMorphSessionId }, cts.Token).ConfigureAwait(false);
      _ = response.IsSuccessStatusCode;
      circuitBreaker.RecordSuccess();
    }
    catch (Exception ex)
    {
      circuitBreaker.RecordFailure();
      logger.LogWarning(ex, "CloudMorph stop call failed for {StreamId}; releasing local resources anyway (best-effort).", cloudMorphSessionId);
    }
    finally
    {
      if (shouldDispose)
      {
        client.Dispose();
      }

      ReleaseLocalState(cloudMorphSessionId, workerInfo.SessionId);
    }
  }

  public Task<CloudStreamStartResult> ReconnectAsync(
    string sessionId,
    string gameId,
    string gameTitle,
    string? captureMode = null,
    string? targetWindowTitle = null,
    string? audioInputDevice = null,
    CancellationToken cancellationToken = default)
    => StartStreamAsync(sessionId, gameId, gameTitle, captureMode, targetWindowTitle, audioInputDevice, cancellationToken);

  public Task AttachSessionAsync(
    string cloudMorphSessionId,
    string userId,
    int controllerSlot,
    CancellationToken cancellationToken = default)
    => ConnectPlayerAsync(cloudMorphSessionId, userId, controllerSlot, cancellationToken);

  public Task DetachSessionAsync(
    string cloudMorphSessionId,
    string userId,
    CancellationToken cancellationToken = default)
    => DisconnectPlayerAsync(cloudMorphSessionId, userId, cancellationToken);

  public async Task<CloudMorphStreamStatus> GetStatusAsync(string cloudMorphSessionId, CancellationToken cancellationToken = default)
  {
    if (!circuitBreaker.ShouldAttempt())
    {
      return new CloudMorphStreamStatus(cloudMorphSessionId, "unknown", "circuit-breaker-open");
    }

    var workerInfo = streamToSession.TryGetValue(cloudMorphSessionId, out var value) ? value : (SessionId: string.Empty, WorkerUrl: null);
    var (client, shouldDispose) = CreateClient(workerInfo.WorkerUrl);

    try
    {
      using var cts = CreateTimeoutToken(cancellationToken);
      var response = await client.GetAsync($"/streams/{Uri.EscapeDataString(cloudMorphSessionId)}/status", cts.Token).ConfigureAwait(false);
      if (!response.IsSuccessStatusCode)
      {
        circuitBreaker.RecordFailure();
        return new CloudMorphStreamStatus(cloudMorphSessionId, "unknown", $"http-{(int)response.StatusCode}");
      }

      var payload = await response.Content.ReadFromJsonAsync<CloudMorphStreamStatusResponse>(cancellationToken: cts.Token).ConfigureAwait(false);
      circuitBreaker.RecordSuccess();

      if (payload is null)
      {
        return new CloudMorphStreamStatus(cloudMorphSessionId, "unknown", "empty-status-payload");
      }

      return new CloudMorphStreamStatus(payload.StreamId, payload.Status ?? "unknown", payload.Error);
    }
    catch (Exception ex)
    {
      circuitBreaker.RecordFailure();
      logger.LogDebug(ex, "CloudMorph status call failed for {StreamId}.", cloudMorphSessionId);
      return new CloudMorphStreamStatus(cloudMorphSessionId, "unknown", ex.Message);
    }
    finally
    {
      if (shouldDispose)
      {
        client.Dispose();
      }
    }
  }

  public async Task<string> GetStreamStatusAsync(string cloudMorphSessionId, CancellationToken cancellationToken = default)
    => (await GetStatusAsync(cloudMorphSessionId, cancellationToken).ConfigureAwait(false)).Status;

  public async Task ConnectPlayerAsync(
    string cloudMorphSessionId,
    string userId,
    int controllerSlot,
    CancellationToken cancellationToken = default)
  {
    if (string.IsNullOrWhiteSpace(cloudMorphSessionId) || string.IsNullOrWhiteSpace(userId) || !circuitBreaker.ShouldAttempt())
    {
      return;
    }

    var workerInfo = streamToSession.TryGetValue(cloudMorphSessionId, out var value) ? value : (SessionId: string.Empty, WorkerUrl: null);
    var (client, shouldDispose) = CreateClient(workerInfo.WorkerUrl);

    try
    {
      using var cts = CreateTimeoutToken(cancellationToken);
      using var response = await client.PostAsJsonAsync(
        $"/streams/{Uri.EscapeDataString(cloudMorphSessionId)}/players/connect",
        new { userId, controllerSlot },
        cts.Token).ConfigureAwait(false);
      _ = response.IsSuccessStatusCode;
      circuitBreaker.RecordSuccess();
    }
    catch (Exception ex)
    {
      circuitBreaker.RecordFailure();
      logger.LogDebug(ex, "CloudMorph connect-player call failed for {StreamId}.", cloudMorphSessionId);
    }
    finally
    {
      if (shouldDispose)
      {
        client.Dispose();
      }
    }
  }

  public async Task DisconnectPlayerAsync(
    string cloudMorphSessionId,
    string userId,
    CancellationToken cancellationToken = default)
  {
    if (string.IsNullOrWhiteSpace(cloudMorphSessionId) || string.IsNullOrWhiteSpace(userId) || !circuitBreaker.ShouldAttempt())
    {
      return;
    }

    var workerInfo = streamToSession.TryGetValue(cloudMorphSessionId, out var value) ? value : (SessionId: string.Empty, WorkerUrl: null);
    var (client, shouldDispose) = CreateClient(workerInfo.WorkerUrl);

    try
    {
      using var cts = CreateTimeoutToken(cancellationToken);
      using var response = await client.PostAsJsonAsync(
        $"/streams/{Uri.EscapeDataString(cloudMorphSessionId)}/players/disconnect",
        new { userId },
        cts.Token).ConfigureAwait(false);
      _ = response.IsSuccessStatusCode;
      circuitBreaker.RecordSuccess();
    }
    catch (Exception ex)
    {
      circuitBreaker.RecordFailure();
      logger.LogDebug(ex, "CloudMorph disconnect-player call failed for {StreamId}.", cloudMorphSessionId);
    }
    finally
    {
      if (shouldDispose)
      {
        client.Dispose();
      }
    }
  }

  public async Task SendInputAsync(
    string cloudMorphSessionId,
    string userId,
    string inputType,
    string payload,
    CancellationToken cancellationToken = default)
  {
    if (string.IsNullOrWhiteSpace(cloudMorphSessionId) || string.IsNullOrWhiteSpace(inputType) || !circuitBreaker.ShouldAttempt())
    {
      return;
    }

    var workerInfo = streamToSession.TryGetValue(cloudMorphSessionId, out var value) ? value : (SessionId: string.Empty, WorkerUrl: null);
    var (client, shouldDispose) = CreateClient(workerInfo.WorkerUrl);

    try
    {
      using var cts = CreateTimeoutToken(cancellationToken);
      using var response = await client.PostAsJsonAsync(
        $"/streams/{Uri.EscapeDataString(cloudMorphSessionId)}/input",
        new { userId, inputType, payload },
        cts.Token).ConfigureAwait(false);
      _ = response.IsSuccessStatusCode;
      circuitBreaker.RecordSuccess();
    }
    catch (Exception ex)
    {
      circuitBreaker.RecordFailure();
      logger.LogDebug(ex, "CloudMorph input relay failed for {StreamId}.", cloudMorphSessionId);
    }
    finally
    {
      if (shouldDispose)
      {
        client.Dispose();
      }
    }
  }

  public async Task<CloudMorphHealthResponse> GetHealthAsync(CancellationToken cancellationToken = default)
  {
    if (!circuitBreaker.ShouldAttempt())
    {
      return new CloudMorphHealthResponse("offline", false, false, 0);
    }

    try
    {
      using var cts = CreateTimeoutToken(cancellationToken);
      var response = await httpClient.GetAsync("/healthz", cts.Token).ConfigureAwait(false);
      if (!response.IsSuccessStatusCode)
      {
        circuitBreaker.RecordFailure();
        return new CloudMorphHealthResponse("unknown", false, false, 0);
      }

      var payload = await response.Content.ReadFromJsonAsync<CloudMorphHealthResponse>(cancellationToken: cts.Token).ConfigureAwait(false);
      circuitBreaker.RecordSuccess();
      return payload ?? new CloudMorphHealthResponse("unknown", false, false, 0);
    }
    catch (Exception ex)
    {
      circuitBreaker.RecordFailure();
      logger.LogDebug(ex, "CloudMorph health check failed.");
      return new CloudMorphHealthResponse("unknown", false, false, 0);
    }
  }

  private async Task<CloudStreamStartResult> StartStreamInternalAsync(
    string sessionId,
    string gameId,
    string gameTitle,
    string captureMode,
    string targetWindowTitle,
    string? audioInputDevice,
    CancellationToken cancellationToken)
  {
    if (!circuitBreaker.ShouldAttempt())
    {
      logger.LogWarning(
        "CloudMorph circuit breaker is {State}; skipping stream start for session {SessionId} and returning fallback immediately.",
        circuitBreaker.State,
        sessionId);
      return BuildFallback(sessionId, gameId);
    }

    var workerUrl = workerRouter.AcquireWorker(sessionId);
    var (client, shouldDispose) = CreateClient(workerUrl);
    var attempts = Math.Max(1, options.StartStreamRetryCount + 1);

    try
    {
      var request = new CloudMorphCreateStreamRequest(sessionId, gameId, gameTitle, captureMode, targetWindowTitle, audioInputDevice);

      for (var attempt = 1; attempt <= attempts; attempt++)
      {
        try
        {
          using var cts = CreateTimeoutToken(cancellationToken);
          using var response = await client.PostAsJsonAsync("/streams/start", request, cts.Token).ConfigureAwait(false);
          if (!response.IsSuccessStatusCode)
          {
            logger.LogWarning(
              "CloudMorph stream start returned {StatusCode} for session {SessionId} (attempt {Attempt}/{Attempts}).",
              (int)response.StatusCode,
              sessionId,
              attempt,
              attempts);
            circuitBreaker.RecordFailure();
            continue;
          }

          var payload = await response.Content.ReadFromJsonAsync<CloudMorphCreateStreamResponse>(cancellationToken: cts.Token).ConfigureAwait(false);
          if (payload is null || string.IsNullOrWhiteSpace(payload.StreamId) || string.IsNullOrWhiteSpace(payload.StreamUrl))
          {
            logger.LogWarning("CloudMorph stream start returned an incomplete payload for session {SessionId}.", sessionId);
            circuitBreaker.RecordFailure();
            continue;
          }

          streamToSession[payload.StreamId] = (sessionId, workerUrl);
          circuitBreaker.RecordSuccess();
          logger.LogInformation(
            "CloudMorph stream started for session {SessionId} -> streamId {StreamId}, status {Status}.",
            sessionId,
            payload.StreamId,
            payload.Status);

          // The Go bridge returns a path relative to its own control-plane
          // base URL (e.g. "/streams/{id}/signal"); resolve it to an absolute
          // ws:// URL here so the browser can open the signaling socket
          // directly against the media plane without hardcoding its address.
          var signalUrl = BuildAbsoluteSignalUrl(workerUrl, payload.StreamUrl);
          return new CloudStreamStartResult(payload.StreamId, signalUrl, string.IsNullOrWhiteSpace(payload.ControllerStatus) ? "connecting" : payload.ControllerStatus);
        }
        catch (Exception ex)
        {
          circuitBreaker.RecordFailure();
          logger.LogWarning(ex, "CloudMorph stream start attempt {Attempt}/{Attempts} failed for session {SessionId}.", attempt, attempts, sessionId);
          if (attempt < attempts)
          {
            await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken).ConfigureAwait(false);
          }
        }
      }

      return BuildFallback(sessionId, gameId);
    }
    finally
    {
      if (shouldDispose)
      {
        client.Dispose();
      }
    }
  }

  private CloudStreamStartResult BuildFallback(string sessionId, string gameId)
  {
    var cloudSessionId = $"cm-{sessionId}";
    streamToSession[cloudSessionId] = (sessionId, null);
    return new CloudStreamStartResult(cloudSessionId, BuildFallbackStreamUrl(sessionId, gameId), "offline");
  }

  private static string BuildFallbackStreamUrl(string sessionId, string gameId)
    => $"{FallbackStreamPage}?sessionId={Uri.EscapeDataString(sessionId)}&gameId={Uri.EscapeDataString(gameId)}";

  /// <summary>
  /// Resolves the media plane's (possibly relative) signal URL into an
  /// absolute ws(s):// URL the browser can connect to directly, using the
  /// worker/base URL that actually served the /streams/start request.
  /// </summary>
  private string BuildAbsoluteSignalUrl(string? workerUrl, string signalUrlFromBridge)
  {
    if (Uri.TryCreate(signalUrlFromBridge, UriKind.Absolute, out var alreadyAbsolute))
    {
      return alreadyAbsolute.ToString();
    }

    var baseUrl = string.IsNullOrWhiteSpace(workerUrl) ? options.BaseUrl : workerUrl;
    if (string.IsNullOrWhiteSpace(baseUrl) || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
    {
      return signalUrlFromBridge;
    }

    var scheme = string.Equals(baseUri.Scheme, "https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws";
    var builder = new UriBuilder(baseUri)
    {
      Scheme = scheme,
      Path = "/" + signalUrlFromBridge.TrimStart('/'),
      Query = string.Empty,
    };

    return builder.Uri.ToString();
  }

  private void ReleaseLocalState(string cloudMorphSessionId, string? routerSessionId)
  {
    if (!string.IsNullOrWhiteSpace(routerSessionId))
    {
      workerRouter.ReleaseWorker(routerSessionId);
    }

    _ = streamToSession.TryRemove(cloudMorphSessionId, out _);
  }

  private CancellationTokenSource CreateTimeoutToken(CancellationToken cancellationToken)
  {
    var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, options.RequestTimeoutSeconds)));
    return cts;
  }

  private (HttpClient Client, bool ShouldDispose) CreateClient(string? workerUrl)
  {
    if (string.IsNullOrWhiteSpace(workerUrl))
    {
      return (httpClient, false);
    }

    var client = new HttpClient
    {
      BaseAddress = new Uri(workerUrl, UriKind.Absolute),
      Timeout = httpClient.Timeout,
    };

    return (client, true);
  }
}
