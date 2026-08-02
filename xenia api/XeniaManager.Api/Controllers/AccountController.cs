using Microsoft.AspNetCore.Mvc;
using NetBox.Core.Abstractions;
using NetBox.Models;

namespace XeniaManager.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class AccountController : ControllerBase
{
  [HttpPost("account/create")]
  public async Task<ActionResult<CreateAccountResponse>> Create(
    [FromBody] CreateAccountRequest request,
    [FromServices] IAccountService accounts,
    CancellationToken cancellationToken)
  {
    try
    {
      var response = await accounts.CreateAccountAsync(request, cancellationToken).ConfigureAwait(false);
      return Ok(response);
    }
    catch (ArgumentException ex)
    {
      return BadRequest(new { success = false, error = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
      return Conflict(new { success = false, error = ex.Message });
    }
  }

  [HttpPost("login")]
  public async Task<ActionResult<LoginResponse>> Login(
    [FromBody] LoginRequest request,
    [FromServices] IAccountService accounts,
    CancellationToken cancellationToken)
  {
    var response = await accounts.LoginAsync(request, cancellationToken).ConfigureAwait(false);
    return response is null ? Unauthorized(new { success = false }) : Ok(response);
  }

  [HttpPost("refresh")]
  public async Task<ActionResult<LoginResponse>> Refresh([FromServices] IAccountService accounts, CancellationToken cancellationToken)
  {
    var token = ReadBearerToken(Request.Headers.Authorization.ToString());
    if (token is null)
    {
      return Unauthorized(new { success = false });
    }

    var refreshed = await accounts.RefreshSessionAsync(token, cancellationToken).ConfigureAwait(false);
    return refreshed is null ? Unauthorized(new { success = false }) : Ok(refreshed);
  }

  [HttpPost("logout")]
  public async Task<IActionResult> Logout([FromServices] IAccountService accounts, CancellationToken cancellationToken)
  {
    var token = ReadBearerToken(Request.Headers.Authorization.ToString());
    if (token is null)
    {
      return Unauthorized(new { success = false });
    }

    _ = await accounts.LogoutAsync(token, cancellationToken).ConfigureAwait(false);
    return Ok(new LogoutResponse(true));
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
