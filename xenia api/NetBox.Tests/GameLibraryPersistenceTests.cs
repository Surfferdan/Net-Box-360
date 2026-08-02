using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NetBox.Models;
using Xunit;
using XeniaManager.Api;

namespace NetBox.Tests;

public sealed class GameLibraryPersistenceTests : IClassFixture<WebApplicationFactory<Program>>
{
  private readonly WebApplicationFactory<Program> factory;

  public GameLibraryPersistenceTests(WebApplicationFactory<Program> factory)
  {
    this.factory = factory.WithWebHostBuilder(builder =>
    {
      builder.ConfigureServices(services =>
      {
        services.AddSingleton<NetBox.Data.Repositories.INetBoxRepository, TestNetBoxRepository>();
      });
    });
  }

  [Fact]
  public async Task RefreshEndpoint_PersistsCatalogSnapshotAndLastPlayed()
  {
    using var client = factory.CreateClient();
    var repository = factory.Services.GetRequiredService<NetBox.Data.Repositories.INetBoxRepository>();

    var response = await client.PostAsJsonAsync("/api/games/refresh", new
    {
      games = new[]
      {
        new { id = "halo4", titleId = "halo-4", title = "Halo 4", relativePath = "halo4.iso", fullPath = "C:/games/halo4.iso", extension = ".iso", sizeBytes = 2048, genre = "Action", players = 1, lastWriteTimeUtc = DateTimeOffset.UtcNow.ToString("O") }
      }
    });

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);

    var catalog = await repository.ListGameCatalogAsync();
    Assert.Single(catalog);
    Assert.Equal("halo4", catalog[0].Id);

    await repository.UpdateLastPlayedAsync("halo4", DateTimeOffset.UtcNow);

    var refreshed = await repository.GetGameCatalogEntryAsync("halo4");
    Assert.NotNull(refreshed);
    Assert.Equal("Halo 4", refreshed!.Title);
    Assert.True(refreshed.LastPlayedAt.HasValue);
  }
}
