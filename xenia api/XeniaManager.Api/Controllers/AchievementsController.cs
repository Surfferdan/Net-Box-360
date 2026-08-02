using Microsoft.AspNetCore.Mvc;
using XeniaManager.Core.Services;
using XeniaManager.Models;

namespace XeniaManager.Api.Controllers;

[ApiController]
[Route("api/profiles/{id}/achievements")]
public sealed class AchievementsController : ControllerBase
{
  [HttpGet]
  public Task<AchievementSummaryDto> Get(string id, [FromServices] IAchievementService achievements, CancellationToken cancellationToken)
    => achievements.GetAchievementsAsync(id, cancellationToken);
}
