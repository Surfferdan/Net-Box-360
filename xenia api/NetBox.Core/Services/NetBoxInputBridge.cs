using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetBox.Core.Abstractions;

namespace NetBox.Core.Services;

/// <summary>
/// UDP client implementation of <see cref="INetBoxInputBridge"/>. Wire
/// format is a fixed 24-byte little-endian struct matching
/// xe::netbox::NetBoxInputPacket exactly (see
/// src/xenia/netbox/netbox_input_server.h) - loopback-only, no
/// serialization library, no allocation beyond the per-call buffer.
/// </summary>
public sealed class NetBoxInputBridge : INetBoxInputBridge, IDisposable
{
  private const uint Magic = 0x50494e42; // "NBIP" little-endian.
  private const byte Version = 1;
  private const byte FlagDisconnect = 1 << 0;
  private const int PacketSize = 24;

  private readonly UdpClient udpClient = new();
  private readonly IPEndPoint endpoint;
  private readonly ILogger<NetBoxInputBridge> logger;

  public NetBoxInputBridge(IOptions<NetBoxInputBridgeOptions> options, ILogger<NetBoxInputBridge> logger)
  {
    this.logger = logger;
    endpoint = new IPEndPoint(IPAddress.Parse(options.Value.Host), options.Value.Port);
  }

  public void SubmitState(
    int player,
    uint sequence,
    ushort buttons,
    short leftStickX,
    short leftStickY,
    short rightStickX,
    short rightStickY,
    byte leftTrigger,
    byte rightTrigger)
  {
    Send(player, sequence, flags: 0, buttons, leftStickX, leftStickY, rightStickX, rightStickY, leftTrigger, rightTrigger);
  }

  public void ReleaseSlot(int player, uint sequence)
  {
    Send(player, sequence, FlagDisconnect, buttons: 0, 0, 0, 0, 0, 0, 0);
  }

  private void Send(
    int player,
    uint sequence,
    byte flags,
    ushort buttons,
    short leftStickX,
    short leftStickY,
    short rightStickX,
    short rightStickY,
    byte leftTrigger,
    byte rightTrigger)
  {
    if (player is < 0 or > 3)
    {
      return;
    }

    Span<byte> packet = stackalloc byte[PacketSize];
    BitConverter.TryWriteBytes(packet[0..4], Magic);
    packet[4] = Version;
    packet[5] = (byte)player;
    packet[6] = flags;
    packet[7] = 0; // reserved
    BitConverter.TryWriteBytes(packet[8..12], sequence);
    BitConverter.TryWriteBytes(packet[12..14], buttons);
    BitConverter.TryWriteBytes(packet[14..16], leftStickX);
    BitConverter.TryWriteBytes(packet[16..18], leftStickY);
    BitConverter.TryWriteBytes(packet[18..20], rightStickX);
    BitConverter.TryWriteBytes(packet[20..22], rightStickY);
    packet[22] = leftTrigger;
    packet[23] = rightTrigger;

    try
    {
      udpClient.Send(packet, endpoint);
    }
    catch (SocketException ex)
    {
      // Fire-and-forget by design - a dropped datagram is superseded by the
      // next poll tick, so we only log at debug level to avoid flooding
      // logs if Xenia isn't running.
      logger.LogDebug(ex, "Failed to send NetBox input packet for player {Player}.", player);
    }
  }

  public void Dispose() => udpClient.Dispose();
}
