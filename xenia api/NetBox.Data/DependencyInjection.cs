using Microsoft.Extensions.DependencyInjection;
using NetBox.Data.Repositories;

namespace NetBox.Data;

public static class DependencyInjection
{
  public static IServiceCollection AddNetBoxData(this IServiceCollection services)
  {
    services.AddScoped<INetBoxRepository, SqliteNetBoxRepository>();
    return services;
  }
}
