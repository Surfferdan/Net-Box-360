using XeniaManager.Core.Abstractions;
using XeniaManager.Core.Abstractions.Adapters;
using XeniaManager.Models;

namespace XeniaManager.Core.Services;

public sealed class SaveService : ISaveService
{
  private readonly IXeniaSaveAdapter saveAdapter;
  private readonly IBackendEventSink eventSink;

  public SaveService(IXeniaSaveAdapter saveAdapter, IBackendEventSink eventSink)
  {
    this.saveAdapter = saveAdapter;
    this.eventSink = eventSink;
  }

  public Task<IReadOnlyList<SaveFileDto>> GetSavesAsync(string profileId, CancellationToken cancellationToken = default)
    => saveAdapter.GetSavesAsync(profileId, cancellationToken);

  public async Task<SaveOperationResultDto> BackupAsync(SaveBackupRequest request, CancellationToken cancellationToken = default)
  {
    var result = await saveAdapter.BackupAsync(request, cancellationToken).ConfigureAwait(false);
    if (result.Success)
    {
      await eventSink.PublishAsync(new BackendEventDto(
        "SaveExported",
        DateTimeOffset.UtcNow,
        new Dictionary<string, string> { ["profileId"] = request.ProfileId, ["destination"] = request.DestinationPath }), cancellationToken).ConfigureAwait(false);
    }
    return result;
  }

  public async Task<SaveOperationResultDto> RestoreAsync(SaveRestoreRequest request, CancellationToken cancellationToken = default)
  {
    var result = await saveAdapter.RestoreAsync(request, cancellationToken).ConfigureAwait(false);
    if (result.Success)
    {
      await eventSink.PublishAsync(new BackendEventDto(
        "SaveImported",
        DateTimeOffset.UtcNow,
        new Dictionary<string, string> { ["profileId"] = request.ProfileId, ["source"] = request.SourcePath }), cancellationToken).ConfigureAwait(false);
    }
    return result;
  }

  public Task<SaveOperationResultDto> ImportAsync(SaveImportRequest request, CancellationToken cancellationToken = default)
    => saveAdapter.ImportAsync(request, cancellationToken);

  public Task<SaveOperationResultDto> ExportAsync(SaveExportRequest request, CancellationToken cancellationToken = default)
    => saveAdapter.ExportAsync(request, cancellationToken);
}
