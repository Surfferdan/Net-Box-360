using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetBox.Core.Abstractions;

namespace NetBox.Core.Services;

/// <summary>
/// Windows virtual display provider that integrates with an external display
/// service/CLI. If service commands are not configured, it can fall back to
/// synthetic in-memory tracking for compatibility.
/// </summary>
public sealed class WindowsVirtualDisplayProvider : IVirtualDisplayProvider
{
  private readonly ILogger<WindowsVirtualDisplayProvider> logger;
  private readonly VirtualDisplayOptions options;
  private readonly BasicVirtualDisplayProvider fallback;

  public WindowsVirtualDisplayProvider(
    ILogger<WindowsVirtualDisplayProvider> logger,
    IOptions<VirtualDisplayOptions> options,
    BasicVirtualDisplayProvider fallback)
  {
    this.logger = logger;
    this.options = options.Value;
    this.fallback = fallback;
  }

  public async Task<string?> ProvisionDisplayAsync(string sessionId, string gameTitle, CancellationToken cancellationToken = default)
  {
    if (!options.Enabled)
    {
      logger.LogInformation("[VirtualDisplay] Virtual display integration disabled by config.");
      return null;
    }

    if (string.IsNullOrWhiteSpace(sessionId))
    {
      logger.LogWarning("[VirtualDisplay] Provision requested with empty session id.");
      return null;
    }

    if (TryGetServiceCommand(options.ProvisionCommand, options.ProvisionArguments, sessionId, gameTitle, null, out var command, out var args))
    {
      var result = await ExecuteAsync(command!, args!, cancellationToken).ConfigureAwait(false);
      if (result.Success)
      {
        var displayId = TryParseDisplayId(result.StdOut);
        if (!string.IsNullOrWhiteSpace(displayId))
        {
          logger.LogInformation("[VirtualDisplay] Service provisioned displayId={DisplayId} for sessionId={SessionId}.", displayId, sessionId);
          return displayId;
        }

        logger.LogWarning("[VirtualDisplay] Provision command succeeded without a display id in stdout. stdout={StdOut}", result.StdOut);
      }
      else
      {
        logger.LogWarning("[VirtualDisplay] Provision command failed. command={Command}, exitCode={ExitCode}, stderr={StdErr}", command, result.ExitCode, result.StdErr);
      }
    }

    if (options.RequireService)
    {
      logger.LogWarning("[VirtualDisplay] Service is required and no provisioned display was available.");
      return null;
    }

    if (!options.UseSyntheticFallback)
    {
      return null;
    }

    logger.LogWarning("[VirtualDisplay] Falling back to synthetic display provisioning.");
    return await fallback.ProvisionDisplayAsync(sessionId, gameTitle, cancellationToken).ConfigureAwait(false);
  }

  public async Task ReleaseDisplayAsync(string sessionId, string? virtualDisplayId, CancellationToken cancellationToken = default)
  {
    if (string.IsNullOrWhiteSpace(virtualDisplayId))
    {
      return;
    }

    var releasedByService = false;
    if (TryGetServiceCommand(options.ReleaseCommand, options.ReleaseArguments, sessionId, null, virtualDisplayId, out var command, out var args))
    {
      var result = await ExecuteAsync(command!, args!, cancellationToken).ConfigureAwait(false);
      if (result.Success)
      {
        releasedByService = true;
        logger.LogInformation("[VirtualDisplay] Service released displayId={DisplayId} for sessionId={SessionId}.", virtualDisplayId, sessionId);
      }
      else
      {
        logger.LogWarning("[VirtualDisplay] Release command failed. command={Command}, exitCode={ExitCode}, stderr={StdErr}", command, result.ExitCode, result.StdErr);
      }
    }

    if (!releasedByService && options.UseSyntheticFallback)
    {
      await fallback.ReleaseDisplayAsync(sessionId, virtualDisplayId, cancellationToken).ConfigureAwait(false);
    }
  }

  public async Task<string> GetDisplayStatusAsync(string? virtualDisplayId, CancellationToken cancellationToken = default)
  {
    if (string.IsNullOrWhiteSpace(virtualDisplayId))
    {
      return "unknown";
    }

    if (TryGetServiceCommand(options.StatusCommand, options.StatusArguments, null, null, virtualDisplayId, out var command, out var args))
    {
      var result = await ExecuteAsync(command!, args!, cancellationToken).ConfigureAwait(false);
      if (result.Success)
      {
        var status = TryParseStatus(result.StdOut);
        if (!string.IsNullOrWhiteSpace(status))
        {
          return status;
        }
      }
      else
      {
        logger.LogDebug("[VirtualDisplay] Status command failed. command={Command}, exitCode={ExitCode}", command, result.ExitCode);
      }
    }

    if (options.UseSyntheticFallback)
    {
      return await fallback.GetDisplayStatusAsync(virtualDisplayId, cancellationToken).ConfigureAwait(false);
    }

    return "unknown";
  }

  public async Task CleanupOrphanedDisplaysAsync(CancellationToken cancellationToken = default)
  {
    if (TryGetServiceCommand(options.CleanupCommand, options.CleanupArguments, null, null, null, out var command, out var args))
    {
      var result = await ExecuteAsync(command!, args!, cancellationToken).ConfigureAwait(false);
      if (!result.Success)
      {
        logger.LogWarning("[VirtualDisplay] Cleanup command failed. command={Command}, exitCode={ExitCode}, stderr={StdErr}", command, result.ExitCode, result.StdErr);
      }
    }

    if (options.UseSyntheticFallback)
    {
      await fallback.CleanupOrphanedDisplaysAsync(cancellationToken).ConfigureAwait(false);
    }
  }

  private bool TryGetServiceCommand(
    string? configuredCommand,
    string? configuredArguments,
    string? sessionId,
    string? gameTitle,
    string? displayId,
    out string? command,
    out string? args)
  {
    command = configuredCommand?.Trim();
    args = null;

    if (string.IsNullOrWhiteSpace(command))
    {
      return false;
    }

    args = (configuredArguments ?? string.Empty)
      .Replace("{sessionId}", EscapeArg(sessionId), StringComparison.Ordinal)
      .Replace("{gameTitle}", EscapeArg(gameTitle), StringComparison.Ordinal)
      .Replace("{displayId}", EscapeArg(displayId), StringComparison.Ordinal);

    return true;
  }

  private static string EscapeArg(string? value)
  {
    if (string.IsNullOrWhiteSpace(value))
    {
      return string.Empty;
    }

    return value.Replace("\"", "\\\"", StringComparison.Ordinal);
  }

  private async Task<CommandResult> ExecuteAsync(string command, string arguments, CancellationToken cancellationToken)
  {
    using var process = new Process
    {
      StartInfo = new ProcessStartInfo
      {
        FileName = command,
        Arguments = arguments,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
        WorkingDirectory = string.IsNullOrWhiteSpace(options.CommandWorkingDirectory)
          ? Environment.CurrentDirectory
          : options.CommandWorkingDirectory
      }
    };

    try
    {
      if (!process.Start())
      {
        return CommandResult.Failed("Process did not start.", -1);
      }
    }
    catch (Exception ex)
    {
      return CommandResult.Failed(ex.Message, -1);
    }

    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, options.CommandTimeoutSeconds)));

    try
    {
      var waitTask = process.WaitForExitAsync(timeoutCts.Token);
      var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
      var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
      await Task.WhenAll(waitTask, stdoutTask, stderrTask).ConfigureAwait(false);

      return new CommandResult(process.ExitCode == 0, stdoutTask.Result.Trim(), stderrTask.Result.Trim(), process.ExitCode);
    }
    catch (OperationCanceledException)
    {
      try
      {
        if (!process.HasExited)
        {
          process.Kill(true);
        }
      }
      catch
      {
        // no-op
      }

      return CommandResult.Failed("Timed out waiting for command to complete.", -1);
    }
  }

  private static string? TryParseDisplayId(string stdOut)
  {
    if (string.IsNullOrWhiteSpace(stdOut))
    {
      return null;
    }

    try
    {
      using var doc = JsonDocument.Parse(stdOut);
      if (doc.RootElement.ValueKind == JsonValueKind.Object
        && doc.RootElement.TryGetProperty("displayId", out var displayIdElement)
        && displayIdElement.ValueKind == JsonValueKind.String)
      {
        return displayIdElement.GetString();
      }
    }
    catch
    {
      // fallback below
    }

    return stdOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
  }

  private static string? TryParseStatus(string stdOut)
  {
    if (string.IsNullOrWhiteSpace(stdOut))
    {
      return null;
    }

    try
    {
      using var doc = JsonDocument.Parse(stdOut);
      if (doc.RootElement.ValueKind == JsonValueKind.Object
        && doc.RootElement.TryGetProperty("status", out var statusElement)
        && statusElement.ValueKind == JsonValueKind.String)
      {
        return statusElement.GetString();
      }
    }
    catch
    {
      // fallback below
    }

    return stdOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
  }

  private sealed record CommandResult(bool Success, string StdOut, string StdErr, int ExitCode)
  {
    public static CommandResult Failed(string error, int exitCode)
      => new(false, string.Empty, error, exitCode);
  }
}
