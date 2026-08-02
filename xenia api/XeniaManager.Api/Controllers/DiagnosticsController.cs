using Microsoft.AspNetCore.Mvc;
using NetBox.Adapters.Xenia;
using NetBox.Core.Services;
using Microsoft.Extensions.Options;
using System.Security.Principal;
using System.Xml.Linq;
using XeniaManager.Core.Services;

namespace XeniaManager.Api.Controllers;

public sealed record DiagnosticsResponse(
  string ApiStatus,
  DateTimeOffset ServerTimeUtc,
  LauncherDiagnostics Launcher,
  CloudMorphDiagnostics CloudMorph,
  VirtualDisplayDiagnostics VirtualDisplay);

public sealed record LauncherDiagnostics(bool IsRunning, int? ProcessId, string? ExecutablePath);

public sealed record CloudMorphDiagnostics(
  string CircuitBreakerState,
  int ConsecutiveFailures,
  string Status,
  bool CaptureReady,
  bool StreamReady,
  int ActiveSessions);

public sealed record VirtualDisplayDiagnostics(
  bool Enabled,
  bool RequireService,
  bool UseSyntheticFallback,
  bool IsAdministrator,
  bool RuntimeSettingsPresent,
  int? MonitorCount);

/// <summary>
/// Fast, dependency-light diagnostics for local dev troubleshooting: exposes the
/// combined launcher + CloudMorph health without requiring an authenticated
/// session, so it can be polled during startup/incident triage.
/// </summary>
[ApiController]
[Route("api/diagnostics")]
public sealed class DiagnosticsController : ControllerBase
{
  [HttpGet]
  public async Task<ActionResult<DiagnosticsResponse>> Get(
    [FromServices] ILauncherService launcher,
    [FromServices] ICloudMorphAdapter cloudMorph,
    [FromServices] ICloudMorphCircuitBreaker circuitBreaker,
    [FromServices] IOptions<VirtualDisplayOptions> virtualDisplayOptions,
    CancellationToken cancellationToken)
  {
    var launcherStatus = await launcher.StatusAsync(cancellationToken).ConfigureAwait(false);
    var health = await cloudMorph.GetHealthAsync(cancellationToken).ConfigureAwait(false);

    var vddSettingsPath = Path.Combine(Environment.GetEnvironmentVariable("SystemDrive") ?? "C:", "VirtualDisplayDriver", "vdd_settings.xml");
    var runtimeSettingsPresent = System.IO.File.Exists(vddSettingsPath);
    var monitorCount = TryReadMonitorCount(vddSettingsPath);
    var isAdministrator = IsAdministrator();
    var vd = virtualDisplayOptions.Value;

    return Ok(new DiagnosticsResponse(
      "ok",
      DateTimeOffset.UtcNow,
      new LauncherDiagnostics(launcherStatus.IsRunning, launcherStatus.ProcessId, launcherStatus.ExecutablePath),
      new CloudMorphDiagnostics(
        circuitBreaker.State,
        circuitBreaker.ConsecutiveFailures,
        health.Status,
        health.CaptureReady,
        health.StreamReady,
        health.ActiveSessions),
      new VirtualDisplayDiagnostics(
        vd.Enabled,
        vd.RequireService,
        vd.UseSyntheticFallback,
        isAdministrator,
        runtimeSettingsPresent,
        monitorCount)));
  }

  private static bool IsAdministrator()
  {
    if (!OperatingSystem.IsWindows())
    {
      return false;
    }

    try
    {
      using var identity = WindowsIdentity.GetCurrent();
      var principal = new WindowsPrincipal(identity);
      return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
    catch
    {
      return false;
    }
  }

  private static int? TryReadMonitorCount(string settingsPath)
  {
    try
    {
      if (!System.IO.File.Exists(settingsPath))
      {
        return null;
      }

      var doc = XDocument.Load(settingsPath);
      var countValue = doc.Root?.Element("monitors")?.Element("count")?.Value;
      return int.TryParse(countValue, out var parsed) ? parsed : null;
    }
    catch
    {
      return null;
    }
  }
}
