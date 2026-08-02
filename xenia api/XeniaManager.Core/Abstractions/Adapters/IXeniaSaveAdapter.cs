using XeniaManager.Models;

namespace XeniaManager.Core.Abstractions.Adapters;

public interface IXeniaSaveAdapter
{
  Task<IReadOnlyList<SaveFileDto>> GetSavesAsync(string profileId, CancellationToken cancellationToken = default);
  Task<SaveOperationResultDto> BackupAsync(SaveBackupRequest request, CancellationToken cancellationToken = default);
  Task<SaveOperationResultDto> RestoreAsync(SaveRestoreRequest request, CancellationToken cancellationToken = default);
  Task<SaveOperationResultDto> ImportAsync(SaveImportRequest request, CancellationToken cancellationToken = default);
  Task<SaveOperationResultDto> ExportAsync(SaveExportRequest request, CancellationToken cancellationToken = default);
}
