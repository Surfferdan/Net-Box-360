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

namespace NetBox.Tests;

public sealed class SessionJoinFlowTests : IClassFixture<WebApplicationFactory<Program>>
{
  private readonly WebApplicationFactory<Program> factory;

  public SessionJoinFlowTests(WebApplicationFactory<Program> factory)
  {
    this.factory = factory;
  }

  [Fact]
  public async Task Join_AssignsNextFreeSlot_AndPreventsDuplicateAssignment()
  {
    var profileGateway = new FlowProfileGateway();
    var launcher = new FlowGameLauncher();
    var cloudMorph = new FlowCloudMorphAdapter();

    using var testFactory = CreateFactory(profileGateway, launcher, cloudMorph);
    using var ownerClient = testFactory.CreateClient();
    using var guestClient = testFactory.CreateClient();
    using var secondGuestClient = testFactory.CreateClient();

    await CreateAndLoginAsync(ownerClient, "join-owner", "JoinOwner");
    await CreateAndLoginAsync(guestClient, "join-guest", "JoinGuest");
    await CreateAndLoginAsync(secondGuestClient, "join-guest-2", "JoinGuest2");

    var startResponse = await ownerClient.PostAsJsonAsync("/api/session/start", new StartGameSessionRequest("deadspace"));
    Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);
    var started = await startResponse.Content.ReadFromJsonAsync<StartGameSessionResponse>();
    Assert.NotNull(started);

    var joinResponse = await guestClient.PostAsync($"/api/session/{started!.SessionId}/join", null);
    Assert.Equal(HttpStatusCode.OK, joinResponse.StatusCode);
    var joined = await joinResponse.Content.ReadFromJsonAsync<JoinGameSessionResponse>();
    Assert.NotNull(joined);
    Assert.Equal(2, joined!.AssignedControllerSlot);

    // Idempotent rejoin should return the same slot, not a new one.
    var rejoinResponse = await guestClient.PostAsync($"/api/session/{started.SessionId}/join", null);
    Assert.Equal(HttpStatusCode.OK, rejoinResponse.StatusCode);
    var rejoined = await rejoinResponse.Content.ReadFromJsonAsync<JoinGameSessionResponse>();
    Assert.Equal(2, rejoined!.AssignedControllerSlot);

    // A second distinct guest should get the next free slot.
    var secondJoinResponse = await secondGuestClient.PostAsync($"/api/session/{started.SessionId}/join", null);
    Assert.Equal(HttpStatusCode.OK, secondJoinResponse.StatusCode);
    var secondJoined = await secondJoinResponse.Content.ReadFromJsonAsync<JoinGameSessionResponse>();
    Assert.Equal(3, secondJoined!.AssignedControllerSlot);

    // The owner is not a "joinable" participant of their own session.
    var ownerJoinResponse = await ownerClient.PostAsync($"/api/session/{started.SessionId}/join", null);
    Assert.NotEqual(HttpStatusCode.OK, ownerJoinResponse.StatusCode);

    var statusResponse = await ownerClient.GetAsync($"/api/session/{started.SessionId}");
    var status = await statusResponse.Content.ReadFromJsonAsync<GameSessionStatusResponse>();

    // Players includes the owner (slot 1) plus the two guests that joined.
    Assert.Equal(3, status!.Players);
    Assert.Equal(new[] { 1, 2, 3 }, status.OccupiedControllerSlots);
  }

  [Fact]
  public async Task Leave_ReleasesSlot_AllowingReuseByAnotherGuest()
  {
    var profileGateway = new FlowProfileGateway();
    var launcher = new FlowGameLauncher();
    var cloudMorph = new FlowCloudMorphAdapter();

    using var testFactory = CreateFactory(profileGateway, launcher, cloudMorph);
    using var ownerClient = testFactory.CreateClient();
    using var guestClient = testFactory.CreateClient();
    using var secondGuestClient = testFactory.CreateClient();

    await CreateAndLoginAsync(ownerClient, "leave-owner", "LeaveOwner");
    await CreateAndLoginAsync(guestClient, "leave-guest", "LeaveGuest");
    await CreateAndLoginAsync(secondGuestClient, "leave-guest-2", "LeaveGuest2");

    var startResponse = await ownerClient.PostAsJsonAsync("/api/session/start", new StartGameSessionRequest("deadspace"));
    var started = await startResponse.Content.ReadFromJsonAsync<StartGameSessionResponse>();

    var joinResponse = await guestClient.PostAsync($"/api/session/{started!.SessionId}/join", null);
    var joined = await joinResponse.Content.ReadFromJsonAsync<JoinGameSessionResponse>();
    Assert.Equal(2, joined!.AssignedControllerSlot);

    var leaveResponse = await guestClient.PostAsync($"/api/session/{started.SessionId}/leave", null);
    Assert.Equal(HttpStatusCode.OK, leaveResponse.StatusCode);
    var left = await leaveResponse.Content.ReadFromJsonAsync<LeaveGameSessionResponse>();
    Assert.True(left!.Success);

    // The owner (slot 1) remains after the guest leaves.
    Assert.Equal(1, left.PlayersRemaining);
    Assert.Equal(1, cloudMorph.DisconnectPlayerCalls);

    var secondJoinResponse = await secondGuestClient.PostAsync($"/api/session/{started.SessionId}/join", null);
    Assert.Equal(HttpStatusCode.OK, secondJoinResponse.StatusCode);
    var secondJoined = await secondJoinResponse.Content.ReadFromJsonAsync<JoinGameSessionResponse>();
    Assert.Equal(2, secondJoined!.AssignedControllerSlot);
  }

  [Fact]
  public async Task Owner_CannotLeaveOwnSession()
  {
    var profileGateway = new FlowProfileGateway();
    var launcher = new FlowGameLauncher();
    var cloudMorph = new FlowCloudMorphAdapter();

    using var testFactory = CreateFactory(profileGateway, launcher, cloudMorph);
    using var ownerClient = testFactory.CreateClient();

    await CreateAndLoginAsync(ownerClient, "leave-solo-owner", "SoloOwner");

    var startResponse = await ownerClient.PostAsJsonAsync("/api/session/start", new StartGameSessionRequest("deadspace"));
    var started = await startResponse.Content.ReadFromJsonAsync<StartGameSessionResponse>();

    var leaveResponse = await ownerClient.PostAsync($"/api/session/{started!.SessionId}/leave", null);
    Assert.Equal(HttpStatusCode.Forbidden, leaveResponse.StatusCode);
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
