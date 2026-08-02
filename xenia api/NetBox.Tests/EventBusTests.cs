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

/// <summary>
/// Verifies the M7 Event Bus milestone: session/player/stream lifecycle
/// events flow through the existing <see cref="IBackendEventSink"/>
/// pub/sub bus (backed by <c>BackendEventHub</c> in the real app), covering
/// event types that were newly wired this milestone (PlayerJoined,
/// PlayerLeft, StreamHealthy, StreamFailed) alongside the pre-existing
/// session lifecycle events (SessionStarted, SessionStopped, etc.).
/// </summary>
public sealed class EventBusTests : IClassFixture<WebApplicationFactory<Program>>
{
  private readonly WebApplicationFactory<Program> factory;

  public EventBusTests(WebApplicationFactory<Program> factory)
  {
    this.factory = factory;
  }

  [Fact]
  public async Task StartSession_PublishesSessionStartedAndStreamHealthyEvents()
  {
    var recorder = new RecordingBackendEventSink();
    using var testFactory = CreateFactory(recorder);
    using var ownerClient = testFactory.CreateClient();

    await CreateAndLoginAsync(ownerClient, "eventbus-owner", "EventBusOwner");

    var startResponse = await ownerClient.PostAsJsonAsync("/api/session/start", new StartGameSessionRequest("deadspace"));
    Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);

    Assert.Contains(recorder.Events, e => e.Type == "SessionStarted");
    Assert.Contains(recorder.Events, e => e.Type == "StreamHealthy");
    Assert.DoesNotContain(recorder.Events, e => e.Type == "StreamFailed");
  }

  [Fact]
  public async Task JoinAndLeave_PublishesPlayerJoinedAndPlayerLeftEvents()
  {
    var recorder = new RecordingBackendEventSink();
    using var testFactory = CreateFactory(recorder);
    using var ownerClient = testFactory.CreateClient();
    using var guestClient = testFactory.CreateClient();

    await CreateAndLoginAsync(ownerClient, "eventbus-join-owner", "EventBusJoinOwner");
    await CreateAndLoginAsync(guestClient, "eventbus-join-guest", "EventBusJoinGuest");

    var startResponse = await ownerClient.PostAsJsonAsync("/api/session/start", new StartGameSessionRequest("deadspace"));
    var started = await startResponse.Content.ReadFromJsonAsync<StartGameSessionResponse>();
    Assert.NotNull(started);

    var joinResponse = await guestClient.PostAsync($"/api/session/{started!.SessionId}/join", null);
    Assert.Equal(HttpStatusCode.OK, joinResponse.StatusCode);

    var joinedEvent = Assert.Single(recorder.Events, e => e.Type == "PlayerJoined");
    Assert.Equal(started.SessionId, joinedEvent.Data["sessionId"]);
    Assert.Equal("2", joinedEvent.Data["controllerSlot"]);

    var leaveResponse = await guestClient.PostAsync($"/api/session/{started.SessionId}/leave", null);
    Assert.Equal(HttpStatusCode.OK, leaveResponse.StatusCode);

    var leftEvent = Assert.Single(recorder.Events, e => e.Type == "PlayerLeft");
    Assert.Equal(started.SessionId, leftEvent.Data["sessionId"]);
    Assert.Equal("2", leftEvent.Data["controllerSlot"]);
  }

  private WebApplicationFactory<Program> CreateFactory(RecordingBackendEventSink recorder)
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
        services.RemoveAll<IBackendEventSink>();

        services.AddSingleton<NetBox.Data.Repositories.INetBoxRepository, TestNetBoxRepository>();
        services.AddSingleton<IXeniaProfileGateway, FlowProfileGateway>();
        services.AddSingleton<IGameLauncher, FlowGameLauncher>();
        services.AddSingleton<ICloudMorphAdapter, FlowCloudMorphAdapter>();
        services.AddSingleton<IVirtualDisplayProvider, FlowVirtualDisplayProvider>();
        services.AddSingleton<IBackendEventSink>(recorder);
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

/// <summary>
/// Test double for <see cref="IBackendEventSink"/> that records every
/// published event in-memory instead of fanning it out to WebSocket
/// subscribers, so tests can assert on exactly what was published.
/// </summary>
public sealed class RecordingBackendEventSink : IBackendEventSink
{
  private readonly List<BackendEventDto> events = new();
  private readonly object gate = new();

  public IReadOnlyList<BackendEventDto> Events
  {
    get
    {
      lock (gate)
      {
        return events.ToArray();
      }
    }
  }

  public Task PublishAsync(BackendEventDto evt, CancellationToken cancellationToken = default)
  {
    lock (gate)
    {
      events.Add(evt);
    }

    return Task.CompletedTask;
  }
}
