using Microsoft.AspNetCore.Mvc;
using NetBox.Core.Abstractions;
using NetBox.Models;

namespace XeniaManager.Api.Controllers;

[ApiController]
[Route("api/session")]
public sealed class SessionController : ControllerBase
{
  [HttpPost("start")]
  public async Task<ActionResult<StartGameSessionResponse>> Start(
    [FromBody] StartGameSessionRequest request,
    [FromServices] IGameSessionService sessions,
    [FromServices] ILogger<SessionController> logger,
    [FromServices] IWebHostEnvironment env,
    CancellationToken cancellationToken)
  {
    var token = ReadBearerToken(Request.Headers.Authorization.ToString());
    if (token is null)
    {
      return Unauthorized(new { success = false, error = "Missing bearer token." });
    }

    try
    {
      var response = await sessions.StartAsync(token, request, cancellationToken).ConfigureAwait(false);
      return Ok(response);
    }
    catch (UnauthorizedAccessException ex)
    {
      return Unauthorized(new { success = false, error = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
      return Conflict(new { success = false, error = ex.Message });
    }
    catch (ArgumentException ex)
    {
      return BadRequest(new { success = false, error = ex.Message });
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "Unexpected error while starting session.");
      var error = env.IsDevelopment()
        ? $"Session service is temporarily unavailable. {ex.Message}"
        : "Session service is temporarily unavailable.";
      return StatusCode(StatusCodes.Status503ServiceUnavailable, new { success = false, error });
    }
  }

  [HttpGet("{id}")]
  public async Task<ActionResult<GameSessionStatusResponse>> Status(
    string id,
    [FromServices] IGameSessionService sessions,
    [FromServices] ILogger<SessionController> logger,
    [FromServices] IWebHostEnvironment env,
    CancellationToken cancellationToken)
  {
    var token = ReadBearerToken(Request.Headers.Authorization.ToString());
    if (token is null)
    {
      return Unauthorized(new { success = false, error = "Missing bearer token." });
    }

    try
    {
      var response = await sessions.GetAsync(token, id, cancellationToken).ConfigureAwait(false);
      return response is null ? NotFound(new { success = false, error = "Session not found." }) : Ok(response);
    }
    catch (UnauthorizedAccessException ex)
    {
      return Unauthorized(new { success = false, error = ex.Message });
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "Unexpected error while loading session status for sessionId={SessionId}.", id);
      var error = env.IsDevelopment()
        ? $"Session service is temporarily unavailable. {ex.Message}"
        : "Session service is temporarily unavailable.";
      return StatusCode(StatusCodes.Status503ServiceUnavailable, new { success = false, error });
    }
  }

  [HttpGet("active")]
  public async Task<ActionResult<GameSessionStatusResponse>> Reconnect(
    [FromServices] IGameSessionService sessions,
    [FromServices] ILogger<SessionController> logger,
    [FromServices] IWebHostEnvironment env,
    CancellationToken cancellationToken)
  {
    var token = ReadBearerToken(Request.Headers.Authorization.ToString());
    if (token is null)
    {
      return Unauthorized(new { success = false, error = "Missing bearer token." });
    }

    try
    {
      var response = await sessions.ReconnectAsync(token, cancellationToken).ConfigureAwait(false);
      return response is null ? NotFound(new { success = false, error = "Session not found." }) : Ok(response);
    }
    catch (UnauthorizedAccessException ex)
    {
      return Unauthorized(new { success = false, error = ex.Message });
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "Unexpected error while reconnecting active session.");
      var error = env.IsDevelopment()
        ? $"Session service is temporarily unavailable. {ex.Message}"
        : "Session service is temporarily unavailable.";
      return StatusCode(StatusCodes.Status503ServiceUnavailable, new { success = false, error });
    }
  }

  [HttpPost("{id}/stop")]
  public async Task<ActionResult<StopGameSessionResponse>> Stop(
    string id,
    [FromServices] IGameSessionService sessions,
    [FromServices] ILogger<SessionController> logger,
    [FromServices] IWebHostEnvironment env,
    CancellationToken cancellationToken)
  {
    var token = ReadBearerToken(Request.Headers.Authorization.ToString());
    if (token is null)
    {
      return Unauthorized(new { success = false, error = "Missing bearer token." });
    }

    try
    {
      var response = await sessions.StopAsync(token, id, cancellationToken).ConfigureAwait(false);
      return Ok(response);
    }
    catch (UnauthorizedAccessException ex)
    {
      if (ex.Message.Contains("Only the session owner can end this session", StringComparison.OrdinalIgnoreCase))
      {
        return StatusCode(StatusCodes.Status403Forbidden, new { success = false, error = ex.Message });
      }

      return Unauthorized(new { success = false, error = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
      return NotFound(new { success = false, error = ex.Message });
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "Unexpected error while stopping sessionId={SessionId}.", id);
      var error = env.IsDevelopment()
        ? $"Session service is temporarily unavailable. {ex.Message}"
        : "Session service is temporarily unavailable.";
      return StatusCode(StatusCodes.Status503ServiceUnavailable, new { success = false, error });
    }
  }

  [HttpPost("{id}/join")]
  public async Task<ActionResult<JoinGameSessionResponse>> Join(
    string id,
    [FromServices] IInputManager inputManager,
    [FromServices] ILogger<SessionController> logger,
    [FromServices] IWebHostEnvironment env,
    CancellationToken cancellationToken)
  {
    var token = ReadBearerToken(Request.Headers.Authorization.ToString());
    if (token is null)
    {
      return Unauthorized(new { success = false, error = "Missing bearer token." });
    }

    try
    {
      var response = await inputManager.JoinAsync(token, id, cancellationToken).ConfigureAwait(false);
      return Ok(response);
    }
    catch (UnauthorizedAccessException ex)
    {
      return Unauthorized(new { success = false, error = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
      if (ex.Message.Contains("Session is full", StringComparison.OrdinalIgnoreCase))
      {
        return Conflict(new { success = false, error = ex.Message });
      }

      return NotFound(new { success = false, error = ex.Message });
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "Unexpected error while joining sessionId={SessionId}.", id);
      var error = env.IsDevelopment()
        ? $"Session service is temporarily unavailable. {ex.Message}"
        : "Session service is temporarily unavailable.";
      return StatusCode(StatusCodes.Status503ServiceUnavailable, new { success = false, error });
    }
  }

  [HttpPost("{id}/leave")]
  public async Task<ActionResult<LeaveGameSessionResponse>> Leave(
    string id,
    [FromServices] IGameSessionService sessions,
    [FromServices] ILogger<SessionController> logger,
    [FromServices] IWebHostEnvironment env,
    CancellationToken cancellationToken)
  {
    var token = ReadBearerToken(Request.Headers.Authorization.ToString());
    if (token is null)
    {
      return Unauthorized(new { success = false, error = "Missing bearer token." });
    }

    try
    {
      var response = await sessions.LeaveAsync(token, id, cancellationToken).ConfigureAwait(false);
      return Ok(response);
    }
    catch (UnauthorizedAccessException ex)
    {
      if (ex.Message.Contains("cannot leave an active session", StringComparison.OrdinalIgnoreCase))
      {
        return StatusCode(StatusCodes.Status403Forbidden, new { success = false, error = ex.Message });
      }

      return Unauthorized(new { success = false, error = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
      return NotFound(new { success = false, error = ex.Message });
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "Unexpected error while leaving sessionId={SessionId}.", id);
      var error = env.IsDevelopment()
        ? $"Session service is temporarily unavailable. {ex.Message}"
        : "Session service is temporarily unavailable.";
      return StatusCode(StatusCodes.Status503ServiceUnavailable, new { success = false, error });
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
