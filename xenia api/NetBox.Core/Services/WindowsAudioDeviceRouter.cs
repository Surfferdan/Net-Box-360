using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NAudio.CoreAudioApi;
using NetBox.Core.Abstractions;

namespace NetBox.Core.Services;

public sealed class WindowsAudioDeviceRouter : IAudioDeviceRouter
{
  private readonly AudioRoutingOptions options;
  private readonly ILogger<WindowsAudioDeviceRouter> logger;
  private readonly SemaphoreSlim gate = new(1, 1);
  private EndpointSnapshot? previousDefaults;
  private bool endpointSwitchUnsupported;

  public WindowsAudioDeviceRouter(IOptions<AudioRoutingOptions> options, ILogger<WindowsAudioDeviceRouter> logger)
  {
    this.options = options.Value;
    this.logger = logger;
  }

  public async Task<AudioRouteResult> PrepareForSessionAsync(CancellationToken cancellationToken = default)
  {
    if (!OperatingSystem.IsWindows() || !options.RouteToVirtualSink)
    {
      return new AudioRouteResult(false, NormalizeCaptureInputDevice(options.CaptureInputDevice));
    }

    await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var device = ResolveVirtualSink();
      if (device is null)
      {
        if (options.RequireVirtualSink)
        {
          throw new InvalidOperationException("Virtual sink required but no matching render device was found.");
        }

        logger.LogWarning("Virtual sink routing enabled, but no matching render device was found. Keeping current default endpoint.");
        return new AudioRouteResult(false, ResolveFallbackCaptureInputDevice(), "virtual-sink-not-found");
      }

      if (options.SwitchDefaultOutputToVirtualSink)
      {
        if (endpointSwitchUnsupported)
        {
          // A previous session already proved the endpoint switch is
          // rejected on this host. Skip retrying it, but - critically - do
          // NOT fall through to the happy path below: the switch still
          // never happened, so the configured virtual-sink capture device
          // is still dead air. Keep auto-healing via the fallback on every
          // subsequent session, the same as the first time this was hit.
          logger.LogDebug("Default endpoint switching previously marked unsupported; skipping switch attempt.");
          return new AudioRouteResult(false, ResolveFallbackCaptureInputDevice(), "endpoint-switch-unsupported");
        }

        previousDefaults ??= CaptureCurrentDefaults();
        if (!TrySetDefaultEndpointForAllRoles(device.ID, out var routeError))
        {
          if (IsUnsupportedEndpointSwitch(routeError))
          {
            endpointSwitchUnsupported = true;
            logger.LogWarning(
              routeError,
              "Default render endpoint switching is unsupported on this host; disabling further switch attempts for this API runtime.");
          }
          else
          {
            logger.LogWarning(
              routeError,
              "Default render endpoint switch to virtual sink failed for {DeviceName}; continuing without endpoint switch.",
              device.FriendlyName);
          }

          if (options.RequireVirtualSink)
          {
            throw new InvalidOperationException("Virtual sink routing is required but default endpoint switch failed.", routeError);
          }

          // Fail open: keep sessions launchable. Do NOT keep pointing capture
          // at the configured virtual-sink device here - if the endpoint
          // switch didn't succeed, Xenia's audio never actually reached that
          // device, so ffmpeg would silently capture dead air (this was the
          // "no audio sent to browser" bug). Instead, auto-heal by
          // loopback-capturing whatever the CURRENT default render device
          // actually is, so streamed audio keeps working automatically
          // without any manual Windows audio routing.
          var degradedReason = IsUnsupportedEndpointSwitch(routeError) ? "endpoint-switch-unsupported" : "endpoint-switch-failed";
          return new AudioRouteResult(false, ResolveFallbackCaptureInputDevice(), degradedReason);
        }

        logger.LogInformation("Default render endpoint switched to virtual sink: {DeviceName}", device.FriendlyName);
      }

      var captureInput = NormalizeCaptureInputDevice(options.CaptureInputDevice)
        ?? $"audio={device.FriendlyName}";

      return new AudioRouteResult(true, captureInput);
    }
    finally
    {
      gate.Release();
    }
  }

  public async Task RestoreAfterSessionAsync(CancellationToken cancellationToken = default)
  {
    if (!OperatingSystem.IsWindows() || !options.RouteToVirtualSink || !options.SwitchDefaultOutputToVirtualSink || !options.RestoreDefaultOutputOnStop)
    {
      return;
    }

    await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      if (previousDefaults is null)
      {
        return;
      }

      if (endpointSwitchUnsupported)
      {
        // The switch to the virtual sink never actually took effect (it was
        // rejected up front), so there is nothing to restore - and retrying
        // here would just log another misleading COMException for a switch
        // we already know this host rejects. Silently drop the snapshot.
        logger.LogDebug("Skipping default render endpoint restore; endpoint switching was already marked unsupported for this session.");
        previousDefaults = null;
        return;
      }

      var consoleRestored = RestoreDefault(previousDefaults.ConsoleDeviceId, ERole.Console, logger);
      var multimediaRestored = RestoreDefault(previousDefaults.MultimediaDeviceId, ERole.Multimedia, logger);
      var communicationsRestored = RestoreDefault(previousDefaults.CommunicationsDeviceId, ERole.Communications, logger);

      if (consoleRestored && multimediaRestored && communicationsRestored)
      {
        logger.LogInformation("Restored previous default render endpoints after session stop.");
      }
      else
      {
        logger.LogWarning("Default render endpoint restore only partially succeeded after session stop; some roles may still point at the virtual sink.");
      }

      previousDefaults = null;
    }
    finally
    {
      gate.Release();
    }
  }

  public string? ResolvePreferredCaptureInputDevice()
  {
    var configured = NormalizeCaptureInputDevice(options.CaptureInputDevice);
    if (!string.IsNullOrWhiteSpace(configured))
    {
      return configured;
    }

    if (!OperatingSystem.IsWindows() || !options.RouteToVirtualSink)
    {
      return null;
    }

    var device = ResolveVirtualSink();
    return device is null ? null : $"audio={device.FriendlyName}";
  }

  private MMDevice? ResolveVirtualSink()
  {
    var marker = options.VirtualSinkNameContains?.Trim();
    if (string.IsNullOrWhiteSpace(marker))
    {
      return null;
    }

    using var enumerator = new MMDeviceEnumerator();
    var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
    foreach (var device in devices)
    {
      if (device.FriendlyName.Contains(marker, StringComparison.OrdinalIgnoreCase))
      {
        return device;
      }
    }

    return null;
  }

  /// <summary>
  /// Resolves a capture input for ffmpeg's WASAPI demuxer when we can't
  /// guarantee that Xenia's audio actually reached the configured
  /// virtual-sink/capture device (e.g. the default-endpoint switch was
  /// unsupported or failed). ffmpeg's WASAPI source automatically performs a
  /// loopback capture when given a render (output) device instead of a
  /// capture (input) device, so pointing it at whatever the CURRENT default
  /// render device is lets the stream keep carrying real audio automatically
  /// - no manual Windows audio routing and no extra native audio backend
  /// required. Falls back to the configured device string only if the
  /// current default render device can't be resolved at all.
  /// </summary>
  private string? ResolveFallbackCaptureInputDevice()
  {
    try
    {
      using var enumerator = new MMDeviceEnumerator();
      var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
      return $"audio={device.FriendlyName}";
    }
    catch (Exception ex)
    {
      logger.LogDebug(ex, "Could not resolve current default render device for audio capture fallback; using configured capture device instead.");
      return NormalizeCaptureInputDevice(options.CaptureInputDevice);
    }
  }

  private static string? NormalizeCaptureInputDevice(string? captureInputDevice)
  {
    if (string.IsNullOrWhiteSpace(captureInputDevice))
    {
      return null;
    }

    var value = captureInputDevice.Trim();
    if (value.Equals("default", StringComparison.OrdinalIgnoreCase))
    {
      return "default";
    }

    if (value.StartsWith("audio=", StringComparison.OrdinalIgnoreCase))
    {
      return value;
    }

    return $"audio={value}";
  }

  private static EndpointSnapshot CaptureCurrentDefaults()
  {
    using var enumerator = new MMDeviceEnumerator();
    return new EndpointSnapshot(
      SafeGetDefaultDeviceId(enumerator, Role.Console),
      SafeGetDefaultDeviceId(enumerator, Role.Multimedia),
      SafeGetDefaultDeviceId(enumerator, Role.Communications));
  }

  private static string? SafeGetDefaultDeviceId(MMDeviceEnumerator enumerator, Role role)
  {
    try
    {
      return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, role).ID;
    }
    catch
    {
      return null;
    }
  }

  [SupportedOSPlatform("windows")]
  private static bool RestoreDefault(string? deviceId, ERole role, ILogger logger)
  {
    if (string.IsNullOrWhiteSpace(deviceId))
    {
      return true;
    }

    if (!TrySetDefaultEndpoint(deviceId, role, out var ex))
    {
      logger.LogWarning(ex, "Failed to restore default render endpoint for role {Role}.", role);
      return false;
    }

    return true;
  }

  [SupportedOSPlatform("windows")]
  private static bool TrySetDefaultEndpointForAllRoles(string deviceId, out Exception? exception)
  {
    if (!TrySetDefaultEndpoint(deviceId, ERole.Console, out exception))
    {
      return false;
    }

    if (!TrySetDefaultEndpoint(deviceId, ERole.Multimedia, out exception))
    {
      return false;
    }

    if (!TrySetDefaultEndpoint(deviceId, ERole.Communications, out exception))
    {
      return false;
    }

    exception = null;
    return true;
  }

  [SupportedOSPlatform("windows")]
  private static bool TrySetDefaultEndpoint(string deviceId, ERole role, out Exception? exception)
  {
    try
    {
      var policyConfigType = Type.GetTypeFromCLSID(new Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9"), throwOnError: true)
        ?? throw new InvalidOperationException("PolicyConfig COM type is unavailable on this system.");
      var policyConfig = (IPolicyConfig)(Activator.CreateInstance(policyConfigType)
        ?? throw new InvalidOperationException("Failed to create PolicyConfig COM instance."));
      var hr = policyConfig.SetDefaultEndpoint(deviceId, role);
      Marshal.ThrowExceptionForHR(hr);
      exception = null;
      return true;
    }
    catch (Exception ex)
    {
      exception = ex;
      return false;
    }
  }

  private sealed record EndpointSnapshot(string? ConsoleDeviceId, string? MultimediaDeviceId, string? CommunicationsDeviceId);

  private static bool IsUnsupportedEndpointSwitch(Exception? exception)
  {
    for (var current = exception; current is not null; current = current.InnerException)
    {
      if (current is COMException comEx && comEx.HResult == unchecked((int)0x8007065E))
      {
        return true;
      }
    }

    return false;
  }

  private enum ERole
  {
    Console = 0,
    Multimedia = 1,
    Communications = 2,
  }

  [StructLayout(LayoutKind.Sequential)]
  private struct PropertyKey
  {
    public Guid fmtid;
    public int pid;
  }

  [ComImport]
  [Guid("f8679f50-850a-41cf-9c72-430f290290c8")]
  [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  private interface IPolicyConfig
  {
    int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr ppFormat);
    int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, int bDefault, IntPtr ppFormat);
    int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr endpointFormat, IntPtr mixFormat);
    int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, int bDefault, IntPtr pmftDefaultPeriod, IntPtr pmftMinimumPeriod);
    int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr pmftPeriod);
    int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr pMode);
    int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr mode);
    int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, int bFxStore, ref PropertyKey key, IntPtr pv);
    int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, int bFxStore, ref PropertyKey key, IntPtr pv);
    int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, ERole role);
    int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, int bVisible);
  }
}
