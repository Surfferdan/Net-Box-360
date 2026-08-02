using Xunit;
using XeniaManager.Api.Adapters;

namespace NetBox.Tests;

public sealed class GameEnrichmentTests
{
  [Fact]
  public void Build_UsesKnownTitleHintsForCoverAndMetadata()
  {
    var metadata = GameEnrichmentMetadataBuilder.Build("Halo 4", null);

    Assert.Equal("Action", metadata.Genre);
    Assert.Equal(1, metadata.Players);
    Assert.NotNull(metadata.CoverPath);
  }

  [Fact]
  public void Build_FallsBackGracefullyForUnknownTitles()
  {
    var metadata = GameEnrichmentMetadataBuilder.Build("Totally Unknown Game", null);

    Assert.Null(metadata.Genre);
    Assert.Null(metadata.Players);
    Assert.NotNull(metadata.CoverPath);
  }
}
