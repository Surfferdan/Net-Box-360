namespace NetBox.Core.Abstractions;

/// <summary>
/// Forwards validated browser-controller state to the NetBox-enabled Xenia
/// process (xenia_netbox.exe), which receives it via a local loopback UDP
/// input server (see src/xenia/netbox/netbox_input_server.h in the Xenia
/// Rebuild tree) and feeds it directly into NetBoxInputProvider.
///
/// This is a one-way, fire-and-forget transport: state is expected to arrive
/// at a steady rate (driven by the browser's Gamepad API poll loop), so a
/// dropped UDP datagram is simply superseded by the next one - no retry or
/// acknowledgement is needed, keeping input latency minimal.
/// </summary>
public interface INetBoxInputBridge
{
  /// <summary>
  /// Submits the latest controller state for a NetBox player slot (0-3).
  /// <paramref name="sequence"/> must be strictly increasing per player so
  /// Xenia can discard reordered/duplicated datagrams.
  /// </summary>
  void SubmitState(
    int player,
    uint sequence,
    ushort buttons,
    short leftStickX,
    short leftStickY,
    short rightStickX,
    short rightStickY,
    byte leftTrigger,
    byte rightTrigger);

  /// <summary>
  /// Releases a NetBox player slot (e.g. the browser controller
  /// disconnected, or the owning browser session ended).
  /// </summary>
  void ReleaseSlot(int player, uint sequence);
}
