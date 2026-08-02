using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NetBox.Data.Repositories;
using NetBox.Models;
using System.Text.Json;
using System.Text.Json.Serialization;
using XeniaManager.Api.Adapters;

namespace XeniaManager.Api.Controllers;

[ApiController]
[Route("api/games")]
public sealed class GamesController : ControllerBase
{
  private static readonly HttpClient Http = new();
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

  [HttpGet]
  public async Task<ActionResult<IReadOnlyList<GameCatalogItemDto>>> GetGames(
    [FromServices] IOptions<XeniaDataOptions> options,
    [FromServices] IWebHostEnvironment environment,
    [FromServices] INetBoxRepository repository,
    CancellationToken cancellationToken)
  {
    var storedCatalog = await repository.ListGameCatalogAsync(cancellationToken).ConfigureAwait(false);
    if (storedCatalog.Count > 0)
    {
      return Ok(MapStoredCatalog(storedCatalog));
    }

    var games = await RefreshCatalogCoreAsync(options, environment, repository, cancellationToken).ConfigureAwait(false);
    return Ok(games);
  }

  [HttpPost("refresh")]
  public async Task<ActionResult<IReadOnlyList<GameCatalogItemDto>>> RefreshCatalog(
    [FromBody] RefreshGamesRequest? request,
    [FromServices] IOptions<XeniaDataOptions> options,
    [FromServices] IWebHostEnvironment environment,
    [FromServices] INetBoxRepository repository,
    CancellationToken cancellationToken)
  {
    if (request?.Games is null)
    {
      var refreshedGames = await RefreshCatalogCoreAsync(options, environment, repository, cancellationToken).ConfigureAwait(false);
      return Ok(refreshedGames);
    }

    foreach (var entry in request.Games)
    {
      await repository.UpsertGameCatalogEntryAsync(new GameCatalogEntryDto(
        Id: entry.Id,
        TitleId: entry.TitleId,
        Title: entry.Title,
        RelativePath: entry.RelativePath,
        FullPath: entry.FullPath,
        Extension: entry.Extension,
        SizeBytes: entry.SizeBytes,
        Genre: entry.Genre,
        Players: entry.Players,
        LastWriteTimeUtc: entry.LastWriteTimeUtc,
        LastPlayedAt: null,
        CoverPath: entry.CoverPath), cancellationToken).ConfigureAwait(false);
    }

    var storedCatalog = await repository.ListGameCatalogAsync(cancellationToken).ConfigureAwait(false);
    return Ok(MapStoredCatalog(storedCatalog));
  }

  private static GameCatalogItemDto[] MapStoredCatalog(IReadOnlyList<GameCatalogEntryDto> storedCatalog)
  {
    return storedCatalog
      .Select(entry => new GameCatalogItemDto(
        Id: entry.Id,
        Name: entry.Title,
        TitleId: entry.TitleId,
        Title: entry.Title,
        RelativePath: entry.RelativePath,
        FullPath: entry.FullPath,
        Extension: entry.Extension,
        SizeBytes: entry.SizeBytes,
        Genre: entry.Genre,
        Players: entry.Players,
        LastWriteTimeUtc: entry.LastWriteTimeUtc,
        CoverPath: entry.CoverPath))
      .ToArray();
  }

  private static async Task<GameCatalogItemDto[]> RefreshCatalogCoreAsync(
    IOptions<XeniaDataOptions> options,
    IWebHostEnvironment environment,
    INetBoxRepository repository,
    CancellationToken cancellationToken)
  {
    var scanner = new FileSystemGameScanner();
    var scanned = await scanner.ScanAsync(options.Value.GamesDirectory, environment.ContentRootPath, cancellationToken).ConfigureAwait(false);
    if (scanned.Count == 0)
    {
      return Array.Empty<GameCatalogItemDto>();
    }

    var coverIndex = BuildCoverIndex(environment.ContentRootPath);
    var coverCachePath = Path.Combine(environment.ContentRootPath, "data", "game-cover-cache.json");
    var coverCache = await ReadCoverCacheAsync(coverCachePath, cancellationToken).ConfigureAwait(false);

    var games = scanned
      .Select(scan =>
      {
        var enrichment = GameEnrichmentMetadataBuilder.Build(scan.Title, null);
        var coverPath = FindCoverPath(scan.Name, coverIndex);

        return new GameCatalogItemDto(
          Id: scan.Id,
          Name: scan.Name,
          TitleId: scan.TitleId,
          Title: scan.Title,
          RelativePath: scan.RelativePath,
          FullPath: scan.FullPath,
          Extension: scan.Extension,
          SizeBytes: scan.SizeBytes,
          Genre: enrichment.Genre,
          Players: enrichment.Players,
          LastWriteTimeUtc: scan.LastWriteTimeUtc,
          CoverPath: coverPath);
      })
      .ToArray();

    var changed = false;
    for (var i = 0; i < games.Length; i++)
    {
      if (!string.IsNullOrWhiteSpace(games[i].CoverPath))
      {
        continue;
      }

      var normalized = NormalizeName(games[i].Name);
      if (coverCache.TryGetValue(normalized, out var cachedPath) && !string.IsNullOrWhiteSpace(cachedPath))
      {
        games[i] = games[i] with { CoverPath = cachedPath };
        continue;
      }

      var discovered = await TryFetchRemoteCoverAsync(games[i].Name, cancellationToken).ConfigureAwait(false);
      if (!string.IsNullOrWhiteSpace(discovered))
      {
        games[i] = games[i] with { CoverPath = discovered };
        coverCache[normalized] = discovered;
        changed = true;
      }
    }

    if (changed)
    {
      await WriteCoverCacheAsync(coverCachePath, coverCache, cancellationToken).ConfigureAwait(false);
    }

    for (var i = 0; i < games.Length; i++)
    {
      if (!string.IsNullOrWhiteSpace(games[i].CoverPath))
      {
        continue;
      }

      var fallback = GameEnrichmentMetadataBuilder.Build(games[i].Title, null).CoverPath;
      if (!string.IsNullOrWhiteSpace(fallback))
      {
        games[i] = games[i] with { CoverPath = fallback };
      }
    }

    foreach (var game in games)
    {
      var existing = await repository.GetGameCatalogEntryAsync(game.Id, cancellationToken).ConfigureAwait(false);
      await repository.UpsertGameCatalogEntryAsync(new GameCatalogEntryDto(
        Id: game.Id,
        TitleId: game.TitleId,
        Title: game.Title,
        RelativePath: game.RelativePath,
        FullPath: game.FullPath,
        Extension: game.Extension,
        SizeBytes: game.SizeBytes,
        Genre: game.Genre,
        Players: game.Players,
        LastWriteTimeUtc: game.LastWriteTimeUtc,
        LastPlayedAt: existing?.LastPlayedAt,
        CoverPath: game.CoverPath), cancellationToken).ConfigureAwait(false);
    }

    return games;
  }

  private static string ResolvePath(string value, string contentRootPath)
  {
    if (Path.IsPathRooted(value))
    {
      return value;
    }

    return Path.GetFullPath(Path.Combine(contentRootPath, value));
  }

  private static IReadOnlyList<CoverCandidate> BuildCoverIndex(string contentRootPath)
  {
    var coverRoot = Path.GetFullPath(Path.Combine(
      contentRootPath,
      "..",
      "..",
      "web-port",
      "public",
      "assets",
      "Assets",
      "Custom Files",
      "CoverArt",
      "Game Menu Cover"));

    if (!Directory.Exists(coverRoot))
    {
      return Array.Empty<CoverCandidate>();
    }

    return Directory
      .EnumerateFiles(coverRoot, "*", SearchOption.TopDirectoryOnly)
      .Where(path =>
      {
        var ext = Path.GetExtension(path);
        return ext.Equals(".png", StringComparison.OrdinalIgnoreCase)
          || ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
          || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
          || ext.Equals(".webp", StringComparison.OrdinalIgnoreCase);
      })
      .Select(path => new CoverCandidate(
        NormalizeName(Path.GetFileNameWithoutExtension(path)),
        BuildPublicCoverPath(Path.GetFileName(path))))
      .ToArray();
  }

  private static string? FindCoverPath(string gameName, IReadOnlyList<CoverCandidate> covers)
  {
    if (covers.Count == 0)
    {
      return null;
    }

    var normalized = NormalizeName(gameName);
    var exact = covers.FirstOrDefault(c => c.NormalizedName.Equals(normalized, StringComparison.Ordinal));
    if (exact is not null)
    {
      return exact.PublicPath;
    }

    var fuzzy = covers.FirstOrDefault(c =>
      c.NormalizedName.Contains(normalized, StringComparison.Ordinal)
      || normalized.Contains(c.NormalizedName, StringComparison.Ordinal));
    return fuzzy?.PublicPath;
  }

  private static string NormalizeName(string value)
  {
    return value
      .Trim()
      .ToLowerInvariant()
      .Replace("_", " ", StringComparison.Ordinal)
      .Replace("-", " ", StringComparison.Ordinal)
      .Replace("  ", " ", StringComparison.Ordinal)
      .Replace("  ", " ", StringComparison.Ordinal)
      .Trim();
  }

  private static string BuildPublicCoverPath(string fileName)
  {
    var encoded = Uri.EscapeDataString(fileName);
    return $"/assets/Assets/Custom%20Files/CoverArt/Game%20Menu%20Cover/{encoded}";
  }

  private static async Task<Dictionary<string, string>> ReadCoverCacheAsync(string path, CancellationToken cancellationToken)
  {
    if (!System.IO.File.Exists(path))
    {
      return new Dictionary<string, string>(StringComparer.Ordinal);
    }

    await using var stream = System.IO.File.OpenRead(path);
    var map = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(stream, Json, cancellationToken).ConfigureAwait(false);
    return map is null
      ? new Dictionary<string, string>(StringComparer.Ordinal)
      : new Dictionary<string, string>(map, StringComparer.Ordinal);
  }

  private static async Task WriteCoverCacheAsync(string path, Dictionary<string, string> cache, CancellationToken cancellationToken)
  {
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    await using var stream = System.IO.File.Create(path);
    await JsonSerializer.SerializeAsync(stream, cache, Json, cancellationToken).ConfigureAwait(false);
  }

  private static async Task<string?> TryFetchRemoteCoverAsync(string title, CancellationToken cancellationToken)
  {
    foreach (var query in BuildCoverSearchTerms(title))
    {
      var escaped = Uri.EscapeDataString(query);
      var searchUrl = $"https://store.steampowered.com/api/storesearch/?term={escaped}&l=english&cc=us";

      try
      {
        using var response = await Http.GetAsync(searchUrl, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
          continue;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var payload = await JsonSerializer.DeserializeAsync<SteamSearchResponse>(stream, Json, cancellationToken).ConfigureAwait(false);
        var first = payload?.Items?.FirstOrDefault();
        if (first is null)
        {
          continue;
        }

        var found = !string.IsNullOrWhiteSpace(first.TinyImage)
          ? first.TinyImage
          : first.LargeCapsuleImage;

        if (!string.IsNullOrWhiteSpace(found))
        {
          return found;
        }
      }
      catch
      {
        // Continue trying alternate query variants.
      }
    }

    return null;
  }

  private static IReadOnlyList<string> BuildCoverSearchTerms(string title)
  {
    var raw = title.Trim();
    var noParens = RemoveParenthetical(raw).Trim();
    var cleaned = NormalizeSearch(noParens);
    var fallback = NormalizeSearch(raw);

    var terms = new List<string>();
    if (!string.IsNullOrWhiteSpace(raw)) terms.Add(raw);
    if (!string.IsNullOrWhiteSpace(noParens) && !terms.Contains(noParens, StringComparer.OrdinalIgnoreCase)) terms.Add(noParens);
    if (!string.IsNullOrWhiteSpace(cleaned) && !terms.Contains(cleaned, StringComparer.OrdinalIgnoreCase)) terms.Add(cleaned);
    if (!string.IsNullOrWhiteSpace(fallback) && !terms.Contains(fallback, StringComparer.OrdinalIgnoreCase)) terms.Add(fallback);
    return terms;
  }

  private static string RemoveParenthetical(string value)
  {
    var chars = new List<char>(value.Length);
    var depth = 0;
    foreach (var ch in value)
    {
      if (ch == '(')
      {
        depth++;
        continue;
      }

      if (ch == ')' && depth > 0)
      {
        depth--;
        continue;
      }

      if (depth == 0)
      {
        chars.Add(ch);
      }
    }

    return new string(chars.ToArray());
  }

  private static string NormalizeSearch(string value)
  {
    var chars = value
      .Select(ch => char.IsLetterOrDigit(ch) ? ch : ' ')
      .ToArray();

    return string.Join(' ', new string(chars)
      .Split(' ', StringSplitOptions.RemoveEmptyEntries));
  }

  public sealed record GameCatalogItemDto(
    string Id,
    string Name,
    string TitleId,
    string Title,
    string RelativePath,
    string FullPath,
    string Extension,
    long SizeBytes,
    string? Genre,
    int? Players,
    DateTimeOffset LastWriteTimeUtc,
    string? CoverPath);

  public sealed record RefreshGamesRequest(IReadOnlyList<RefreshGameCatalogEntry> Games);

  public sealed record RefreshGameCatalogEntry(
    string Id,
    string TitleId,
    string Title,
    string RelativePath,
    string FullPath,
    string Extension,
    long SizeBytes,
    string? Genre,
    int? Players,
    DateTimeOffset LastWriteTimeUtc,
    string? CoverPath);

  private sealed record CoverCandidate(string NormalizedName, string PublicPath);

  private sealed record SteamSearchResponse(IReadOnlyList<SteamSearchItem>? Items);

  private sealed record SteamSearchItem(
    string Name,
    [property: JsonPropertyName("tiny_image")] string? TinyImage,
    [property: JsonPropertyName("large_capsule_image")] string? LargeCapsuleImage);
}