namespace NetBox.Core.Services;

public sealed class VirtualDisplayOptions
{
  public bool Enabled { get; set; } = true;
  public bool RequireService { get; set; }
  public bool UseSyntheticFallback { get; set; } = true;
  public int CommandTimeoutSeconds { get; set; } = 10;
  public string? CommandWorkingDirectory { get; set; }

  public string? ProvisionCommand { get; set; }
  public string ProvisionArguments { get; set; } = "provision --session \"{sessionId}\" --title \"{gameTitle}\"";

  public string? ReleaseCommand { get; set; }
  public string ReleaseArguments { get; set; } = "release --session \"{sessionId}\" --display \"{displayId}\"";

  public string? StatusCommand { get; set; }
  public string StatusArguments { get; set; } = "status --display \"{displayId}\"";

  public string? CleanupCommand { get; set; }
  public string CleanupArguments { get; set; } = "cleanup";
}
