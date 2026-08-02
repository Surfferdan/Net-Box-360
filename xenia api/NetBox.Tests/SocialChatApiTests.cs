using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetBox.Adapters.Xenia;
using NetBox.Data.Repositories;
using NetBox.Models;
using Xunit;
using XeniaManager.Api;

namespace NetBox.Tests;

public sealed class SocialChatApiTests : IClassFixture<WebApplicationFactory<Program>>
{
  private readonly WebApplicationFactory<Program> factory;

  public SocialChatApiTests(WebApplicationFactory<Program> factory)
  {
    this.factory = factory.WithWebHostBuilder(builder =>
    {
      builder.ConfigureServices(services =>
      {
        services.RemoveAll<IXeniaProfileGateway>();
        services.AddSingleton<INetBoxRepository, TestNetBoxRepository>();
        services.AddSingleton<IXeniaProfileGateway, SocialTestProfileGateway>();
      });
    });
  }

  [Fact]
  public async Task ChatSendAndReadBack_WorksForAuthedUser()
  {
    using var client = factory.CreateClient();

    var createResponse = await client.PostAsJsonAsync("/api/account/create", new CreateAccountRequest("chat-user", "Password123!", "ChatUser"));
    Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);

    var loginResponse = await client.PostAsJsonAsync("/api/login", new LoginRequest("chat-user", "Password123!"));
    Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
    var login = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
    Assert.NotNull(login);

    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);

    var sendResponse = await client.PostAsJsonAsync("/api/netbox/social/chat", new { message = "Hello from dashboard chat" });
    Assert.Equal(HttpStatusCode.OK, sendResponse.StatusCode);

    var sent = await sendResponse.Content.ReadFromJsonAsync<ChatMessageDto>();
    Assert.NotNull(sent);
    Assert.Equal("Hello from dashboard chat", sent.Message);
    Assert.True(sent.IsMine);

    var listResponse = await client.GetAsync("/api/netbox/social/chat?limit=25");
    Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

    var messages = await listResponse.Content.ReadFromJsonAsync<List<ChatMessageDto>>();
    Assert.NotNull(messages);
    Assert.Contains(messages, message => message.Message == "Hello from dashboard chat" && message.IsMine);
  }

  [Fact]
  public async Task ChatEndpoints_RequireAuth()
  {
    using var client = factory.CreateClient();

    var listResponse = await client.GetAsync("/api/netbox/social/chat");
    Assert.Equal(HttpStatusCode.Unauthorized, listResponse.StatusCode);

    var sendResponse = await client.PostAsJsonAsync("/api/netbox/social/chat", new { message = "No auth" });
    Assert.Equal(HttpStatusCode.Unauthorized, sendResponse.StatusCode);
  }

  [Fact]
  public async Task FriendLifecycle_AndDirectMessagePermissions_Work()
  {
    using var aliceClient = factory.CreateClient();
    using var bobClient = factory.CreateClient();
    var repository = factory.Services.GetRequiredService<INetBoxRepository>();

    var createAlice = await aliceClient.PostAsJsonAsync("/api/account/create", new CreateAccountRequest("alice", "Password123!", "Alice"));
    Assert.Equal(HttpStatusCode.OK, createAlice.StatusCode);

    var createBob = await bobClient.PostAsJsonAsync("/api/account/create", new CreateAccountRequest("bob", "Password123!", "Bob"));
    Assert.Equal(HttpStatusCode.OK, createBob.StatusCode);

    var loginAliceResponse = await aliceClient.PostAsJsonAsync("/api/login", new LoginRequest("alice", "Password123!"));
    Assert.Equal(HttpStatusCode.OK, loginAliceResponse.StatusCode);
    var loginAlice = await loginAliceResponse.Content.ReadFromJsonAsync<LoginResponse>();
    Assert.NotNull(loginAlice);

    var loginBobResponse = await bobClient.PostAsJsonAsync("/api/login", new LoginRequest("bob", "Password123!"));
    Assert.Equal(HttpStatusCode.OK, loginBobResponse.StatusCode);
    var loginBob = await loginBobResponse.Content.ReadFromJsonAsync<LoginResponse>();
    Assert.NotNull(loginBob);

    aliceClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginAlice.Token);
    bobClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginBob.Token);

    var bobUser = await repository.GetUserByUsernameAsync("bob");
    Assert.NotNull(bobUser);

    var sendBeforeFriendResponse = await aliceClient.PostAsJsonAsync("/api/netbox/social/chat", new
    {
      message = "Private hello",
      recipientUserId = bobUser.Id.ToString()
    });
    Assert.Equal(HttpStatusCode.Forbidden, sendBeforeFriendResponse.StatusCode);

    var addFriendResponse = await aliceClient.PostAsJsonAsync("/api/netbox/social/friends", new { username = "bob" });
    Assert.Equal(HttpStatusCode.OK, addFriendResponse.StatusCode);

    var feedResponse = await aliceClient.GetAsync("/api/netbox/social/feed");
    Assert.Equal(HttpStatusCode.OK, feedResponse.StatusCode);
    var feed = await feedResponse.Content.ReadFromJsonAsync<SocialFeedDto>();
    Assert.NotNull(feed);
    Assert.Contains(feed.Friends, friend => friend.Gamertag.Contains("bob", StringComparison.OrdinalIgnoreCase) || friend.Gamertag.Contains("Bob", StringComparison.OrdinalIgnoreCase));

    var sendAfterFriendResponse = await aliceClient.PostAsJsonAsync("/api/netbox/social/chat", new
    {
      message = "Private hello",
      recipientUserId = bobUser.Id.ToString()
    });
    Assert.Equal(HttpStatusCode.OK, sendAfterFriendResponse.StatusCode);

    var removeFriendResponse = await aliceClient.DeleteAsync($"/api/netbox/social/friends/{bobUser.Id}");
    Assert.Equal(HttpStatusCode.OK, removeFriendResponse.StatusCode);

    var sendAfterRemoveResponse = await aliceClient.PostAsJsonAsync("/api/netbox/social/chat", new
    {
      message = "Private hello again",
      recipientUserId = bobUser.Id.ToString()
    });
    Assert.Equal(HttpStatusCode.Forbidden, sendAfterRemoveResponse.StatusCode);
  }

  private sealed record ChatMessageDto(string Id, string FromGamertag, string? ToGamertag, string Message, DateTimeOffset SentAtUtc, bool IsMine);

  private sealed record SocialFeedDto(IReadOnlyList<FriendDto> Friends, IReadOnlyList<ActivityItemDto> Activity);

  private sealed record FriendDto(string Id, string Gamertag, string Subtitle, string Status, string AvatarPath);

  private sealed record ActivityItemDto(string Id, string Text);

  private sealed class SocialTestProfileGateway : IXeniaProfileGateway
  {
    private long nextId = 1;
    private readonly Dictionary<string, NetBoxXeniaProfileDto> profiles = new(StringComparer.OrdinalIgnoreCase);

    public Task<NetBoxXeniaProfileDto> CreateProfileAsync(string gamertag, CancellationToken cancellationToken = default)
    {
      var id = $"social-profile-{nextId++}";
      var profile = new NetBoxXeniaProfileDto(id, gamertag, 0, null, Array.Empty<string>(), Array.Empty<NetBoxAchievementDto>());
      profiles[id] = profile;
      return Task.FromResult(profile);
    }

    public Task<NetBoxXeniaProfileDto?> GetProfileAsync(string profileId, CancellationToken cancellationToken = default)
      => Task.FromResult(profiles.TryGetValue(profileId, out var profile) ? profile : null);

    public Task<IReadOnlyList<NetBoxAchievementDto>> GetAchievementsAsync(string profileId, CancellationToken cancellationToken = default)
      => Task.FromResult<IReadOnlyList<NetBoxAchievementDto>>(Array.Empty<NetBoxAchievementDto>());
  }
}
