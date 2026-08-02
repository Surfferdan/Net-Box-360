using Microsoft.AspNetCore.Mvc;
using NetBox.Adapters.Xenia;
using NetBox.Models;
using XeniaManager.Core.Services;
using XeniaManager.Models;

namespace XeniaManager.Api.Controllers;

[ApiController]
public sealed class LauncherController : ControllerBase
{
  [HttpPost("api/xenia/start")]
  public Task<LauncherStatusDto> Start([FromBody] LauncherStartRequest request, [FromServices] ILauncherService launcher, CancellationToken cancellationToken)
    => launcher.StartAsync(request, cancellationToken);

  [HttpPost("api/xenia/stop")]
  public Task<LauncherStatusDto> Stop([FromServices] ILauncherService launcher, CancellationToken cancellationToken)
    => launcher.StopAsync(cancellationToken);

  [HttpGet("api/xenia/status")]
  public Task<LauncherStatusDto> Status([FromServices] ILauncherService launcher, CancellationToken cancellationToken)
    => launcher.StatusAsync(cancellationToken);

  [HttpGet("api/cloudmorph/status")]
  public Task<CloudMorphHealthResponse> CloudMorphStatus([FromServices] ICloudMorphAdapter cloudMorph, CancellationToken cancellationToken)
    => cloudMorph.GetHealthAsync(cancellationToken);
}
