using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using XeniaManager.Adapters.Legacy;
using XeniaManager.Models;

namespace XeniaManager.Api.Adapters;

public sealed class FileSystemLegacyFacade : IXeniaManagerLegacyFacade
{
  private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
  {
    WriteIndented = true,
  };

  private readonly XeniaDataOptions options;
  private readonly IWebHostEnvironment environment;
  private readonly ILogger<FileSystemLegacyFacade> logger;

  public FileSystemLegacyFacade(IOptions<XeniaDataOptions> options, IWebHostEnvironment environment, ILogger<FileSystemLegacyFacade> logger)
  {
    this.options = options.Value;
    this.environment = environment;
    this.logger = logger;
  }

  public async Task<IReadOnlyList<ProfileDto>> EnumerateProfilesAsync(CancellationToken cancellationToken = default)
  {
    var profiles = await ReadProfilesAsync(cancellationToken).ConfigureAwait(false);
    var games = GetGameFiles();
    return profiles
      .Select(p => p with { RecentGames = BuildRecentGames(games, p.Id) })
      .ToArray();
  }

  public async Task<ProfileDto?> ReadProfileAsync(string profileId, CancellationToken cancellationToken = default)
  {
    var profiles = await ReadProfilesAsync(cancellationToken).ConfigureAwait(false);
    var profile = profiles.FirstOrDefault(p => p.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase));
    if (profile is null)
    {
      return null;
    }

    return profile with { RecentGames = BuildRecentGames(GetGameFiles(), profile.Id) };
  }

  public async Task<ProfileDto> CreateProfileAsync(CreateProfileRequest request, CancellationToken cancellationToken = default)
  {
    var profiles = (await ReadProfilesAsync(cancellationToken).ConfigureAwait(false)).ToList();
    var profile = new ProfileDto(
      Id: Guid.NewGuid().ToString("N"),
      Gamertag: request.Gamertag,
      Gamerscore: 0,
      AvatarPath: null,
      IsActive: profiles.Count == 0,
      RecentGames: Array.Empty<RecentGameDto>());

    profiles.Add(profile);
    await WriteProfilesAsync(profiles, cancellationToken).ConfigureAwait(false);
    return profile;
  }

  public async Task<ProfileDto?> RenameProfileAsync(string profileId, string gamertag, CancellationToken cancellationToken = default)
  {
    var profiles = (await ReadProfilesAsync(cancellationToken).ConfigureAwait(false)).ToList();
    var index = profiles.FindIndex(p => p.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase));
    if (index < 0)
    {
      return null;
    }

    profiles[index] = profiles[index] with { Gamertag = gamertag };
    await WriteProfilesAsync(profiles, cancellationToken).ConfigureAwait(false);
    return profiles[index];
  }

  public async Task<bool> DeleteProfileAsync(string profileId, CancellationToken cancellationToken = default)
  {
    var profiles = (await ReadProfilesAsync(cancellationToken).ConfigureAwait(false)).ToList();
    var removed = profiles.RemoveAll(p => p.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase)) > 0;
    if (!removed)
    {
      return false;
    }

    if (profiles.Count > 0 && profiles.All(p => !p.IsActive))
    {
      profiles[0] = profiles[0] with { IsActive = true };
    }

    await WriteProfilesAsync(profiles, cancellationToken).ConfigureAwait(false);
    return true;
  }

  public async Task<bool> SetActiveProfileAsync(string profileId, CancellationToken cancellationToken = default)
  {
    var profiles = (await ReadProfilesAsync(cancellationToken).ConfigureAwait(false)).ToList();
    var exists = profiles.Any(p => p.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase));
    if (!exists)
    {
      return false;
    }

    for (var i = 0; i < profiles.Count; i++)
    {
      profiles[i] = profiles[i] with { IsActive = profiles[i].Id.Equals(profileId, StringComparison.OrdinalIgnoreCase) };
    }

    await WriteProfilesAsync(profiles, cancellationToken).ConfigureAwait(false);
    return true;
  }

  public async Task<IReadOnlyList<AchievementDto>> EnumerateAchievementsAsync(string profileId, CancellationToken cancellationToken = default)
  {
    var path = ResolvePath(Path.Combine(options.AchievementsDirectory, $"{profileId}.json"));
    if (!File.Exists(path))
    {
      return Array.Empty<AchievementDto>();
    }

    await using var stream = File.OpenRead(path);
    var items = await JsonSerializer.DeserializeAsync<List<AchievementDto>>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
    return items ?? new List<AchievementDto>();
  }

  public Task<IReadOnlyList<SaveFileDto>> EnumerateSavesAsync(string profileId, CancellationToken cancellationToken = default)
  {
    var root = ResolvePath(Path.Combine(options.SavesDirectory, profileId));
    if (!Directory.Exists(root))
    {
      return Task.FromResult<IReadOnlyList<SaveFileDto>>(Array.Empty<SaveFileDto>());
    }

    var files = Directory
      .EnumerateFiles(root, "*", SearchOption.AllDirectories)
      .Select(path =>
      {
        var file = new FileInfo(path);
        var rel = Path.GetRelativePath(root, path).Replace('\\', '/');
        var titleId = rel.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "unknown";
        return new SaveFileDto(
          Id: rel,
          TitleId: titleId,
          Name: file.Name,
          Path: path,
          SizeBytes: file.Length,
          LastWriteTimeUtc: new DateTimeOffset(file.LastWriteTimeUtc));
      })
      .OrderByDescending(f => f.LastWriteTimeUtc)
      .ToArray();

    return Task.FromResult<IReadOnlyList<SaveFileDto>>(files);
  }

  public async Task<SaveOperationResultDto> BackupSavesAsync(SaveBackupRequest request, CancellationToken cancellationToken = default)
  {
    var source = ResolvePath(Path.Combine(options.SavesDirectory, request.ProfileId));
    var destination = ResolvePath(request.DestinationPath);
    if (!Directory.Exists(source))
    {
      return new SaveOperationResultDto(false, "Profile save directory not found.", Array.Empty<string>());
    }

    Directory.CreateDirectory(destination);
    CopyDirectory(source, destination, overwrite: true);
    var ids = (await EnumerateSavesAsync(request.ProfileId, cancellationToken).ConfigureAwait(false)).Select(s => s.Id).ToArray();
    return new SaveOperationResultDto(true, "Backup completed.", ids);
  }

  public async Task<SaveOperationResultDto> RestoreSavesAsync(SaveRestoreRequest request, CancellationToken cancellationToken = default)
  {
    var source = ResolvePath(request.SourcePath);
    var destination = ResolvePath(Path.Combine(options.SavesDirectory, request.ProfileId));
    if (!Directory.Exists(source))
    {
      return new SaveOperationResultDto(false, "Backup source directory not found.", Array.Empty<string>());
    }

    Directory.CreateDirectory(destination);
    CopyDirectory(source, destination, request.OverwriteExisting);
    var ids = (await EnumerateSavesAsync(request.ProfileId, cancellationToken).ConfigureAwait(false)).Select(s => s.Id).ToArray();
    return new SaveOperationResultDto(true, "Restore completed.", ids);
  }

  public async Task<SaveOperationResultDto> ImportSavesAsync(SaveImportRequest request, CancellationToken cancellationToken = default)
  {
    var mapped = new SaveRestoreRequest(request.ProfileId, request.ImportPath, request.OverwriteExisting);
    return await RestoreSavesAsync(mapped, cancellationToken).ConfigureAwait(false);
  }

  public async Task<SaveOperationResultDto> ExportSavesAsync(SaveExportRequest request, CancellationToken cancellationToken = default)
  {
    var mapped = new SaveBackupRequest(request.ProfileId, request.ExportPath, request.SaveIds);
    return await BackupSavesAsync(mapped, cancellationToken).ConfigureAwait(false);
  }

  public async Task<EmulatorConfigDto> ReadConfigAsync(CancellationToken cancellationToken = default)
  {
    var path = ResolvePath(options.ConfigFilePath);
    if (!File.Exists(path))
    {
      return new EmulatorConfigDto(new Dictionary<string, string>());
    }

    await using var stream = File.OpenRead(path);
    var dict = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
      ?? new Dictionary<string, string>();
    return new EmulatorConfigDto(dict);
  }

  public async Task<EmulatorConfigDto> SaveConfigAsync(UpdateConfigRequest request, CancellationToken cancellationToken = default)
  {
    var path = ResolvePath(options.ConfigFilePath);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    await using var stream = File.Create(path);
    await JsonSerializer.SerializeAsync(stream, request.Values, JsonOptions, cancellationToken).ConfigureAwait(false);
    return new EmulatorConfigDto(request.Values);
  }

  // Serializes concurrent launch requests so two near-simultaneous "start" calls
  // (e.g. a double click, or a retried request) cannot spawn two Xenia processes.
  private static readonly SemaphoreSlim LaunchGate = new(1, 1);

  public async Task<LauncherStatusDto> LaunchXeniaAsync(LauncherStartRequest request, CancellationToken cancellationToken = default)
  {
    var requestedExecutable = string.IsNullOrWhiteSpace(request.ExecutablePath)
      ? options.XeniaExecutablePath
      : request.ExecutablePath;

    var resolvedExecutable = ResolveExecutablePath(requestedExecutable);
    var executableForLaunch = resolvedExecutable ?? requestedExecutable;

    if (string.IsNullOrWhiteSpace(executableForLaunch))
    {
      return new LauncherStatusDto(false, null, requestedExecutable);
    }

    // If the caller passed a path-like value and it did not resolve to a file, fail fast.
    if (resolvedExecutable is null && LooksLikePath(requestedExecutable))
    {
      logger.LogWarning("Xenia executable not found. Requested path: {Executable}", requestedExecutable);
      return new LauncherStatusDto(false, null, requestedExecutable);
    }

    var workingDirectory = request.WorkingDirectory is { Length: > 0 }
      ? ResolvePath(request.WorkingDirectory)
      : resolvedExecutable is { Length: > 0 }
        ? Path.GetDirectoryName(resolvedExecutable) ?? environment.ContentRootPath
        : environment.ContentRootPath;

    await LaunchGate.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      // Duplicate-launch guard: if Xenia is already running, reuse it instead of
      // spawning a second instance that would race for the same profile/save data.
      var existingProcessName = Path.GetFileNameWithoutExtension(executableForLaunch);
      var existing = Process.GetProcessesByName(existingProcessName).FirstOrDefault(p => !p.HasExited);
      if (existing is not null)
      {
        logger.LogInformation("Xenia is already running (PID {ProcessId}); reusing existing process instead of launching a duplicate.", existing.Id);
        return new LauncherStatusDto(true, existing.Id, executableForLaunch);
      }

      var process = Process.Start(new ProcessStartInfo
      {
        FileName = executableForLaunch,
        Arguments = request.Arguments ?? string.Empty,
        WorkingDirectory = workingDirectory,
        UseShellExecute = false,
      });

      return new LauncherStatusDto(process is not null && !process.HasExited, process?.Id, executableForLaunch);
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "Failed to launch Xenia. Executable: {Executable}. WorkingDirectory: {WorkingDirectory}. Arguments: {Arguments}", executableForLaunch, workingDirectory, request.Arguments ?? string.Empty);
      return new LauncherStatusDto(false, null, executableForLaunch);
    }
    finally
    {
      LaunchGate.Release();
    }
  }

  public async Task<LauncherStatusDto> StopXeniaAsync(CancellationToken cancellationToken = default)
  {
    await LaunchGate.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var name = Path.GetFileNameWithoutExtension(options.XeniaExecutablePath);
      foreach (var process in Process.GetProcessesByName(name))
      {
        try
        {
          // entireProcessTree ensures any ffmpeg/helper children spawned for
          // capture are also released so ports and file locks are freed.
          process.Kill(entireProcessTree: true);
          process.WaitForExit(5000);
        }
        catch (Exception ex)
        {
          logger.LogWarning(ex, "Failed to terminate Xenia process {ProcessId} cleanly.", process.Id);
        }
      }

      return new LauncherStatusDto(false, null, ResolvePath(options.XeniaExecutablePath));
    }
    finally
    {
      LaunchGate.Release();
    }
  }

  public Task<LauncherStatusDto> ReadXeniaStatusAsync(CancellationToken cancellationToken = default)
  {
    var name = Path.GetFileNameWithoutExtension(options.XeniaExecutablePath);
    var process = Process.GetProcessesByName(name).FirstOrDefault();
    return Task.FromResult(new LauncherStatusDto(process is not null && !process.HasExited, process?.Id, ResolvePath(options.XeniaExecutablePath)));
  }

  private async Task<IReadOnlyList<ProfileDto>> ReadProfilesAsync(CancellationToken cancellationToken)
  {
    var path = ResolvePath(options.ProfilesFilePath);
    if (!File.Exists(path))
    {
      return Array.Empty<ProfileDto>();
    }

    await using var stream = File.OpenRead(path);
    var profiles = await JsonSerializer.DeserializeAsync<List<ProfileDto>>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
    return profiles ?? new List<ProfileDto>();
  }

  private async Task WriteProfilesAsync(IReadOnlyList<ProfileDto> profiles, CancellationToken cancellationToken)
  {
    var path = ResolvePath(options.ProfilesFilePath);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    await using var stream = File.Create(path);
    await JsonSerializer.SerializeAsync(stream, profiles, JsonOptions, cancellationToken).ConfigureAwait(false);
  }

  private static void CopyDirectory(string source, string destination, bool overwrite)
  {
    foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
    {
      var rel = Path.GetRelativePath(source, dir);
      Directory.CreateDirectory(Path.Combine(destination, rel));
    }

    foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
    {
      var rel = Path.GetRelativePath(source, file);
      var target = Path.Combine(destination, rel);
      Directory.CreateDirectory(Path.GetDirectoryName(target)!);
      File.Copy(file, target, overwrite);
    }
  }

  private IReadOnlyList<RecentGameDto> BuildRecentGames(IReadOnlyList<FileInfo> gameFiles, string profileId)
  {
    _ = profileId;
    return gameFiles
      .OrderByDescending(g => g.LastWriteTimeUtc)
      .Take(12)
      .Select(g => new RecentGameDto(
        TitleId: g.Name.GetHashCode(StringComparison.Ordinal).ToString("X8"),
        Name: Path.GetFileNameWithoutExtension(g.Name),
        LastPlayedAt: new DateTimeOffset(g.LastWriteTimeUtc)))
      .ToArray();
  }

  private IReadOnlyList<FileInfo> GetGameFiles()
  {
    var root = ResolvePath(options.GamesDirectory);
    if (!Directory.Exists(root))
    {
      return Array.Empty<FileInfo>();
    }

    return Directory
      .EnumerateFiles(root, "*", SearchOption.AllDirectories)
      .Where(path =>
      {
        var ext = Path.GetExtension(path);
        return ext.Equals(".iso", StringComparison.OrdinalIgnoreCase)
          || ext.Equals(".xex", StringComparison.OrdinalIgnoreCase)
          || ext.Equals(".zar", StringComparison.OrdinalIgnoreCase);
      })
      .Select(path => new FileInfo(path))
      .ToArray();
  }

  private string ResolvePath(string path)
  {
    if (Path.IsPathRooted(path))
    {
      return path;
    }

    return Path.GetFullPath(Path.Combine(environment.ContentRootPath, path));
  }

  private string? ResolveExecutablePath(string path)
  {
    if (string.IsNullOrWhiteSpace(path))
    {
      return null;
    }

    if (Path.IsPathRooted(path))
    {
      return File.Exists(path) ? path : null;
    }

    var contentRelative = ResolvePath(path);
    if (File.Exists(contentRelative))
    {
      return contentRelative;
    }

    return null;
  }

  private static bool LooksLikePath(string value)
    => Path.IsPathRooted(value)
      || value.Contains('\\')
      || value.Contains('/');
}
