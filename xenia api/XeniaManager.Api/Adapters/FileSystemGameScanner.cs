using System.Text;

namespace XeniaManager.Api.Adapters;

public sealed class FileSystemGameScanner
{
  private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
  {
    ".iso",
    ".xex",
    ".zar",
  };

  public Task<IReadOnlyList<GameScanResult>> ScanAsync(string gamesRoot, string? contentRootPath = null, CancellationToken cancellationToken = default)
  {
    if (string.IsNullOrWhiteSpace(gamesRoot))
    {
      return Task.FromResult<IReadOnlyList<GameScanResult>>(Array.Empty<GameScanResult>());
    }

    var resolvedRoot = ResolvePath(gamesRoot, contentRootPath);
    if (!Directory.Exists(resolvedRoot))
    {
      return Task.FromResult<IReadOnlyList<GameScanResult>>(Array.Empty<GameScanResult>());
    }

    var matches = Directory
      .EnumerateFiles(resolvedRoot, "*", SearchOption.AllDirectories)
      .Where(path => SupportedExtensions.Contains(Path.GetExtension(path)))
      .Select(path =>
      {
        try
        {
          var file = new FileInfo(path);
          if (file.Length <= 0)
          {
            return null;
          }

          var relativePath = Path.GetRelativePath(resolvedRoot, path).Replace('\\', '/');
          var name = Path.GetFileNameWithoutExtension(file.Name);
          var title = string.IsNullOrWhiteSpace(name) ? "Untitled Game" : name;
          return new GameScanResult(
            Id: CreateStableId(relativePath),
            Name: title,
            TitleId: CreateTitleId(title, relativePath),
            Title: title,
            RelativePath: relativePath,
            FullPath: file.FullName,
            Extension: file.Extension,
            SizeBytes: file.Length,
            LastWriteTimeUtc: new DateTimeOffset(file.LastWriteTimeUtc));
        }
        catch
        {
          return null;
        }
      })
      .Where(game => game is not null)
      .Select(game => game!)
      .OrderByDescending(game => game.LastWriteTimeUtc)
      .ToArray();

    return Task.FromResult<IReadOnlyList<GameScanResult>>(matches);
  }

  private static string ResolvePath(string value, string? contentRootPath)
  {
    if (Path.IsPathRooted(value))
    {
      return value;
    }

    if (string.IsNullOrWhiteSpace(contentRootPath))
    {
      return Path.GetFullPath(value);
    }

    return Path.GetFullPath(Path.Combine(contentRootPath, value));
  }

  private static string CreateStableId(string path)
  {
    return path
      .ToLowerInvariant()
      .Replace('/', '-')
      .Replace(' ', '-')
      .Replace(".iso", string.Empty, StringComparison.Ordinal)
      .Replace(".xex", string.Empty, StringComparison.Ordinal)
      .Replace(".zar", string.Empty, StringComparison.Ordinal);
  }

  private static string CreateTitleId(string title, string relativePath)
  {
    var slug = ToSlug(title);
    return string.IsNullOrWhiteSpace(slug)
      ? ToSlug(relativePath)
      : slug;
  }

  private static string ToSlug(string value)
  {
    var builder = new StringBuilder(value.Length);
    var lastDash = false;
    foreach (var ch in value.Trim().ToLowerInvariant())
    {
      if (char.IsLetterOrDigit(ch))
      {
        builder.Append(ch);
        lastDash = false;
        continue;
      }

      if (!lastDash && builder.Length > 0)
      {
        builder.Append('-');
        lastDash = true;
      }
    }

    return builder.ToString().Trim('-');
  }
}

public sealed record GameScanResult(
  string Id,
  string Name,
  string TitleId,
  string Title,
  string RelativePath,
  string FullPath,
  string Extension,
  long SizeBytes,
  DateTimeOffset LastWriteTimeUtc);
