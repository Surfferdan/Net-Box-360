using Microsoft.AspNetCore.Mvc;
using NetBox.Core.Abstractions;
using NetBox.Models;

namespace XeniaManager.Api.Controllers;

[ApiController]
[Route("api/profile")]
public sealed class NetBoxProfileController : ControllerBase
{
  [HttpGet("me")]
  public async Task<IActionResult> Me([FromServices] IAccountService accounts, CancellationToken cancellationToken)
  {
    var token = ReadBearerToken(Request.Headers.Authorization.ToString());
    if (token is null)
    {
      return Unauthorized(new { success = false, error = "Missing bearer token." });
    }

    var profile = await accounts.GetCurrentProfileAsync(token, cancellationToken).ConfigureAwait(false);
    return profile is null ? Unauthorized(new { success = false, error = "Invalid or expired session." }) : Ok(profile);
  }

  [HttpPut("me/customization")]
  [HttpPost("me/customization")]
  public async Task<IActionResult> UpdateCustomization(
    [FromBody] UpdateProfileCustomizationRequest request,
    [FromServices] IAccountService accounts,
    CancellationToken cancellationToken)
  {
    var token = ReadBearerToken(Request.Headers.Authorization.ToString());
    if (token is null)
    {
      return Unauthorized(new { success = false, error = "Missing bearer token." });
    }

    try
    {
      var profile = await accounts.UpdateCurrentProfileCustomizationAsync(token, request, cancellationToken).ConfigureAwait(false);
      return profile is null
        ? Unauthorized(new { success = false, error = "Invalid or expired session." })
        : Ok(profile);
    }
    catch (ArgumentException ex)
    {
      return BadRequest(new { success = false, error = ex.Message });
    }
  }

  private static string? ReadBearerToken(string authorizationHeader)
  {
    if (string.IsNullOrWhiteSpace(authorizationHeader) || !authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
      return null;
    }

    return authorizationHeader[7..].Trim();
  }
}
