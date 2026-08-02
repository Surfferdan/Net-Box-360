using Microsoft.Extensions.DependencyInjection;
using XeniaManager.Adapters.Legacy;
using XeniaManager.Core.Abstractions.Adapters;

namespace XeniaManager.Adapters;

public static class DependencyInjection
{
  public static IServiceCollection AddXeniaManagerLegacyAdapters<TFacade>(this IServiceCollection services)
    where TFacade : class, IXeniaManagerLegacyFacade
  {
    services.AddScoped<IXeniaManagerLegacyFacade, TFacade>();
    services.AddScoped<LegacyAdapterBridge>();

    services.AddScoped<IXeniaProfileAdapter>(sp => sp.GetRequiredService<LegacyAdapterBridge>());
    services.AddScoped<IXeniaAchievementAdapter>(sp => sp.GetRequiredService<LegacyAdapterBridge>());
    services.AddScoped<IXeniaSaveAdapter>(sp => sp.GetRequiredService<LegacyAdapterBridge>());
    services.AddScoped<IXeniaConfigAdapter>(sp => sp.GetRequiredService<LegacyAdapterBridge>());
    services.AddScoped<IXeniaLauncherAdapter>(sp => sp.GetRequiredService<LegacyAdapterBridge>());

    return services;
  }
}
