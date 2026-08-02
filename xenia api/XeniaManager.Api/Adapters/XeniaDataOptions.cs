namespace XeniaManager.Api.Adapters;

public sealed class XeniaDataOptions
{
  public string ProfilesFilePath { get; set; } = "data/profiles.json";
  public string AchievementsDirectory { get; set; } = "data/achievements";
  public string SavesDirectory { get; set; } = "data/saves";
  public string ConfigFilePath { get; set; } = "data/config.json";
  public string GamesDirectory { get; set; } = "data/games";
  public string XeniaExecutablePath { get; set; } = "xenia_canary.exe";
}
