using XeniaManager.Api.Adapters;
using Xunit;

namespace NetBox.Tests;

public sealed class GameScannerTests
{
  [Fact]
  public async Task ScanAsync_RecursesAndSkipsUnsupportedFiles()
  {
    using var temp = new TempDirectory();
    var gamesRoot = Path.Combine(temp.Path, "games");
    var nested = Path.Combine(gamesRoot, "nested");
    Directory.CreateDirectory(nested);

    var supported = Path.Combine(nested, "Halo.iso");
    var supported2 = Path.Combine(gamesRoot, "Forza.xex");
    var unsupported = Path.Combine(gamesRoot, "notes.txt");
    var unsupportedImage = Path.Combine(nested, "cover.png");
    var unsupportedNested = Path.Combine(nested, "ignore.rar");

    await File.WriteAllTextAsync(supported, "game");
    await File.WriteAllTextAsync(supported2, "game");
    await File.WriteAllTextAsync(unsupported, "ignore");
    await File.WriteAllTextAsync(unsupportedImage, "ignore");
    await File.WriteAllTextAsync(unsupportedNested, "ignore");

    var scanner = new FileSystemGameScanner();
    var discovered = await scanner.ScanAsync(gamesRoot, temp.Path);

    Assert.Equal(2, discovered.Count);
    Assert.Contains(discovered, game => game.FullPath == supported && game.Name == "Halo" && game.RelativePath == Path.Combine("nested", "Halo.iso").Replace('\\', '/'));
    Assert.Contains(discovered, game => game.FullPath == supported2 && game.Name == "Forza" && game.RelativePath == "Forza.xex");
  }

  [Fact]
  public async Task ScanAsync_NormalizesMetadataAndSkipsEmptyFiles()
  {
    using var temp = new TempDirectory();
    var gamesRoot = Path.Combine(temp.Path, "games");
    Directory.CreateDirectory(gamesRoot);

    var validPath = Path.Combine(gamesRoot, "My Awesome Game.iso");
    var emptyPath = Path.Combine(gamesRoot, "Empty.iso");

    await File.WriteAllTextAsync(validPath, "game payload");
    await File.WriteAllBytesAsync(emptyPath, Array.Empty<byte>());

    var scanner = new FileSystemGameScanner();
    var discovered = await scanner.ScanAsync(gamesRoot, temp.Path);

    var metadata = Assert.Single(discovered);
    Assert.Equal("my-awesome-game", metadata.TitleId);
    Assert.Equal("My Awesome Game", metadata.Title);
    Assert.Equal(validPath, metadata.FullPath);
    Assert.Equal(12, metadata.SizeBytes);
  }

  private sealed class TempDirectory : IDisposable
  {
    public TempDirectory()
    {
      Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "netbox-scanner-tests", Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
      if (Directory.Exists(Path))
      {
        Directory.Delete(Path, recursive: true);
      }
    }
  }
}
