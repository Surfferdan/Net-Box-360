using XeniaManager.Adapters.Legacy;
using XeniaManager.Models;

namespace XeniaManager.Api.Adapters;

public sealed class NotConfiguredLegacyFacade : IXeniaManagerLegacyFacade
{
  private static InvalidOperationException Missing() =>
    new("No IXeniaManagerLegacyFacade implementation was registered. Wire existing Xenia Manager backend logic in API composition root.");

  public Task<IReadOnlyList<ProfileDto>> EnumerateProfilesAsync(CancellationToken cancellationToken = default) => throw Missing();
  public Task<ProfileDto?> ReadProfileAsync(string profileId, CancellationToken cancellationToken = default) => throw Missing();
  public Task<ProfileDto> CreateProfileAsync(CreateProfileRequest request, CancellationToken cancellationToken = default) => throw Missing();
  public Task<ProfileDto?> RenameProfileAsync(string profileId, string gamertag, CancellationToken cancellationToken = default) => throw Missing();
  public Task<bool> DeleteProfileAsync(string profileId, CancellationToken cancellationToken = default) => throw Missing();
  public Task<bool> SetActiveProfileAsync(string profileId, CancellationToken cancellationToken = default) => throw Missing();
  public Task<IReadOnlyList<AchievementDto>> EnumerateAchievementsAsync(string profileId, CancellationToken cancellationToken = default) => throw Missing();
  public Task<IReadOnlyList<SaveFileDto>> EnumerateSavesAsync(string profileId, CancellationToken cancellationToken = default) => throw Missing();
  public Task<SaveOperationResultDto> BackupSavesAsync(SaveBackupRequest request, CancellationToken cancellationToken = default) => throw Missing();
  public Task<SaveOperationResultDto> RestoreSavesAsync(SaveRestoreRequest request, CancellationToken cancellationToken = default) => throw Missing();
  public Task<SaveOperationResultDto> ImportSavesAsync(SaveImportRequest request, CancellationToken cancellationToken = default) => throw Missing();
  public Task<SaveOperationResultDto> ExportSavesAsync(SaveExportRequest request, CancellationToken cancellationToken = default) => throw Missing();
  public Task<EmulatorConfigDto> ReadConfigAsync(CancellationToken cancellationToken = default) => throw Missing();
  public Task<EmulatorConfigDto> SaveConfigAsync(UpdateConfigRequest request, CancellationToken cancellationToken = default) => throw Missing();
  public Task<LauncherStatusDto> LaunchXeniaAsync(LauncherStartRequest request, CancellationToken cancellationToken = default) => throw Missing();
  public Task<LauncherStatusDto> StopXeniaAsync(CancellationToken cancellationToken = default) => throw Missing();
  public Task<LauncherStatusDto> ReadXeniaStatusAsync(CancellationToken cancellationToken = default) => throw Missing();
}
