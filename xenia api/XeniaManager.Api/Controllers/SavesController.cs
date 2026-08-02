using Microsoft.AspNetCore.Mvc;
using XeniaManager.Core.Services;
using XeniaManager.Models;

namespace XeniaManager.Api.Controllers;

[ApiController]
public sealed class SavesController : ControllerBase
{
  [HttpGet("api/profiles/{id}/saves")]
  public Task<IReadOnlyList<SaveFileDto>> GetProfileSaves(string id, [FromServices] ISaveService saves, CancellationToken cancellationToken)
    => saves.GetSavesAsync(id, cancellationToken);

  [HttpPost("api/saves/backup")]
  public Task<SaveOperationResultDto> Backup([FromBody] SaveBackupRequest request, [FromServices] ISaveService saves, CancellationToken cancellationToken)
    => saves.BackupAsync(request, cancellationToken);

  [HttpPost("api/saves/restore")]
  public Task<SaveOperationResultDto> Restore([FromBody] SaveRestoreRequest request, [FromServices] ISaveService saves, CancellationToken cancellationToken)
    => saves.RestoreAsync(request, cancellationToken);
}
