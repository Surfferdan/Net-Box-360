using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetBox.Adapters.Xenia;
using NetBox.Models;
using Xunit;
using XeniaManager.Api;

namespace NetBox.Tests;

public sealed class AccountApiTests : IClassFixture<WebApplicationFactory<Program>>
{
  private readonly WebApplicationFactory<Program> factory;

  public AccountApiTests(WebApplicationFactory<Program> factory)
  {
    this.factory = factory.WithWebHostBuilder(builder =>
    {
      builder.ConfigureServices(services =>
      {
        services.AddSingleton<NetBox.Data.Repositories.INetBoxRepository, TestNetBoxRepository>();
        services.AddSingleton<IXeniaProfileGateway, TestXeniaProfileGateway>();
      });
    });
  }

  [Fact]
  public async Task CreateLoginLogoutAndProfileFlow_Works()
  {
    using var client = factory.CreateClient();

    var createRequest = new CreateAccountRequest("tester", "Password123!", "TestUser");
    var createResponse = await client.PostAsJsonAsync("/api/account/create", createRequest);
    Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);

    var created = await createResponse.Content.ReadFromJsonAsync<CreateAccountResponse>();
    Assert.NotNull(created);
    Assert.True(created.Success);

    var loginRequest = new LoginRequest("tester", "Password123!");
    var loginResponse = await client.PostAsJsonAsync("/api/login", loginRequest);
    Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
    var login = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
    Assert.NotNull(login);
    Assert.NotNull(login.Token);

    var profileResponse = await client.GetAsync("/api/profile/me");
    Assert.Equal(HttpStatusCode.Unauthorized, profileResponse.StatusCode);

    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
    profileResponse = await client.GetAsync("/api/profile/me");
    Assert.Equal(HttpStatusCode.OK, profileResponse.StatusCode);

    var profile = await profileResponse.Content.ReadFromJsonAsync<CombinedProfileDto>();
    Assert.NotNull(profile);
    Assert.Equal("tester", profile.Username);

    var logoutResponse = await client.PostAsync("/api/logout", null);
    Assert.Equal(HttpStatusCode.OK, logoutResponse.StatusCode);

    profileResponse = await client.GetAsync("/api/profile/me");
    Assert.Equal(HttpStatusCode.Unauthorized, profileResponse.StatusCode);
  }

  [Fact]
  public async Task RefreshSession_RotatesTokenAndInvalidatesOldOne()
  {
    using var client = factory.CreateClient();

    var createResponse = await client.PostAsJsonAsync("/api/account/create", new CreateAccountRequest("refresh-user", "Password123!", "RefreshUser"));
    Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);

    var loginResponse = await client.PostAsJsonAsync("/api/login", new LoginRequest("refresh-user", "Password123!"));
    Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
    var login = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
    Assert.NotNull(login);

    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
    var refreshResponse = await client.PostAsync("/api/refresh", null);
    Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);

    var refreshed = await refreshResponse.Content.ReadFromJsonAsync<LoginResponse>();
    Assert.NotNull(refreshed);
    Assert.NotEqual(login.Token, refreshed.Token);

    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
    var oldTokenProfileResponse = await client.GetAsync("/api/profile/me");
    Assert.Equal(HttpStatusCode.Unauthorized, oldTokenProfileResponse.StatusCode);

    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", refreshed.Token);
    var newTokenProfileResponse = await client.GetAsync("/api/profile/me");
    Assert.Equal(HttpStatusCode.OK, newTokenProfileResponse.StatusCode);
  }

  [Fact]
  public async Task MissingProfileLink_RecreatesAndPersistsProfileLink()
  {
    var gateway = new RepairableXeniaProfileGateway();
    var testFactory = factory.WithWebHostBuilder(builder =>
    {
      builder.ConfigureServices(services =>
      {
        services.RemoveAll<IXeniaProfileGateway>();
        services.AddSingleton<IXeniaProfileGateway>(gateway);
      });
    });

    using var client = testFactory.CreateClient();
    var repository = testFactory.Services.GetRequiredService<NetBox.Data.Repositories.INetBoxRepository>();

    var createResponse = await client.PostAsJsonAsync("/api/account/create", new CreateAccountRequest("link-repair", "Password123!", "LinkRepair"));
    Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);

    var user = await repository.GetUserByUsernameAsync("link-repair");
    Assert.NotNull(user);
    await repository.UpdateXeniaProfileIdAsync(user.Id, "missing-profile");

    var loginResponse = await client.PostAsJsonAsync("/api/login", new LoginRequest("link-repair", "Password123!"));
    Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
    var login = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
    Assert.NotNull(login);

    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
    var profileResponse = await client.GetAsync("/api/profile/me");
    Assert.Equal(HttpStatusCode.OK, profileResponse.StatusCode);

    Assert.Equal(2, gateway.CreatedProfileIds.Count);
    var refreshedUser = await repository.GetUserByUsernameAsync("link-repair");
    Assert.NotNull(refreshedUser);
    Assert.Equal(gateway.CreatedProfileIds[^1], refreshedUser.XeniaProfileId);
  }

  [Fact]
  public async Task DuplicateUsername_ReturnsConflict()
  {
    using var client = factory.CreateClient();

    var first = new CreateAccountRequest("dup", "Password123!", "Alpha");
    var second = new CreateAccountRequest("dup", "Password123!", "Beta");

    var firstResponse = await client.PostAsJsonAsync("/api/account/create", first);
    Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

    var secondResponse = await client.PostAsJsonAsync("/api/account/create", second);
    Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
  }

  [Fact]
  public async Task InvalidLogin_ReturnsUnauthorized()
  {
    using var client = factory.CreateClient();
    var response = await client.PostAsJsonAsync("/api/login", new LoginRequest("missing", "Password123!"));
    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
  }

  [Fact]
  public async Task UpdateProfileCustomization_PersistsAndReturnsUnifiedProfile()
  {
    using var client = factory.CreateClient();

    var createResponse = await client.PostAsJsonAsync("/api/account/create", new CreateAccountRequest("custom-user", "Password123!", "Custom User"));
    Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);

    var loginResponse = await client.PostAsJsonAsync("/api/login", new LoginRequest("custom-user", "Password123!"));
    Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
    var login = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
    Assert.NotNull(login);

    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);

    var updateResponse = await client.PutAsJsonAsync("/api/profile/me/customization", new
    {
      displayName = "GrimPrime",
      motto = "Ready to play",
      cardStyle = "emerald",
      avatarDataUrl = "data:image/png;base64,AAAA"
    });
    Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

    var updated = await updateResponse.Content.ReadFromJsonAsync<CombinedProfileDto>();
    Assert.NotNull(updated);
    Assert.Equal("GrimPrime", updated.DisplayName);
    Assert.Equal("Ready to play", updated.Motto);
    Assert.Equal("emerald", updated.CardStyle);
    Assert.Equal("data:image/png;base64,AAAA", updated.Avatar);

    var meResponse = await client.GetAsync("/api/profile/me");
    Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);

    var me = await meResponse.Content.ReadFromJsonAsync<CombinedProfileDto>();
    Assert.NotNull(me);
    Assert.Equal("GrimPrime", me.DisplayName);
    Assert.Equal("Ready to play", me.Motto);
    Assert.Equal("emerald", me.CardStyle);
  }
}

public sealed class RepairableXeniaProfileGateway : IXeniaProfileGateway
{
  private long nextId = 1;

  public List<string> CreatedProfileIds { get; } = new();

  public Task<NetBoxXeniaProfileDto> CreateProfileAsync(string gamertag, CancellationToken cancellationToken = default)
  {
    var profileId = $"profile-{nextId++}";
    CreatedProfileIds.Add(profileId);
    return Task.FromResult(new NetBoxXeniaProfileDto(profileId, gamertag, 0, null, Array.Empty<string>(), Array.Empty<NetBoxAchievementDto>()));
  }

  public Task<NetBoxXeniaProfileDto?> GetProfileAsync(string profileId, CancellationToken cancellationToken = default)
    => Task.FromResult<NetBoxXeniaProfileDto?>(profileId == "missing-profile" ? null : new NetBoxXeniaProfileDto(profileId, "RepairUser", 0, null, Array.Empty<string>(), Array.Empty<NetBoxAchievementDto>()));

  public Task<IReadOnlyList<NetBoxAchievementDto>> GetAchievementsAsync(string profileId, CancellationToken cancellationToken = default)
    => Task.FromResult<IReadOnlyList<NetBoxAchievementDto>>(Array.Empty<NetBoxAchievementDto>());
}
