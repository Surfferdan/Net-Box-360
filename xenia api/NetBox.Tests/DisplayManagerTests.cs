using NetBox.Core.Abstractions;
using NetBox.Core.Services;
using Xunit;

namespace NetBox.Tests;

public sealed class DisplayManagerTests
{
  [Fact]
  public async Task ProvisionAsync_DelegatesToVirtualDisplayProvider()
  {
    var provider = new RecordingVirtualDisplayProvider();
    var manager = new DisplayManager(provider);

    var displayId = await manager.ProvisionAsync("session-1", "Game Title");

    Assert.Equal("provisioned-session-1", displayId);
    Assert.Equal("session-1", provider.LastProvisionSessionId);
    Assert.Equal("Game Title", provider.LastProvisionGameTitle);
  }

  [Fact]
  public async Task ReleaseAsync_DelegatesToVirtualDisplayProvider()
  {
    var provider = new RecordingVirtualDisplayProvider();
    var manager = new DisplayManager(provider);

    await manager.ReleaseAsync("session-1", "provisioned-session-1");

    Assert.Equal("session-1", provider.LastReleaseSessionId);
    Assert.Equal("provisioned-session-1", provider.LastReleaseDisplayId);
  }

  [Theory]
  [InlineData(null)]
  [InlineData(0)]
  [InlineData(-1)]
  public async Task ResolveWindowHandleAsync_ReturnsNull_WhenProcessIdInvalid(int? processId)
  {
    var manager = new DisplayManager(new RecordingVirtualDisplayProvider());

    var handle = await manager.ResolveWindowHandleAsync(processId);

    Assert.Null(handle);
  }

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("   ")]
  public async Task PlaceWindowAsync_ReturnsWindowHandleUnchanged_WhenNoVirtualDisplayAssigned(string? virtualDisplayId)
  {
    var manager = new DisplayManager(new RecordingVirtualDisplayProvider());

    var result = await manager.PlaceWindowAsync(processId: 1234, windowHandle: "0x1", virtualDisplayId: virtualDisplayId);

    Assert.Equal("0x1", result);
  }

  [Fact]
  public async Task PlaceWindowAsync_ReturnsWindowHandleUnchanged_WhenTargetMonitorCannotBeResolved()
  {
    // On a machine/CI runner with no virtual display driver installed there is no
    // non-primary "virtual" monitor to target, so placement must safely no-op
    // instead of throwing or moving the window to an arbitrary monitor.
    var manager = new DisplayManager(new RecordingVirtualDisplayProvider());

    var result = await manager.PlaceWindowAsync(processId: 1234, windowHandle: "0x1", virtualDisplayId: "mttvdd-1-doesnotexist-guid");

    Assert.Equal("0x1", result);
  }

  private sealed class RecordingVirtualDisplayProvider : IVirtualDisplayProvider
  {
    public string? LastProvisionSessionId { get; private set; }
    public string? LastProvisionGameTitle { get; private set; }
    public string? LastReleaseSessionId { get; private set; }
    public string? LastReleaseDisplayId { get; private set; }

    public Task<string?> ProvisionDisplayAsync(string sessionId, string gameTitle, CancellationToken cancellationToken = default)
    {
      LastProvisionSessionId = sessionId;
      LastProvisionGameTitle = gameTitle;
      return Task.FromResult<string?>($"provisioned-{sessionId}");
    }

    public Task ReleaseDisplayAsync(string sessionId, string? virtualDisplayId, CancellationToken cancellationToken = default)
    {
      LastReleaseSessionId = sessionId;
      LastReleaseDisplayId = virtualDisplayId;
      return Task.CompletedTask;
    }

    public Task<string> GetDisplayStatusAsync(string? virtualDisplayId, CancellationToken cancellationToken = default)
      => Task.FromResult("active");

    public Task CleanupOrphanedDisplaysAsync(CancellationToken cancellationToken = default)
      => Task.CompletedTask;
  }
}
