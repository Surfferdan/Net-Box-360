using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using NetBox.Core.Abstractions;
using NetBox.Core.Security;
using NetBox.Core.Services;

namespace NetBox.Core;

public static class DependencyInjection
{
  public static IServiceCollection AddNetBoxCore(this IServiceCollection services, IConfiguration? configuration = null)
  {
    if (configuration is not null)
    {
      services.Configure<VirtualDisplayOptions>(configuration.GetSection("VirtualDisplay"));
      services.Configure<AudioRoutingOptions>(configuration.GetSection("AudioRouting"));
      services.Configure<NetBoxInputBridgeOptions>(configuration.GetSection("NetBoxInput"));
    }
    else
    {
      services.Configure<VirtualDisplayOptions>(_ => { });
      services.Configure<AudioRoutingOptions>(_ => { });
      services.Configure<NetBoxInputBridgeOptions>(_ => { });
    }

    services.AddScoped<IAccountService, AccountService>();
    services.AddScoped<IGameLauncher, GameLauncherService>();
    services.AddScoped<IConsoleSessionManager, ConsoleSessionManager>();
    services.AddScoped<ILauncherManager, LauncherManager>();
    services.AddScoped<IDisplayManager, DisplayManager>();
    services.AddScoped<IAudioManager, AudioManager>();
    services.AddScoped<IStreamManager, StreamManager>();
    services.AddScoped<IRuntimeManager, RuntimeManager>();
    services.AddScoped<IInputManager, InputManager>();
    services.AddScoped<IGameSessionService, GameSessionService>();
    services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
    services.AddSingleton<ISessionTokenGenerator, SessionTokenGenerator>();
    services.AddSingleton<BasicVirtualDisplayProvider>();
    services.AddSingleton<IVirtualDisplayProvider, WindowsVirtualDisplayProvider>();
    services.AddSingleton<IProcessAudioPolicy, WindowsProcessAudioPolicy>();
    services.AddSingleton<IAudioDeviceRouter, WindowsAudioDeviceRouter>();
    services.AddSingleton<INetBoxInputBridge, NetBoxInputBridge>();
    return services;
  }
}
