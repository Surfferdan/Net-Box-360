using Microsoft.Extensions.DependencyInjection;
using XeniaManager.Core.Services;

namespace XeniaManager.Core;

public static class DependencyInjection
{
  public static IServiceCollection AddXeniaManagerCore(this IServiceCollection services)
  {
    services.AddScoped<IProfileService, ProfileService>();
    services.AddScoped<IAchievementService, AchievementService>();
    services.AddScoped<ISaveService, SaveService>();
    services.AddScoped<IConfigService, ConfigService>();
    services.AddScoped<ILauncherService, LauncherService>();
    return services;
  }
}
