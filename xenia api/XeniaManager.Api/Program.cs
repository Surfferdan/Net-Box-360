using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using NetBox.Adapters;
using NetBox.Adapters.Xenia;
using NetBox.Core;
using NetBox.Data;
using XeniaManager.Adapters;
using XeniaManager.Api.Adapters;
using XeniaManager.Api.Events;
using XeniaManager.Core;
using XeniaManager.Core.Abstractions;

var builder = WebApplication.CreateBuilder(args);
const string DevCorsPolicy = "DevCorsPolicy";

builder.Services.AddControllers();
builder.Services.Configure<XeniaDataOptions>(builder.Configuration.GetSection("XeniaData"));
builder.Services.Configure<NetBoxDatabaseOptions>(builder.Configuration.GetSection("NetBoxDatabase"));
builder.Services.Configure<XeniaApiOptions>(builder.Configuration.GetSection("XeniaApi"));
builder.Services.AddCors(options =>
{
  options.AddPolicy(DevCorsPolicy, policy =>
  {
    policy
      .SetIsOriginAllowed(origin =>
      {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
          return false;
        }

        if (!string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase))
        {
          return false;
        }

        return string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
          || string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase);
      })
      .AllowAnyHeader()
      .AllowAnyMethod()
      .AllowCredentials();
  });
});

builder.Services.AddSingleton<BackendEventHub>();
builder.Services.AddSingleton<IBackendEventSink>(sp => sp.GetRequiredService<BackendEventHub>());

builder.Services.AddXeniaManagerCore();
builder.Services.AddXeniaManagerLegacyAdapters<FileSystemLegacyFacade>();
builder.Services.AddNetBoxData();
builder.Services.AddXeniaProfileGateway();
builder.Services.AddXeniaGameCatalogGateway();
builder.Services.AddCloudMorphAdapter(builder.Configuration);
builder.Services.AddNetBoxCore(builder.Configuration);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
  var repository = scope.ServiceProvider.GetRequiredService<NetBox.Data.Repositories.INetBoxRepository>();
  await repository.InitializeAsync();
}

app.UseWebSockets();
app.UseCors(DevCorsPolicy);
app.MapControllers();

app.Map("/ws/events", async (HttpContext context, BackendEventHub hub) =>
{
  if (!context.WebSockets.IsWebSocketRequest)
  {
    context.Response.StatusCode = StatusCodes.Status400BadRequest;
    return;
  }

  using var socket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
  var (id, reader) = hub.Subscribe();
  try
  {
    await foreach (var evt in reader.ReadAllAsync(context.RequestAborted).ConfigureAwait(false))
    {
      if (socket.State != WebSocketState.Open)
      {
        break;
      }

      var payload = JsonSerializer.Serialize(evt, BackendEventHub.Json);
      var bytes = Encoding.UTF8.GetBytes(payload);
      await socket.SendAsync(bytes, WebSocketMessageType.Text, true, context.RequestAborted).ConfigureAwait(false);
    }
  }
  finally
  {
    hub.Unsubscribe(id);
  }
});

// Phase 13: browser Gamepad API -> NetBox player slot bridge. Browsers send
// small fixed-size binary frames (16 bytes: sequence, buttons, sticks,
// triggers - see web-port's GamepadInputClient.ts); the player slot is never
// trusted from the client, it is resolved server-side from the session
// token + sessionId via IInputManager.ResolvePlayerSlotAsync so a browser
// can never drive a slot it doesn't own. Validated packets are forwarded to
// the running xenia_netbox.exe over loopback UDP via INetBoxInputBridge.
app.Map("/ws/input", async (
  HttpContext context,
  NetBox.Core.Abstractions.IInputManager inputManager,
  NetBox.Core.Abstractions.INetBoxInputBridge inputBridge,
  ILogger<Program> logger) =>
{
  if (!context.WebSockets.IsWebSocketRequest)
  {
    context.Response.StatusCode = StatusCodes.Status400BadRequest;
    return;
  }

  var token = context.Request.Query["token"].ToString();
  var sessionId = context.Request.Query["sessionId"].ToString();
  int playerSlot;
  try
  {
    playerSlot = await inputManager.ResolvePlayerSlotAsync(token, sessionId, context.RequestAborted).ConfigureAwait(false);
  }
  catch (UnauthorizedAccessException)
  {
    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
    return;
  }
  catch (InvalidOperationException)
  {
    context.Response.StatusCode = StatusCodes.Status404NotFound;
    return;
  }

  using var socket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
  var buffer = new byte[64];
  uint lastSequence = 0;
  try
  {
    while (socket.State == WebSocketState.Open)
    {
      var result = await socket.ReceiveAsync(buffer, context.RequestAborted).ConfigureAwait(false);
      if (result.MessageType == WebSocketMessageType.Close)
      {
        break;
      }

      if (result.MessageType != WebSocketMessageType.Binary || result.Count < 16)
      {
        // Ignore malformed/unexpected frames rather than tearing down the
        // connection - a stray text ping/keepalive shouldn't kill input.
        continue;
      }

      var sequence = BitConverter.ToUInt32(buffer, 0);
      if (unchecked((int)(sequence - lastSequence)) <= 0 && lastSequence != 0)
      {
        // Reordered/duplicated frame - superseded by a newer one already
        // forwarded, drop it.
        continue;
      }
      lastSequence = sequence;

      var buttons = BitConverter.ToUInt16(buffer, 4);
      var leftStickX = BitConverter.ToInt16(buffer, 6);
      var leftStickY = BitConverter.ToInt16(buffer, 8);
      var rightStickX = BitConverter.ToInt16(buffer, 10);
      var rightStickY = BitConverter.ToInt16(buffer, 12);
      var leftTrigger = buffer[14];
      var rightTrigger = buffer[15];

      inputBridge.SubmitState(playerSlot, sequence, buttons, leftStickX, leftStickY, rightStickX, rightStickY, leftTrigger, rightTrigger);
    }
  }
  catch (OperationCanceledException)
  {
    // Client/browser tab closed - fall through to slot release below.
  }
  catch (WebSocketException ex)
  {
    logger.LogDebug(ex, "NetBox input socket for player {PlayerSlot} closed unexpectedly.", playerSlot);
  }
  finally
  {
    inputBridge.ReleaseSlot(playerSlot, unchecked(lastSequence + 1));
  }
});

app.Run();

public partial class Program;
