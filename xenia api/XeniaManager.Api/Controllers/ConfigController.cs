using Microsoft.AspNetCore.Mvc;
using XeniaManager.Core.Services;
using XeniaManager.Models;

namespace XeniaManager.Api.Controllers;

[ApiController]
[Route("api/config")]
public sealed class ConfigController : ControllerBase
{
  [HttpGet]
  public Task<EmulatorConfigDto> Get([FromServices] IConfigService config, CancellationToken cancellationToken)
    => config.GetConfigAsync(cancellationToken);

  [HttpPut]
  public Task<EmulatorConfigDto> Put([FromBody] UpdateConfigRequest request, [FromServices] IConfigService config, CancellationToken cancellationToken)
    => config.UpdateConfigAsync(request, cancellationToken);
}
