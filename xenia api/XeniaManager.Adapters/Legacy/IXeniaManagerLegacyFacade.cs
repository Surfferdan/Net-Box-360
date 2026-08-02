using XeniaManager.Models;

namespace XeniaManager.Adapters.Legacy;

public interface IXeniaManagerLegacyFacade
{
  Task<IReadOnlyList<ProfileDto>> EnumerateProfilesAsync(CancellationToken cancellationToken = default);
  Task<ProfileDto?> ReadProfileAsync(string profileId, CancellationToken cancellationToken = default);
  Task<ProfileDto> CreateProfileAsync(CreateProfileRequest request, CancellationToken cancellationToken = default);
  Task<ProfileDto?> RenameProfileAsync(string profileId, string gamertag, CancellationToken cancellationToken = default);
  Task<bool> DeleteProfileAsync(string profileId, CancellationToken cancellationToken = default);
  Task<bool> SetActiveProfileAsync(string profileId, CancellationToken cancellationToken = default);

  Task<IReadOnlyList<AchievementDto>> EnumerateAchievementsAsync(string profileId, CancellationToken cancellationToken = default);
  Task<IReadOnlyList<SaveFileDto>> EnumerateSavesAsync(string profileId, CancellationToken cancellationToken = default);
  Task<SaveOperationResultDto> BackupSavesAsync(SaveBackupRequest request, CancellationToken cancellationToken = default);
  Task<SaveOperationResultDto> RestoreSavesAsync(SaveRestoreRequest request, CancellationToken cancellationToken = default);
  Task<SaveOperationResultDto> ImportSavesAsync(SaveImportRequest request, CancellationToken cancellationToken = default);
  Task<SaveOperationResultDto> ExportSavesAsync(SaveExportRequest request, CancellationToken cancellationToken = default);

  Task<EmulatorConfigDto> ReadConfigAsync(CancellationToken cancellationToken = default);
  Task<EmulatorConfigDto> SaveConfigAsync(UpdateConfigRequest request, CancellationToken cancellationToken = default);

  Task<LauncherStatusDto> LaunchXeniaAsync(LauncherStartRequest request, CancellationToken cancellationToken = default);
  Task<LauncherStatusDto> StopXeniaAsync(CancellationToken cancellationToken = default);
  Task<LauncherStatusDto> ReadXeniaStatusAsync(CancellationToken cancellationToken = default);
}
