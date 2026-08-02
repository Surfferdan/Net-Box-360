using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

const string vddReleaseTag = "25.7.23";
const string vddDriverZipUrl = "https://github.com/VirtualDrivers/Virtual-Display-Driver/releases/download/25.7.23/VirtualDisplayDriver-x86.Driver.Only.zip";
const string vddControlZipUrl = "https://github.com/VirtualDrivers/Virtual-Display-Driver/releases/download/25.7.23/VDD.Control.25.7.23.zip";

var appRoot = ResolveAppRoot();
var stateDir = Path.Combine(appRoot, ".netbox");
var statePath = Path.Combine(stateDir, "virtual-displays.json");
var toolsRoot = Path.Combine(appRoot, ".tools", "vdd");
var downloadsRoot = Path.Combine(toolsRoot, "downloads");
var driverExtractRoot = Path.Combine(toolsRoot, "driver-x86");
var driverFolder = Path.Combine(driverExtractRoot, "VirtualDisplayDriver");
var infPath = Path.Combine(driverFolder, "MttVDD.inf");
var bundledSettingsPath = Path.Combine(driverFolder, "vdd_settings.xml");
var vddRuntimeDir = Path.Combine(Environment.GetEnvironmentVariable("SystemDrive") ?? "C:", "VirtualDisplayDriver");
var runtimeSettingsPath = Path.Combine(vddRuntimeDir, "vdd_settings.xml");
var controlExtractRoot = Path.Combine(toolsRoot, "control");
var devconPath = Path.Combine(controlExtractRoot, "Dependencies", "devcon.exe");

Directory.CreateDirectory(stateDir);
Directory.CreateDirectory(downloadsRoot);

var command = args.Length > 0 ? args[0].Trim().ToLowerInvariant() : string.Empty;
var options = ParseArgs(args.Skip(1).ToArray());
var state = await LoadStateAsync(statePath).ConfigureAwait(false);

try
{
  switch (command)
  {
    case "provision":
    {
      EnsureElevated();
      await EnsureVddReadyAsync().ConfigureAwait(false);

      var sessionId = GetRequired(options, "session");
      var gameTitle = options.GetValueOrDefault("title") ?? string.Empty;

      if (state.Displays.Values.Any(x => x.SessionId.Equals(sessionId, StringComparison.OrdinalIgnoreCase)))
      {
        var existing = state.Displays.Values.First(x => x.SessionId.Equals(sessionId, StringComparison.OrdinalIgnoreCase));
        WriteJson(new { displayId = existing.DisplayId, status = "active" });
        return 0;
      }

      var reusableMonitor = TrySelectReusableVirtualMonitor(state);
      if (reusableMonitor is not null)
      {
        var reusedDisplayId = BuildDisplayId(reusableMonitor.Value.Slot, reusableMonitor.Value.Monitor.DeviceToken, reuseExisting: true);
        state.Displays[reusedDisplayId] = new DisplayRecord(reusedDisplayId, sessionId, gameTitle, DateTimeOffset.UtcNow, "active", reusableMonitor.Value.Slot);
        await SaveStateAsync(statePath, state).ConfigureAwait(false);

        WriteJson(new
        {
          displayId = reusedDisplayId,
          status = "active",
          slot = reusableMonitor.Value.Slot,
          reusedExisting = true,
          monitorToken = reusableMonitor.Value.Monitor.DeviceToken,
          monitorDevice = reusableMonitor.Value.Monitor.DeviceName,
          monitorLeft = reusableMonitor.Value.Monitor.Left,
          monitorTop = reusableMonitor.Value.Monitor.Top,
          monitorWidth = reusableMonitor.Value.Monitor.Width,
          monitorHeight = reusableMonitor.Value.Monitor.Height
        });
        return 0;
      }

      var actualVirtualCount = EnumerateMonitors().Count(x => x.IsVirtual);
      var currentCount = actualVirtualCount;
      if (actualVirtualCount == 0)
      {
        var staleDisplayIds = state.Displays.Values
          .Where(x => x.DisplayId.StartsWith("mttvdd-", StringComparison.OrdinalIgnoreCase))
          .Select(x => x.DisplayId)
          .ToArray();

        foreach (var staleDisplayId in staleDisplayIds)
        {
          _ = state.Displays.Remove(staleDisplayId);
        }
      }

      var monitorsBefore = EnumerateMonitors()
        .Select(x => x.DeviceToken)
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

      var nextCount = currentCount + 1;
      WriteMonitorCount(runtimeSettingsPath, nextCount);
      await ReloadDriverAsync().ConfigureAwait(false);

      var targetMonitor = ResolveProvisionedMonitor(monitorsBefore);
      if (targetMonitor is null || !targetMonitor.Value.IsVirtual)
      {
        WriteMonitorCount(runtimeSettingsPath, currentCount);
        await ReloadDriverAsync().ConfigureAwait(false);
        throw new InvalidOperationException("Virtual display driver did not expose a new virtual monitor after reload.");
      }

      var monitorToken = string.IsNullOrWhiteSpace(targetMonitor?.DeviceToken)
        ? string.Empty
        : targetMonitor.Value.DeviceToken;

      var displayId = BuildDisplayId(nextCount, monitorToken, reuseExisting: false);
      state.Displays[displayId] = new DisplayRecord(displayId, sessionId, gameTitle, DateTimeOffset.UtcNow, "active", nextCount);
      await SaveStateAsync(statePath, state).ConfigureAwait(false);

      WriteJson(new
      {
        displayId,
        status = "active",
        slot = nextCount,
        monitorToken,
        monitorDevice = targetMonitor?.DeviceName,
        monitorLeft = targetMonitor?.Left,
        monitorTop = targetMonitor?.Top,
        monitorWidth = targetMonitor?.Width,
        monitorHeight = targetMonitor?.Height
      });
      return 0;
    }
    case "release":
    {
      EnsureElevated();
      await EnsureVddReadyAsync().ConfigureAwait(false);

      var displayId = GetRequired(options, "display");
      _ = state.Displays.Remove(displayId);

      if (displayId.Contains("-reuse-dev-", StringComparison.OrdinalIgnoreCase))
      {
        await SaveStateAsync(statePath, state).ConfigureAwait(false);
        WriteJson(new { displayId, status = "released", reusedExisting = true });
        return 0;
      }

      var desiredCount = Math.Max(0, state.Displays.Count);
      WriteMonitorCount(runtimeSettingsPath, desiredCount);
      await ReloadDriverAsync().ConfigureAwait(false);
      await SaveStateAsync(statePath, state).ConfigureAwait(false);

      WriteJson(new { displayId, status = desiredCount > 0 ? "active" : "released" });
      return 0;
    }
    case "status":
    {
      var displayId = GetRequired(options, "display");
      if (state.Displays.TryGetValue(displayId, out var record))
      {
        var encodedToken = ParseDisplayIdMonitorToken(displayId);
        var monitor = ResolveTargetMonitor(encodedToken, record.Slot);
        WriteJson(new
        {
          displayId,
          status = record.Status,
          slot = record.Slot,
          monitorToken = string.IsNullOrWhiteSpace(monitor?.DeviceToken) ? null : monitor.Value.DeviceToken,
          monitorDevice = monitor?.DeviceName,
          monitorLeft = monitor?.Left,
          monitorTop = monitor?.Top,
          monitorWidth = monitor?.Width,
          monitorHeight = monitor?.Height
        });
        return 0;
      }

      WriteJson(new { displayId, status = "inactive" });
      return 0;
    }
    case "cleanup":
    {
      EnsureElevated();
      await EnsureVddReadyAsync().ConfigureAwait(false);

      var cutoff = DateTimeOffset.UtcNow.AddHours(-24);
      var stale = state.Displays.Values
        .Where(x => x.CreatedAt <= cutoff)
        .Select(x => x.DisplayId)
        .ToArray();

      foreach (var displayId in stale)
      {
        _ = state.Displays.Remove(displayId);
      }

      var desiredCount = Math.Max(0, state.Displays.Count);
      WriteMonitorCount(runtimeSettingsPath, desiredCount);
      await ReloadDriverAsync().ConfigureAwait(false);
      await SaveStateAsync(statePath, state).ConfigureAwait(false);

      WriteJson(new { status = "ok", removed = stale.Length });
      return 0;
    }
    default:
      Console.Error.WriteLine("Usage: provision|release|status|cleanup [--session <id>] [--title <title>] [--display <id>]");
      return 2;
  }
}
catch (Exception ex)
{
  Console.Error.WriteLine(ex.Message);
  return 1;
}

void EnsureElevated()
{
  if (!OperatingSystem.IsWindows())
  {
    throw new InvalidOperationException("Virtual display operations are only supported on Windows.");
  }

  var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
  var principal = new System.Security.Principal.WindowsPrincipal(identity);
  if (!principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
  {
    throw new InvalidOperationException("Virtual display operations require an elevated administrator process.");
  }
}

async Task EnsureVddReadyAsync()
{
  if (!File.Exists(infPath) || !File.Exists(bundledSettingsPath))
  {
    await DownloadAndExtractDriverAsync().ConfigureAwait(false);
  }

  if (!File.Exists(devconPath))
  {
    await DownloadAndExtractControlAsync().ConfigureAwait(false);
  }

  Directory.CreateDirectory(vddRuntimeDir);
  if (!File.Exists(runtimeSettingsPath))
  {
    File.Copy(bundledSettingsPath, runtimeSettingsPath, overwrite: true);
  }

  await InstallDriverAsync().ConfigureAwait(false);
}

async Task DownloadAndExtractDriverAsync()
{
  var zipPath = Path.Combine(downloadsRoot, $"VirtualDisplayDriver-x86.Driver.Only-{vddReleaseTag}.zip");
  if (!File.Exists(zipPath))
  {
    using var http = new HttpClient();
    await using var input = await http.GetStreamAsync(vddDriverZipUrl).ConfigureAwait(false);
    await using var output = File.Create(zipPath);
    await input.CopyToAsync(output).ConfigureAwait(false);
  }

  Directory.CreateDirectory(driverExtractRoot);
  ZipFile.ExtractToDirectory(zipPath, driverExtractRoot, overwriteFiles: true);
}

async Task DownloadAndExtractControlAsync()
{
  var zipPath = Path.Combine(downloadsRoot, $"VDD.Control.{vddReleaseTag}.zip");
  if (!File.Exists(zipPath))
  {
    using var http = new HttpClient();
    await using var input = await http.GetStreamAsync(vddControlZipUrl).ConfigureAwait(false);
    await using var output = File.Create(zipPath);
    await input.CopyToAsync(output).ConfigureAwait(false);
  }

  Directory.CreateDirectory(controlExtractRoot);
  ZipFile.ExtractToDirectory(zipPath, controlExtractRoot, overwriteFiles: true);
}

async Task InstallDriverAsync()
{
  var install = await RunProcessAsync("pnputil", $"/add-driver \"{infPath}\" /install").ConfigureAwait(false);
  var alreadyInstalled = install.StdOut.Contains("Already exists in the system", StringComparison.OrdinalIgnoreCase)
    || install.StdOut.Contains("Driver package added successfully. (Already exists in the system)", StringComparison.OrdinalIgnoreCase);

  if (install.ExitCode != 0 && !alreadyInstalled)
  {
    throw new InvalidOperationException($"Failed to install VDD driver via pnputil. {install.StdErr} {install.StdOut}".Trim());
  }

  if ((await GetVddDisplayDeviceInstanceIdsAsync().ConfigureAwait(false)).Length > 0)
  {
    return;
  }

  if (!File.Exists(devconPath))
  {
    throw new InvalidOperationException("VDD driver files are staged, but no Root\\MttVDD device exists and devcon.exe is unavailable to create it.");
  }

  var createDevice = await RunProcessAsync(devconPath, $"install \"{infPath}\" Root\\MttVDD").ConfigureAwait(false);
  var deviceCreated = createDevice.StdOut.Contains("Device node created", StringComparison.OrdinalIgnoreCase)
    || createDevice.StdOut.Contains("Drivers installed successfully", StringComparison.OrdinalIgnoreCase);

  if (createDevice.ExitCode != 0 && !deviceCreated)
  {
    throw new InvalidOperationException($"Failed to create Root\\MttVDD device via devcon. {createDevice.StdErr} {createDevice.StdOut}".Trim());
  }

  if ((await GetVddDisplayDeviceInstanceIdsAsync().ConfigureAwait(false)).Length == 0)
  {
    throw new InvalidOperationException($"devcon reported VDD device creation, but no active Virtual Display Driver device instance was found afterward. {createDevice.StdErr} {createDevice.StdOut}".Trim());
  }
}

async Task<string[]> GetVddDisplayDeviceInstanceIdsAsync()
{
  var result = await RunProcessAsync("pnputil", "/enum-devices /class Display").ConfigureAwait(false);
  if (result.ExitCode != 0)
  {
    return Array.Empty<string>();
  }

  var blocks = result.StdOut
    .Split(Environment.NewLine + Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

  return blocks
    .Where(block => block.Contains("Device Description:         Virtual Display Driver", StringComparison.OrdinalIgnoreCase)
      && block.Contains("Manufacturer Name:          MikeTheTech", StringComparison.OrdinalIgnoreCase))
    .Select(block => block.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
      .FirstOrDefault(line => line.StartsWith("Instance ID:", StringComparison.OrdinalIgnoreCase)))
    .Where(line => !string.IsNullOrWhiteSpace(line))
    .Select(line => line!["Instance ID:".Length..].Trim())
    .ToArray();
}

async Task ReloadDriverAsync()
{
  var instanceIds = await GetVddDisplayDeviceInstanceIdsAsync().ConfigureAwait(false);
  if (instanceIds.Length == 0)
  {
    throw new InvalidOperationException("No installed Virtual Display Driver device instances were found to reload.");
  }

  var failures = new List<string>();
  foreach (var instanceId in instanceIds)
  {
    var restart = await RunProcessAsync("pnputil", $"/restart-device \"{instanceId}\"").ConfigureAwait(false);
    if (restart.ExitCode == 0)
    {
      continue;
    }

    if (File.Exists(devconPath))
    {
      var devcon = await RunProcessAsync(devconPath, $"restart \"@{instanceId}\"").ConfigureAwait(false);
      if (devcon.ExitCode == 0)
      {
        continue;
      }

      failures.Add($"{instanceId}: {devcon.StdErr} {devcon.StdOut}".Trim());
      continue;
    }

    failures.Add($"{instanceId}: {restart.StdErr} {restart.StdOut}".Trim());
  }

  if (failures.Count > 0)
  {
    throw new InvalidOperationException($"Failed to reload one or more VDD display devices after updating monitor count. {string.Join(" | ", failures)}".Trim());
  }
}

static void WriteMonitorCount(string settingsPath, int count)
{
  var doc = XDocument.Load(settingsPath);
  var countElement = doc.Root?.Element("monitors")?.Element("count");
  if (countElement is null)
  {
    throw new InvalidOperationException("Invalid vdd_settings.xml: missing monitors/count node.");
  }

  countElement.Value = Math.Max(0, count).ToString();
  doc.Save(settingsPath);
}

static async Task<ProcessResult> RunProcessAsync(string fileName, string arguments)
{
  using var process = new Process
  {
    StartInfo = new ProcessStartInfo
    {
      FileName = fileName,
      Arguments = arguments,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true
    }
  };

  if (!process.Start())
  {
    return new ProcessResult(-1, string.Empty, "Process failed to start.");
  }

  var stdoutTask = process.StandardOutput.ReadToEndAsync();
  var stderrTask = process.StandardError.ReadToEndAsync();
  await process.WaitForExitAsync().ConfigureAwait(false);

  return new ProcessResult(process.ExitCode, await stdoutTask.ConfigureAwait(false), await stderrTask.ConfigureAwait(false));
}

static string ResolveAppRoot()
{
  var current = new DirectoryInfo(AppContext.BaseDirectory);
  while (current is not null)
  {
    var probe = Path.Combine(current.FullName, "XeniaManager.Api");
    if (Directory.Exists(probe))
    {
      return current.FullName;
    }

    current = current.Parent;
  }

  return Directory.GetCurrentDirectory();
}

static Dictionary<string, string> ParseArgs(string[] args)
{
  var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
  for (var i = 0; i < args.Length; i++)
  {
    var arg = args[i];
    if (!arg.StartsWith("--", StringComparison.Ordinal))
    {
      continue;
    }

    var key = arg[2..];
    var value = i + 1 < args.Length ? args[i + 1] : string.Empty;
    map[key] = value;
    i++;
  }

  return map;
}

static string GetRequired(Dictionary<string, string> options, string key)
{
  if (!options.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
  {
    throw new InvalidOperationException($"Missing required argument --{key}");
  }

  return value.Trim();
}

static void WriteJson(object payload)
{
  Console.WriteLine(JsonSerializer.Serialize(payload));
}

static async Task<VirtualDisplayState> LoadStateAsync(string statePath)
{
  if (!File.Exists(statePath))
  {
    return new VirtualDisplayState();
  }

  await using var stream = File.OpenRead(statePath);
  var state = await JsonSerializer.DeserializeAsync<VirtualDisplayState>(stream).ConfigureAwait(false);
  return state ?? new VirtualDisplayState();
}

static async Task SaveStateAsync(string statePath, VirtualDisplayState state)
{
  await using var stream = File.Create(statePath);
  await JsonSerializer.SerializeAsync(stream, state, new JsonSerializerOptions { WriteIndented = true }).ConfigureAwait(false);
}

static string BuildStableMonitorToken(string deviceName)
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

static string NormalizeToken(string? value)
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

static MonitorRect? ResolveTargetMonitorForSlot(int slot)
{
  var monitors = EnumerateMonitors()
    .Where(x => !x.IsPrimary && x.IsVirtual)
    .OrderBy(x => x.Left)
    .ThenBy(x => x.Top)
    .ToArray();

  if (monitors.Length == 0)
  {
    return null;
  }

  if (monitors.Length >= slot)
  {
    return monitors[slot - 1];
  }

  var index = Math.Clamp(slot - 1, 0, monitors.Length - 1);
  return monitors[index];
}

static MonitorRect? ResolveTargetMonitor(string? monitorToken, int fallbackSlot)
{
  if (!string.IsNullOrWhiteSpace(monitorToken))
  {
    var match = EnumerateMonitors()
      .FirstOrDefault(x => x.IsVirtual && x.DeviceToken.Equals(monitorToken, StringComparison.OrdinalIgnoreCase));
    if (!string.IsNullOrWhiteSpace(match.DeviceName))
    {
      return match;
    }
  }

  return ResolveTargetMonitorForSlot(fallbackSlot);
}

static MonitorRect? ResolveProvisionedMonitor(HashSet<string> monitorsBefore)
{
  var current = EnumerateMonitors()
    .Where(x => x.IsVirtual)
    .ToArray();

  foreach (var monitor in current)
  {
    var token = monitor.DeviceToken;
    if (string.IsNullOrWhiteSpace(token))
    {
      continue;
    }

    if (!monitorsBefore.Contains(token))
    {
      return monitor;
    }
  }

  return null;
}

static string? ParseDisplayIdMonitorToken(string displayId)
{
  var parts = displayId.Split('-', StringSplitOptions.RemoveEmptyEntries);
  for (var i = 2; i < parts.Length - 1; i++)
  {
    if (parts[i].Equals("dev", StringComparison.OrdinalIgnoreCase))
    {
      return parts[i + 1];
    }
  }

  return null;
}

static string BuildDisplayId(int slot, string? monitorToken, bool reuseExisting)
{
  if (string.IsNullOrWhiteSpace(monitorToken))
  {
    return $"mttvdd-{slot}-{Guid.NewGuid():N}";
  }

  return reuseExisting
    ? $"mttvdd-{slot}-reuse-dev-{monitorToken}-{Guid.NewGuid():N}"
    : $"mttvdd-{slot}-dev-{monitorToken}-{Guid.NewGuid():N}";
}

static (int Slot, MonitorRect Monitor)? TrySelectReusableVirtualMonitor(VirtualDisplayState state)
{
  var claimedTokens = state.Displays.Keys
    .Select(ParseDisplayIdMonitorToken)
    .Where(x => !string.IsNullOrWhiteSpace(x))
    .ToHashSet(StringComparer.OrdinalIgnoreCase);

  var activeVirtualMonitors = EnumerateMonitors()
    .Where(x => x.IsVirtual && !x.IsPrimary)
    .OrderBy(x => x.Left)
    .ThenBy(x => x.Top)
    .ToArray();

  if (activeVirtualMonitors.Length == 0)
  {
    return null;
  }

  for (var i = 0; i < activeVirtualMonitors.Length; i++)
  {
    var monitor = activeVirtualMonitors[i];
    if (!string.IsNullOrWhiteSpace(monitor.DeviceToken) && !claimedTokens.Contains(monitor.DeviceToken))
    {
      return (i + 1, monitor);
    }
  }

  return (1, activeVirtualMonitors[0]);
}

static MonitorRect[] EnumerateMonitors()
{
  var monitors = new List<MonitorRect>();

  _ = Win32.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
  {
    var info = new MonitorInfoEx();
    info.CbSize = (uint)Marshal.SizeOf<MonitorInfoEx>();
    if (!Win32.GetMonitorInfo(monitor, ref info))
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

  return monitors.ToArray();
}

static bool IsLikelyVirtualMonitor(string? deviceName)
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

static bool TryReadDisplayDevice(string deviceName, uint index, out DisplayDevice device)
{
  device = new DisplayDevice();
  device.Cb = Marshal.SizeOf<DisplayDevice>();
  return Win32.EnumDisplayDevices(deviceName, index, ref device, 0);
}

static bool ContainsVirtualMarker(params string?[] values)
  => values.Any(v => !string.IsNullOrWhiteSpace(v)
    && (v.Contains("VIRTUAL", StringComparison.OrdinalIgnoreCase)
      || v.Contains("MTTVDD", StringComparison.OrdinalIgnoreCase)
      || v.Contains("MTT1337", StringComparison.OrdinalIgnoreCase)
      || v.Contains("VDD BY MTT", StringComparison.OrdinalIgnoreCase)
      || v.Contains("IDD", StringComparison.OrdinalIgnoreCase)));

readonly record struct MonitorRect(int Left, int Top, int Width, int Height, bool IsPrimary, string DeviceName, string DeviceToken, bool IsVirtual);

delegate bool MonitorEnumDelegate(IntPtr hMonitor, IntPtr hdcMonitor, IntPtr lprcMonitor, IntPtr dwData);

[StructLayout(LayoutKind.Sequential)]
struct Rect
{
  public int Left;
  public int Top;
  public int Right;
  public int Bottom;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
struct MonitorInfoEx
{
  public uint CbSize;
  public Rect RcMonitor;
  public Rect RcWork;
  public uint DwFlags;

  [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
  public string SzDevice;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
struct DisplayDevice
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

static class Win32
{
  [DllImport("user32.dll")]
  public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumDelegate lpfnEnum, IntPtr dwData);

  [DllImport("user32.dll", CharSet = CharSet.Unicode)]
  public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfoEx lpmi);

  [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
  public static extern bool EnumDisplayDevices(string lpDevice, uint iDevNum, ref DisplayDevice lpDisplayDevice, uint dwFlags);
}

sealed class VirtualDisplayState
{
  public Dictionary<string, DisplayRecord> Displays { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

sealed record DisplayRecord(
  string DisplayId,
  string SessionId,
  string GameTitle,
  DateTimeOffset CreatedAt,
  string Status,
  int Slot);

sealed record ProcessResult(int ExitCode, string StdOut, string StdErr);
