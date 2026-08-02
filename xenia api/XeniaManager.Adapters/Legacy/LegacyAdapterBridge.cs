using XeniaManager.Core.Abstractions.Adapters;
using XeniaManager.Models;

namespace XeniaManager.Adapters.Legacy;

public sealed class LegacyAdapterBridge :
  IXeniaProfileAdapter,
  IXeniaAchievementAdapter,
  IXeniaSaveAdapter,
  IXeniaConfigAdapter,
  IXeniaLauncherAdapter
{
  private readonly IXeniaManagerLegacyFacade legacy;

  public LegacyAdapterBridge(IXeniaManagerLegacyFacade legacy)
  {
    this.legacy = legacy;
  }

  public Task<IReadOnlyList<ProfileDto>> GetProfilesAsync(CancellationToken cancellationToken = default)
    => legacy.EnumerateProfilesAsync(cancellationToken);

  public Task<ProfileDto?> GetProfileAsync(string profileId, CancellationToken cancellationToken = default)
    => legacy.ReadProfileAsync(profileId, cancellationToken);

  public Task<ProfileDto> CreateProfileAsync(CreateProfileRequest request, CancellationToken cancellationToken = default)
    => legacy.CreateProfileAsync(request, cancellationToken);

  public Task<ProfileDto?> RenameProfileAsync(string profileId, string gamertag, CancellationToken cancellationToken = default)
    => legacy.RenameProfileAsync(profileId, gamertag, cancellationToken);

  public Task<bool> DeleteProfileAsync(string profileId, CancellationToken cancellationToken = default)
    => legacy.DeleteProfileAsync(profileId, cancellationToken);

  public Task<bool> SetActiveProfileAsync(string profileId, CancellationToken cancellationToken = default)
    => legacy.SetActiveProfileAsync(profileId, cancellationToken);

  public Task<IReadOnlyList<AchievementDto>> GetAchievementsAsync(string profileId, CancellationToken cancellationToken = default)
    => legacy.EnumerateAchievementsAsync(profileId, cancellationToken);

  public async Task<IReadOnlyList<AchievementDto>> GetUnlockedAchievementsAsync(string profileId, CancellationToken cancellationToken = default)
  {
    var all = await legacy.EnumerateAchievementsAsync(profileId, cancellationToken).ConfigureAwait(false);
    return all.Where(a => a.IsUnlocked).ToArray();
  }

  public Task<IReadOnlyList<SaveFileDto>> GetSavesAsync(string profileId, CancellationToken cancellationToken = default)
    => legacy.EnumerateSavesAsync(profileId, cancellationToken);

  public Task<SaveOperationResultDto> BackupAsync(SaveBackupRequest request, CancellationToken cancellationToken = default)
    => legacy.BackupSavesAsync(request, cancellationToken);

  public Task<SaveOperationResultDto> RestoreAsync(SaveRestoreRequest request, CancellationToken cancellationToken = default)
    => legacy.RestoreSavesAsync(request, cancellationToken);

  public Task<SaveOperationResultDto> ImportAsync(SaveImportRequest request, CancellationToken cancellationToken = default)
    => legacy.ImportSavesAsync(request, cancellationToken);

  public Task<SaveOperationResultDto> ExportAsync(SaveExportRequest request, CancellationToken cancellationToken = default)
    => legacy.ExportSavesAsync(request, cancellationToken);

  public Task<EmulatorConfigDto> GetConfigAsync(CancellationToken cancellationToken = default)
    => legacy.ReadConfigAsync(cancellationToken);

  public Task<EmulatorConfigDto> SaveConfigAsync(UpdateConfigRequest request, CancellationToken cancellationToken = default)
    => legacy.SaveConfigAsync(request, cancellationToken);

  public Task<LauncherStatusDto> StartAsync(LauncherStartRequest request, CancellationToken cancellationToken = default)
    => legacy.LaunchXeniaAsync(request, cancellationToken);

  public Task<LauncherStatusDto> StopAsync(CancellationToken cancellationToken = default)
    => legacy.StopXeniaAsync(cancellationToken);

  public Task<LauncherStatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
    => legacy.ReadXeniaStatusAsync(cancellationToken);
}
