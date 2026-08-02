using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using NetBox.Core.Abstractions;

namespace NetBox.Core.Services;

/// <summary>
/// Owns virtual display provisioning/release plus the monitor-assignment and
/// window-placement policy. The low-level "create/reuse a virtual monitor
/// slot" mechanism still runs behind <see cref="IVirtualDisplayProvider"/>
/// (an elevated external CLI on Windows), but interpreting a provisioned
/// display's slot/monitor-token into a concrete monitor rect - and acting on
/// that decision by repositioning the game window - is owned entirely here,
/// not scattered into the launcher.
/// </summary>
public sealed class DisplayManager : IDisplayManager
{
  private const uint SwpNoZOrder = 0x0004;
  private const uint SwpNoActivate = 0x0010;

  private readonly IVirtualDisplayProvider virtualDisplay;

  public DisplayManager(IVirtualDisplayProvider virtualDisplay)
  {
    this.virtualDisplay = virtualDisplay;
  }

  public Task<string?> ProvisionAsync(string sessionId, string gameTitle, CancellationToken cancellationToken = default)
    => virtualDisplay.ProvisionDisplayAsync(sessionId, gameTitle, cancellationToken);

  public Task ReleaseAsync(string sessionId, string? virtualDisplayId, CancellationToken cancellationToken = default)
    => virtualDisplay.ReleaseDisplayAsync(sessionId, virtualDisplayId, cancellationToken);

  public async Task<string?> ResolveWindowHandleAsync(int? processId, CancellationToken cancellationToken = default)
  {
    if (!processId.HasValue || processId.Value <= 0)
    {
      return null;
    }

    for (var attempt = 0; attempt < 40; attempt++)
    {
      cancellationToken.ThrowIfCancellationRequested();

      try
      {
        using var process = Process.GetProcessById(processId.Value);
        process.Refresh();
        var hwnd = process.MainWindowHandle;
        if (hwnd != IntPtr.Zero)
        {
          return $"0x{hwnd.ToInt64():X}";
        }
      }
      catch
      {
        return null;
      }

      await Task.Delay(250, cancellationToken).ConfigureAwait(false);
    }

    return null;
  }

  public async Task<string?> PlaceWindowAsync(int? processId, string? windowHandle, string? virtualDisplayId, CancellationToken cancellationToken = default)
  {
    if (!OperatingSystem.IsWindows())
    {
      return windowHandle;
    }

    if (string.IsNullOrWhiteSpace(virtualDisplayId))
    {
      return windowHandle;
    }

    var target = ParseVirtualDisplayTarget(virtualDisplayId);
    var targetMonitor = ResolveTargetMonitor(target.Slot, target.MonitorToken);
    if (targetMonitor is null)
    {
      return windowHandle;
    }

    string? latestResolvedHandle = windowHandle;

    for (var attempt = 0; attempt < 12; attempt++)
    {
      cancellationToken.ThrowIfCancellationRequested();

      var hWnd = ResolveWindowHandle(processId, windowHandle);
      if (hWnd == IntPtr.Zero)
      {
        await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        continue;
      }

      latestResolvedHandle = $"0x{hWnd.ToInt64():X}";

      _ = SetWindowPos(
        hWnd,
        IntPtr.Zero,
        targetMonitor.Value.Left,
        targetMonitor.Value.Top,
        targetMonitor.Value.Width,
        targetMonitor.Value.Height,
        SwpNoZOrder | SwpNoActivate);

      _ = SetForegroundWindow(hWnd);
      _ = ShowWindowAsync(hWnd, 9);
      await Task.Delay(250, cancellationToken).ConfigureAwait(false);
    }

    return latestResolvedHandle;
  }

  /// <summary>
  /// Monitor-assignment strategy: decodes the slot/monitor-token that
  /// <see cref="IVirtualDisplayProvider"/> encoded into the display id, then
  /// resolves the concrete monitor to target - preferring an exact monitor
  /// token match, falling back to ordinal indexing among non-primary virtual
  /// monitors by slot number.
  /// </summary>
  private static (int Slot, string? MonitorToken) ParseVirtualDisplayTarget(string virtualDisplayId)
  {
    // IDs are mttvdd-{slot}-{guid} or mttvdd-{slot}-dev-{token}-{guid}.
    var parts = virtualDisplayId.Split('-', StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length < 3)
    {
      return (1, null);
    }

    var slot = int.TryParse(parts[1], out var parsed) && parsed > 0 ? parsed : 1;
    string? monitorToken = null;

    for (var i = 2; i < parts.Length - 1; i++)
    {
      if (!parts[i].Equals("dev", StringComparison.OrdinalIgnoreCase))
      {
        continue;
      }

      monitorToken = parts[i + 1];
      break;
    }

    return (slot, monitorToken);
  }

  private static IntPtr ResolveWindowHandle(int? processId, string? windowHandle)
  {
    if (TryParseWindowHandle(windowHandle, out var parsedHandle) && parsedHandle != IntPtr.Zero)
    {
      return parsedHandle;
    }

    if (processId is > 0)
    {
      return FindWindowHandleForProcess(processId.Value);
    }

    return IntPtr.Zero;
  }

  private static IntPtr FindWindowHandleForProcess(int processId)
  {
    IntPtr result = IntPtr.Zero;

    EnumWindows((hWnd, _) =>
    {
      if (result != IntPtr.Zero)
      {
        return false;
      }

      if (!IsWindowVisible(hWnd))
      {
        return true;
      }

      if (TryGetWindowProcessId(hWnd, out var windowProcessId) && windowProcessId == processId)
      {
        result = hWnd;
        return false;
      }

      return true;
    }, IntPtr.Zero);

    return result;
  }

  private static bool TryGetWindowProcessId(IntPtr hWnd, out int processId)
  {
    processId = 0;
    return GetWindowThreadProcessId(hWnd, out processId) != 0;
  }

  private static bool TryParseWindowHandle(string? handle, out IntPtr hWnd)
  {
    hWnd = IntPtr.Zero;
    if (string.IsNullOrWhiteSpace(handle))
    {
      return false;
    }

    var trimmed = handle.Trim();
    if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
    {
      trimmed = trimmed[2..];
    }

    if (!long.TryParse(trimmed, System.Globalization.NumberStyles.HexNumber, null, out var raw))
    {
      return false;
    }

    hWnd = new IntPtr(raw);
    return true;
  }

  private static MonitorRect? ResolveTargetMonitor(int targetSlot, string? monitorToken)
  {
    var monitors = EnumerateMonitors()
      .OrderBy(m => m.Left)
      .ThenBy(m => m.Top)
      .ToArray();

    if (!string.IsNullOrWhiteSpace(monitorToken))
    {
      var tokenMatch = monitors.FirstOrDefault(m =>
        m.IsVirtual
        &&
        !string.IsNullOrWhiteSpace(m.DeviceToken)
        && m.DeviceToken.Equals(monitorToken, StringComparison.OrdinalIgnoreCase));

      if (tokenMatch != default)
      {
        return tokenMatch;
      }
    }

    var nonPrimaryVirtual = monitors.Where(m => !m.IsPrimary && m.IsVirtual).ToArray();
    if (nonPrimaryVirtual.Length > 0)
    {
      var vIndex = Math.Clamp(targetSlot - 1, 0, nonPrimaryVirtual.Length - 1);
      return nonPrimaryVirtual[vIndex];
    }

    return null;
  }

  private static List<MonitorRect> EnumerateMonitors()
  {
    var monitors = new List<MonitorRect>();

    _ = EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
    {
      var info = new MonitorInfoEx();
      info.CbSize = (uint)Marshal.SizeOf<MonitorInfoEx>();
      if (!GetMonitorInfo(monitor, ref info))
      {
        return true;
      }

      monitors.Add(new MonitorRect(
        info.RcMonitor.Left,
        info.RcMonitor.Top,
        info.RcMonitor.Right - info.RcMonitor.Left,
        info.RcMonitor.Bottom - info.RcMonitor.Top,
        (info.DwFlags & 0x1) != 0,
        info.SzDevice,
        BuildStableMonitorToken(info.SzDevice),
        IsLikelyVirtualMonitor(info.SzDevice)));

      return true;
    }, IntPtr.Zero);

    return monitors;
  }

  private static string BuildStableMonitorToken(string? deviceName)
  {
    if (string.IsNullOrWhiteSpace(deviceName))
    {
      return string.Empty;
    }

    if (TryReadDisplayDevice(deviceName, 0, out var display))
    {
      var primary = NormalizeToken(display.DeviceId);
      if (!string.IsNullOrWhiteSpace(primary))
      {
        return primary;
      }

      var secondary = NormalizeToken(display.DeviceKey);
      if (!string.IsNullOrWhiteSpace(secondary))
      {
        return secondary;
      }
    }

    for (uint monitorIndex = 0; monitorIndex < 8; monitorIndex++)
    {
      if (!TryReadDisplayDevice(deviceName, monitorIndex, out var monitor))
      {
        break;
      }

      var monitorToken = NormalizeToken(monitor.DeviceId);
      if (!string.IsNullOrWhiteSpace(monitorToken))
      {
        return monitorToken;
      }
    }

    return NormalizeToken(deviceName);
  }

  private static string NormalizeToken(string? value)
  {
    if (string.IsNullOrWhiteSpace(value))
    {
      return string.Empty;
    }

    var builder = new StringBuilder(value.Length);
    foreach (var c in value.ToUpperInvariant())
    {
      if (char.IsLetterOrDigit(c))
      {
        builder.Append(c);
      }
    }

    return builder.ToString();
  }

  private static int ParseDisplayOrdinal(string? token)
  {
    if (string.IsNullOrWhiteSpace(token))
    {
      return 0;
    }

    var digits = new string(token.Where(char.IsDigit).ToArray());
    return int.TryParse(digits, out var ordinal) ? ordinal : 0;
  }

  private static bool IsLikelyVirtualMonitor(string? deviceName)
  {
    if (string.IsNullOrWhiteSpace(deviceName))
    {
      return false;
    }

    if (TryReadDisplayDevice(deviceName, 0, out var display)
      && ContainsVirtualMarker(display.DeviceString, display.DeviceId, display.DeviceName))
    {
      return true;
    }

    for (uint monitorIndex = 0; monitorIndex < 8; monitorIndex++)
    {
      if (!TryReadDisplayDevice(deviceName, monitorIndex, out var monitor))
      {
        break;
      }

      if (ContainsVirtualMarker(monitor.DeviceString, monitor.DeviceId, monitor.DeviceName))
      {
        return true;
      }
    }

    return false;
  }

  private static bool TryReadDisplayDevice(string deviceName, uint index, out DisplayDevice device)
  {
    device = new DisplayDevice();
    device.Cb = Marshal.SizeOf<DisplayDevice>();
    return EnumDisplayDevices(deviceName, index, ref device, 0);
  }

  private static bool ContainsVirtualMarker(params string?[] values)
    => values.Any(v => !string.IsNullOrWhiteSpace(v)
      && (v.Contains("VIRTUAL", StringComparison.OrdinalIgnoreCase)
        || v.Contains("MTTVDD", StringComparison.OrdinalIgnoreCase)
        || v.Contains("MTT1337", StringComparison.OrdinalIgnoreCase)
        || v.Contains("VDD BY MTT", StringComparison.OrdinalIgnoreCase)
        || v.Contains("IDD", StringComparison.OrdinalIgnoreCase)));

  private readonly record struct MonitorRect(int Left, int Top, int Width, int Height, bool IsPrimary, string DeviceName, string DeviceToken, bool IsVirtual);

  private delegate bool MonitorEnumDelegate(IntPtr hMonitor, IntPtr hdcMonitor, IntPtr lprcMonitor, IntPtr dwData);

  [DllImport("user32.dll")]
  private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumDelegate lpfnEnum, IntPtr dwData);

  [DllImport("user32.dll", CharSet = CharSet.Unicode)]
  private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfoEx lpmi);

  [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
  private static extern bool EnumDisplayDevices(string lpDevice, uint iDevNum, ref DisplayDevice lpDisplayDevice, uint dwFlags);

  private delegate bool WindowEnumDelegate(IntPtr hWnd, IntPtr lParam);

  [DllImport("user32.dll", SetLastError = true)]
  private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

  [DllImport("user32.dll", SetLastError = true)]
  private static extern bool SetForegroundWindow(IntPtr hWnd);

  [DllImport("user32.dll", SetLastError = true)]
  private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

  [DllImport("user32.dll", SetLastError = true)]
  private static extern bool IsWindowVisible(IntPtr hWnd);

  [DllImport("user32.dll", SetLastError = true)]
  private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int processId);

  [DllImport("user32.dll", SetLastError = true)]
  private static extern bool EnumWindows(WindowEnumDelegate lpEnumFunc, IntPtr lParam);

  [StructLayout(LayoutKind.Sequential)]
  private struct Rect
  {
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
  }

  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
  private struct MonitorInfoEx
  {
    public uint CbSize;
    public Rect RcMonitor;
    public Rect RcWork;
    public uint DwFlags;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string SzDevice;
  }

  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
  private struct DisplayDevice
  {
    public int Cb;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string DeviceName;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string DeviceString;

    public uint StateFlags;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string DeviceId;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string DeviceKey;
  }
}
