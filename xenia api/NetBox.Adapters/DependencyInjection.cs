using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NetBox.Adapters.Xenia;

namespace NetBox.Adapters;

public static class DependencyInjection
{
  public static IServiceCollection AddXeniaProfileGateway(this IServiceCollection services)
  {
    services.AddHttpClient<IXeniaProfileGateway, HttpXeniaProfileGateway>((sp, client) =>
    {
      var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<XeniaApiOptions>>().Value;
      client.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
    });

    return services;
  }

  public static IServiceCollection AddXeniaGameCatalogGateway(this IServiceCollection services)
  {
    services.AddHttpClient<IXeniaGameCatalogGateway, HttpXeniaGameCatalogGateway>((sp, client) =>
    {
      var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<XeniaApiOptions>>().Value;
      client.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
    });

    return services;
  }

  public static IServiceCollection AddCloudMorphAdapter(this IServiceCollection services, IConfiguration configuration)
  {
    services.Configure<CloudMorphOptions>(configuration.GetSection("CloudMorph"));
    services.AddSingleton<ICloudMorphWorkerRouter, CloudMorphWorkerRouter>();
    services.AddSingleton<ICloudMorphCircuitBreaker, CloudMorphCircuitBreaker>();
    services.AddHttpClient<ICloudMorphAdapter, CloudMorphAdapter>((sp, client) =>
    {
      var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CloudMorphOptions>>().Value;
      if (Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri))
      {
        client.BaseAddress = baseUri;
      }
      else
      {
        // Guard against malformed local config/env overrides so stream calls
        // degrade to fallback instead of failing API endpoints with 500.
        client.BaseAddress = new Uri("http://127.0.0.1:8080", UriKind.Absolute);
      }
      // Control-plane calls must fail fast so an unreachable streamer never stalls
      // session start/stop; the circuit breaker takes over after repeated failures.
      client.Timeout = TimeSpan.FromSeconds(Math.Max(1, options.RequestTimeoutSeconds));
    });
    return services;
  }
}
