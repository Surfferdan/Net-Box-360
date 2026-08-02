using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetBox.Adapters.Xenia;
using NetBox.Core.Abstractions;
using NetBox.Models;
using Xunit;
using XeniaManager.Api;
using XeniaManager.Core.Abstractions;
using XeniaManager.Models;

namespace NetBox.Tests;

public sealed class SessionStartFlowTests : IClassFixture<WebApplicationFactory<Program>>
{
  private readonly WebApplicationFactory<Program> factory;

  public SessionStartFlowTests(WebApplicationFactory<Program> factory)
  {
    this.factory = factory;
  }

  [Fact]
  public async Task StartFlow_LoadsProfileThenLaunchesAndExposesStatus()
  {
    var profileGateway = new FlowProfileGateway();
    var launcher = new FlowGameLauncher();
    var cloudMorph = new FlowCloudMorphAdapter();

    using var testFactory = CreateFactory(profileGateway, launcher, cloudMorph);
    using var client = testFactory.CreateClient();

    await CreateAndLoginAsync(client, "flow-user", "FlowUser");

    var startResponse = await client.PostAsJsonAsync("/api/session/start", new StartGameSessionRequest("deadspace"));
    Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);

    var started = await startResponse.Content.ReadFromJsonAsync<StartGameSessionResponse>();
    Assert.NotNull(started);
    Assert.Equal("running", started!.Status);
    Assert.Equal(1, started.AssignedControllerSlot);
    Assert.Equal(1, launcher.LaunchCalls);
    Assert.StartsWith("test-display-", launcher.LastVirtualDisplayId);
    Assert.Equal(1, profileGateway.GetProfileCalls);

    var statusResponse = await client.GetAsync($"/api/session/{started.SessionId}");
    Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);

    var status = await statusResponse.Content.ReadFromJsonAsync<GameSessionStatusResponse>();
    Assert.NotNull(status);
    Assert.Equal("running", status!.Status);
    Assert.Equal(1, status.Players);
    Assert.Equal(1, status.AssignedControllerSlot);
    Assert.Equal("cloud-session", status.CloudMorphSessionId);

    var reconnect = await client.GetAsync("/api/session/active");
    Assert.Equal(HttpStatusCode.OK, reconnect.StatusCode);
  }

  [Fact]
  public async Task StartFlow_ProfileLoadFailure_IsRecoverableOnRetry()
  {
    var profileGateway = new FlowProfileGateway { FailProfileLookup = true };
    var launcher = new FlowGameLauncher();
    var cloudMorph = new FlowCloudMorphAdapter();

    using var testFactory = CreateFactory(profileGateway, launcher, cloudMorph);
    using var client = testFactory.CreateClient();

    await CreateAndLoginAsync(client, "flow-retry", "FlowRetry");

    var failedStart = await client.PostAsJsonAsync("/api/session/start", new StartGameSessionRequest("deadspace"));
    Assert.Equal(HttpStatusCode.Conflict, failedStart.StatusCode);
    Assert.Equal(0, launcher.LaunchCalls);

    profileGateway.FailProfileLookup = false;

    var successfulRetry = await client.PostAsJsonAsync("/api/session/start", new StartGameSessionRequest("deadspace"));
    Assert.Equal(HttpStatusCode.OK, successfulRetry.StatusCode);
    Assert.Equal(1, launcher.LaunchCalls);
  }

  [Fact]
  public async Task Reconnect_WhenLauncherNotRunning_CleansStaleAndAllowsNextStart()
  {
    var profileGateway = new FlowProfileGateway();
    var launcher = new FlowGameLauncher();
    var cloudMorph = new FlowCloudMorphAdapter();

    using var testFactory = CreateFactory(profileGateway, launcher, cloudMorph);
    using var client = testFactory.CreateClient();

    await CreateAndLoginAsync(client, "flow-cleanup", "FlowCleanup");

    var startResponse = await client.PostAsJsonAsync("/api/session/start", new StartGameSessionRequest("deadspace"));
    Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);

    launcher.Running = false;

    var reconnect = await client.GetAsync("/api/session/active");
    Assert.Equal(HttpStatusCode.NotFound, reconnect.StatusCode);
    Assert.Equal(1, launcher.StopCalls);
    Assert.Equal(1, cloudMorph.StopStreamCalls);

    launcher.Running = true;

    var nextStart = await client.PostAsJsonAsync("/api/session/start", new StartGameSessionRequest("deadspace"));
    Assert.Equal(HttpStatusCode.OK, nextStart.StatusCode);
    Assert.Equal(2, launcher.LaunchCalls);
  }

  [Fact]
  public async Task Stop_ReleasesControllerStreamAndLauncher()
  {
    var profileGateway = new FlowProfileGateway();
    var launcher = new FlowGameLauncher();
    var cloudMorph = new FlowCloudMorphAdapter();

    using var testFactory = CreateFactory(profileGateway, launcher, cloudMorph);
    using var client = testFactory.CreateClient();

    await CreateAndLoginAsync(client, "flow-stop", "FlowStop");

    var startResponse = await client.PostAsJsonAsync("/api/session/start", new StartGameSessionRequest("deadspace"));
    Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);
    var started = await startResponse.Content.ReadFromJsonAsync<StartGameSessionResponse>();
    Assert.NotNull(started);

    var stopResponse = await client.PostAsync($"/api/session/{started!.SessionId}/stop", null);
    Assert.Equal(HttpStatusCode.OK, stopResponse.StatusCode);

    Assert.Equal(1, cloudMorph.DisconnectPlayerCalls);
    Assert.Equal(1, cloudMorph.StopStreamCalls);
    Assert.Equal(1, launcher.StopCalls);
  }

  private WebApplicationFactory<Program> CreateFactory(
    FlowProfileGateway profileGateway,
    FlowGameLauncher launcher,
    FlowCloudMorphAdapter cloudMorph)
  {
    return factory.WithWebHostBuilder(builder =>
    {
      builder.ConfigureServices(services =>
      {
        services.RemoveAll<NetBox.Data.Repositories.INetBoxRepository>();
        services.RemoveAll<IXeniaProfileGateway>();
        services.RemoveAll<IGameLauncher>();
        services.RemoveAll<ICloudMorphAdapter>();
        services.RemoveAll<IVirtualDisplayProvider>();

        services.AddSingleton<NetBox.Data.Repositories.INetBoxRepository, TestNetBoxRepository>();
        services.AddSingleton<IXeniaProfileGateway>(profileGateway);
        services.AddSingleton<IGameLauncher>(launcher);
        services.AddSingleton<ICloudMorphAdapter>(cloudMorph);
        services.AddSingleton<IVirtualDisplayProvider, FlowVirtualDisplayProvider>();
      });
    });
  }

  private static async Task CreateAndLoginAsync(HttpClient client, string username, string gamertag)
  {
    var create = await client.PostAsJsonAsync("/api/account/create", new CreateAccountRequest(username, "Password123!", gamertag));
    Assert.Equal(HttpStatusCode.OK, create.StatusCode);

    var login = await client.PostAsJsonAsync("/api/login", new LoginRequest(username, "Password123!"));
    Assert.Equal(HttpStatusCode.OK, login.StatusCode);

    var payload = await login.Content.ReadFromJsonAsync<LoginResponse>();
    Assert.NotNull(payload);
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload!.Token);
  }
}

public sealed class FlowProfileGateway : IXeniaProfileGateway
{
  private long nextId = 1;

  public bool FailProfileLookup { get; set; }
  public int GetProfileCalls { get; private set; }

  public Task<NetBoxXeniaProfileDto> CreateProfileAsync(string gamertag, CancellationToken cancellationToken = default)
  {
    var id = $"profile-{nextId++}";
    return Task.FromResult(new NetBoxXeniaProfileDto(id, gamertag, 0, null, Array.Empty<string>(), Array.Empty<NetBoxAchievementDto>()));
  }

  public Task<NetBoxXeniaProfileDto?> GetProfileAsync(string profileId, CancellationToken cancellationToken = default)
  {
    GetProfileCalls++;
    if (FailProfileLookup)
    {
      return Task.FromResult<NetBoxXeniaProfileDto?>(null);
    }

    return Task.FromResult<NetBoxXeniaProfileDto?>(new NetBoxXeniaProfileDto(profileId, "FlowUser", 0, null, Array.Empty<string>(), Array.Empty<NetBoxAchievementDto>()));
  }

  public Task<IReadOnlyList<NetBoxAchievementDto>> GetAchievementsAsync(string profileId, CancellationToken cancellationToken = default)
    => Task.FromResult<IReadOnlyList<NetBoxAchievementDto>>(Array.Empty<NetBoxAchievementDto>());
}

public sealed class FlowGameLauncher : IGameLauncher
{
  public int LaunchCalls { get; private set; }
  public int StopCalls { get; private set; }
  public string? LastVirtualDisplayId { get; private set; }
  public bool Running { get; set; } = true;

  public Task<ResolvedGameLaunch> ResolveGameAsync(string gameId, CancellationToken cancellationToken = default)
    => Task.FromResult(new ResolvedGameLaunch(gameId, "Dead Space", "C:/games/deadspace.iso"));

  public Task<bool> IsRunningAsync(CancellationToken cancellationToken = default)
    => Task.FromResult(Running);

  public Task<GameLaunchRuntime> LaunchGameAsync(string launchPath, string? virtualDisplayId = null, CancellationToken cancellationToken = default)
  {
    LaunchCalls++;
    LastVirtualDisplayId = virtualDisplayId;
    Running = true;
    return Task.FromResult(new GameLaunchRuntime(4242, "0x00001092"));
  }

  public Task StopGameAsync(CancellationToken cancellationToken = default)
  {
    StopCalls++;
    Running = false;
    return Task.CompletedTask;
  }
}

public sealed class FlowCloudMorphAdapter : ICloudMorphAdapter
{
  public int StopStreamCalls { get; private set; }
  public int DisconnectPlayerCalls { get; private set; }

  public Task<CloudStreamStartResult> StartStreamAsync(string sessionId, string gameId, string gameTitle, string? captureMode = null, string? targetWindowTitle = null, string? audioInputDevice = null, CancellationToken cancellationToken = default)
    => Task.FromResult(new CloudStreamStartResult("cloud-session", "ws://localhost:3000/session", "game"));

  public Task<CloudStreamStartResult> CreateStreamAsync(string sessionId, string gameId, string gameTitle, string? captureMode = null, string? targetWindowTitle = null, string? audioInputDevice = null, CancellationToken cancellationToken = default)
    => StartStreamAsync(sessionId, gameId, gameTitle, captureMode, targetWindowTitle, audioInputDevice, cancellationToken);

  public Task StopStreamAsync(string cloudMorphSessionId, CancellationToken cancellationToken = default)
  {
    StopStreamCalls++;
    return Task.CompletedTask;
  }

  public Task<CloudStreamStartResult> ReconnectAsync(string sessionId, string gameId, string gameTitle, string? captureMode = null, string? targetWindowTitle = null, string? audioInputDevice = null, CancellationToken cancellationToken = default)
    => StartStreamAsync(sessionId, gameId, gameTitle, captureMode, targetWindowTitle, audioInputDevice, cancellationToken);

  public Task AttachSessionAsync(string cloudMorphSessionId, string userId, int controllerSlot, CancellationToken cancellationToken = default)
    => ConnectPlayerAsync(cloudMorphSessionId, userId, controllerSlot, cancellationToken);

  public Task DetachSessionAsync(string cloudMorphSessionId, string userId, CancellationToken cancellationToken = default)
    => DisconnectPlayerAsync(cloudMorphSessionId, userId, cancellationToken);

  public Task<CloudMorphStreamStatus> GetStatusAsync(string cloudMorphSessionId, CancellationToken cancellationToken = default)
    => Task.FromResult(new CloudMorphStreamStatus(cloudMorphSessionId, "game", null));

  public Task<string> GetStreamStatusAsync(string cloudMorphSessionId, CancellationToken cancellationToken = default)
    => Task.FromResult("game");

  public Task ConnectPlayerAsync(string cloudMorphSessionId, string userId, int controllerSlot, CancellationToken cancellationToken = default)
    => Task.CompletedTask;

  public Task DisconnectPlayerAsync(string cloudMorphSessionId, string userId, CancellationToken cancellationToken = default)
  {
    DisconnectPlayerCalls++;
    return Task.CompletedTask;
  }

  public Task SendInputAsync(string cloudMorphSessionId, string userId, string inputType, string payload, CancellationToken cancellationToken = default)
    => Task.CompletedTask;

  public Task<CloudMorphHealthResponse> GetHealthAsync(CancellationToken cancellationToken = default)
    => Task.FromResult(new CloudMorphHealthResponse("ok", true, true, 1));
}

public sealed class FlowVirtualDisplayProvider : IVirtualDisplayProvider
{
  public Task<string?> ProvisionDisplayAsync(string sessionId, string gameTitle, CancellationToken cancellationToken = default)
    => Task.FromResult<string?>($"test-display-{sessionId}");

  public Task ReleaseDisplayAsync(string sessionId, string? virtualDisplayId, CancellationToken cancellationToken = default)
    => Task.CompletedTask;

  public Task<string> GetDisplayStatusAsync(string? virtualDisplayId, CancellationToken cancellationToken = default)
    => Task.FromResult("active");

  public Task CleanupOrphanedDisplaysAsync(CancellationToken cancellationToken = default)
    => Task.CompletedTask;
}
