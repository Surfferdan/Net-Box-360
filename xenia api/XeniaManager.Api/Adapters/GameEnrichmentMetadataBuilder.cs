namespace XeniaManager.Api.Adapters;

public static class GameEnrichmentMetadataBuilder
{
  private static readonly Dictionary<string, GameEnrichmentMetadata> KnownTitles = new(StringComparer.OrdinalIgnoreCase)
  {
    ["halo 4"] = new GameEnrichmentMetadata("Action", 1, "/assets/Assets/Tiles/halo4home.jpg"),
    ["forza horizon"] = new GameEnrichmentMetadata("Racing", 1, "/assets/Assets/Tiles/forzahorizongames.jpg"),
    ["minecraft"] = new GameEnrichmentMetadata("Sandbox", 1, "/assets/Assets/Tiles/minecraftgames.jpg"),
    ["black ops ii"] = new GameEnrichmentMetadata("Shooter", 2, "/assets/Assets/Tiles/blackops2games.jpg"),
    ["kung fu panda 2"] = new GameEnrichmentMetadata("Video", 1, "/assets/Assets/Tiles/kungfupanda2video.jpg"),
  };

  public static GameEnrichmentMetadata Build(string title, string? coverPath)
  {
    if (string.IsNullOrWhiteSpace(title))
    {
      return new GameEnrichmentMetadata(null, null, coverPath);
    }

    var normalized = title.Trim().ToLowerInvariant();
    if (KnownTitles.TryGetValue(normalized, out var known))
    {
      return new GameEnrichmentMetadata(
        known.Genre,
        known.Players,
        string.IsNullOrWhiteSpace(coverPath) ? known.CoverPath : coverPath);
    }

    return new GameEnrichmentMetadata(null, null, string.IsNullOrWhiteSpace(coverPath) ? "/assets/Assets/Tiles/halo4home.jpg" : coverPath);
  }
}

public sealed record GameEnrichmentMetadata(string? Genre, int? Players, string? CoverPath);
