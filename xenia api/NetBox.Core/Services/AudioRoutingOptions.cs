namespace NetBox.Core.Services;

public sealed class AudioRoutingOptions
{
  public bool MuteHostGamePlayback { get; set; }
  public bool RouteToVirtualSink { get; set; }
  public bool SwitchDefaultOutputToVirtualSink { get; set; } = true;
  public bool RestoreDefaultOutputOnStop { get; set; } = true;
  public bool RequireVirtualSink { get; set; }
  public string? VirtualSinkNameContains { get; set; }
  public string? CaptureInputDevice { get; set; }
  public int SessionDetectAttempts { get; set; } = 20;
  public int SessionDetectDelayMs { get; set; } = 250;
}
