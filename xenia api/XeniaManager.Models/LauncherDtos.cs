namespace XeniaManager.Models;

public sealed record LauncherStartRequest(string? ExecutablePath, string? WorkingDirectory, string? Arguments);

public sealed record LauncherStatusDto(bool IsRunning, int? ProcessId, string? ExecutablePath);
