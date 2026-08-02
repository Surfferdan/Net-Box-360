namespace NetBox.Core.Services;

/// <summary>
/// Where to send NetBox input UDP packets - must match the
/// --netbox_input_port the running xenia_netbox.exe was launched with (see
/// appsettings.json "NetBoxInput" section and the Xenia cvar default of
/// 47600 in src/xenia/netbox/netbox_config.cc).
/// </summary>
public sealed class NetBoxInputBridgeOptions
{
  public string Host { get; set; } = "127.0.0.1";
  public int Port { get; set; } = 47600;
}
