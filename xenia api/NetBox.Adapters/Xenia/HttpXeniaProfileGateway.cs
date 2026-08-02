using System.Net.Http.Json;
using NetBox.Models;

namespace NetBox.Adapters.Xenia;

public sealed class HttpXeniaProfileGateway : IXeniaProfileGateway
{
  private readonly HttpClient httpClient;

  public HttpXeniaProfileGateway(HttpClient httpClient)
  {
    this.httpClient = httpClient;
  }

  public async Task<NetBoxXeniaProfileDto> CreateProfileAsync(string gamertag, CancellationToken cancellationToken = default)
  {
    var response = await httpClient.PostAsJsonAsync("/api/profiles", new { gamertag }, cancellationToken).ConfigureAwait(false);
    response.EnsureSuccessStatusCode();
    var dto = await response.Content.ReadFromJsonAsync<XeniaProfilePayload>(cancellationToken: cancellationToken).ConfigureAwait(false)
      ?? throw new InvalidOperationException("Xenia profile create returned empty payload.");
    var achievements = await GetAchievementsAsync(dto.Id, cancellationToken).ConfigureAwait(false);
    return new NetBoxXeniaProfileDto(dto.Id, dto.Gamertag, dto.Gamerscore, dto.AvatarPath, dto.RecentGames?.Select(game => game.Name).ToArray() ?? Array.Empty<string>(), achievements);
  }

  public async Task<NetBoxXeniaProfileDto?> GetProfileAsync(string profileId, CancellationToken cancellationToken = default)
  {
    var response = await httpClient.GetAsync($"/api/profiles/{Uri.EscapeDataString(profileId)}", cancellationToken).ConfigureAwait(false);
    if (!response.IsSuccessStatusCode)
    {
      return null;
    }

    var dto = await response.Content.ReadFromJsonAsync<XeniaProfilePayload>(cancellationToken: cancellationToken).ConfigureAwait(false);
    if (dto is null)
    {
      return null;
    }

    var achievements = await GetAchievementsAsync(dto.Id, cancellationToken).ConfigureAwait(false);
    return new NetBoxXeniaProfileDto(dto.Id, dto.Gamertag, dto.Gamerscore, dto.AvatarPath, dto.RecentGames?.Select(game => game.Name).ToArray() ?? Array.Empty<string>(), achievements);
  }

  public async Task<IReadOnlyList<NetBoxAchievementDto>> GetAchievementsAsync(string profileId, CancellationToken cancellationToken = default)
  {
    var response = await httpClient.GetAsync($"/api/profiles/{Uri.EscapeDataString(profileId)}/achievements", cancellationToken).ConfigureAwait(false);
    if (!response.IsSuccessStatusCode)
    {
      return Array.Empty<NetBoxAchievementDto>();
    }

    var payload = await response.Content.ReadFromJsonAsync<AchievementSummaryPayload>(cancellationToken: cancellationToken).ConfigureAwait(false);
    return payload?.Achievements?
      .Select(a => new NetBoxAchievementDto(a.Id, a.Name, a.Description, a.Gamerscore, a.IsUnlocked, a.UnlockedAt, a.ProgressPercent))
      .ToArray() ?? Array.Empty<NetBoxAchievementDto>();
  }

  private sealed record XeniaProfilePayload(string Id, string Gamertag, int Gamerscore, string? AvatarPath, IReadOnlyList<RecentGamePayload>? RecentGames);
  private sealed record AchievementSummaryPayload(string ProfileId, int Total, int Unlocked, IReadOnlyList<AchievementPayload> Achievements);
  private sealed record RecentGamePayload(string TitleId, string Name, DateTimeOffset LastPlayedAt);
  private sealed record AchievementPayload(string Id, string TitleId, string Name, string Description, int Gamerscore, bool IsUnlocked, DateTimeOffset? UnlockedAt, double? ProgressPercent);
}
