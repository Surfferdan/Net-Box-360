using Microsoft.AspNetCore.Mvc;
using XeniaManager.Core.Services;
using XeniaManager.Models;

namespace XeniaManager.Api.Controllers;

[ApiController]
[Route("api/profiles")]
public sealed class ProfilesController : ControllerBase
{
  [HttpGet]
  public Task<IReadOnlyList<ProfileDto>> GetProfiles([FromServices] IProfileService profiles, CancellationToken cancellationToken)
    => profiles.GetProfilesAsync(cancellationToken);

  [HttpGet("{id}")]
  public async Task<ActionResult<ProfileDto>> GetProfile(string id, [FromServices] IProfileService profiles, CancellationToken cancellationToken)
  {
    var profile = await profiles.GetProfileAsync(id, cancellationToken).ConfigureAwait(false);
    return profile is null ? NotFound() : Ok(profile);
  }

  [HttpPost]
  public Task<ProfileDto> Create([FromBody] CreateProfileRequest request, [FromServices] IProfileService profiles, CancellationToken cancellationToken)
    => profiles.CreateProfileAsync(request, cancellationToken);

  [HttpPut("{id}")]
  public async Task<ActionResult<ProfileDto>> Update(string id, [FromBody] UpdateProfileRequest request, [FromServices] IProfileService profiles, CancellationToken cancellationToken)
  {
    var profile = await profiles.UpdateProfileAsync(id, request, cancellationToken).ConfigureAwait(false);
    return profile is null ? NotFound() : Ok(profile);
  }

  [HttpDelete("{id}")]
  public async Task<IActionResult> Delete(string id, [FromServices] IProfileService profiles, CancellationToken cancellationToken)
  {
    var deleted = await profiles.DeleteProfileAsync(id, cancellationToken).ConfigureAwait(false);
    return deleted ? NoContent() : NotFound();
  }
}
