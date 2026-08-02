namespace NetBox.Data;

public sealed class NetBoxDatabaseOptions
{
  public string DatabasePath { get; set; } = "data/netbox.db";
  public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromDays(7);
}
