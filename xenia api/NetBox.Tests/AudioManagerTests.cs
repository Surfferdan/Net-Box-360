using NetBox.Core.Abstractions;
using NetBox.Core.Services;
using Xunit;

namespace NetBox.Tests;

public sealed class AudioManagerTests
{
  [Fact]
  public async Task PrepareAsync_DelegatesToRouterAndSurfacesDegradedReason()
  {
    var router = new RecordingAudioDeviceRouter(new AudioRouteResult(false, "audio=Fallback", "virtual-sink-not-found"));
    var manager = new AudioManager(router, new RecordingProcessAudioPolicy(true));

    var result = await manager.PrepareAsync();

    Assert.False(result.RoutedToVirtualSink);
    Assert.Equal("audio=Fallback", result.CaptureInputDevice);
    Assert.Equal("virtual-sink-not-found", result.DegradedReason);
  }

  [Fact]
  public async Task PrepareAsync_HealthyRoute_HasNullDegradedReason()
  {
    var router = new RecordingAudioDeviceRouter(new AudioRouteResult(true, "audio=Virtual Sink"));
    var manager = new AudioManager(router, new RecordingProcessAudioPolicy(true));

    var result = await manager.PrepareAsync();

    Assert.True(result.RoutedToVirtualSink);
    Assert.Null(result.DegradedReason);
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task ApplyGameLocalMuteAsync_SurfacesMuteOutcome(bool muteSucceeds)
  {
    var manager = new AudioManager(new RecordingAudioDeviceRouter(new AudioRouteResult(false, null)), new RecordingProcessAudioPolicy(muteSucceeds));

    var result = await manager.ApplyGameLocalMuteAsync(1234);

    Assert.Equal(muteSucceeds, result);
  }

  private sealed class RecordingAudioDeviceRouter : IAudioDeviceRouter
  {
    private readonly AudioRouteResult result;

    public RecordingAudioDeviceRouter(AudioRouteResult result)
    {
      this.result = result;
    }

    public Task<AudioRouteResult> PrepareForSessionAsync(CancellationToken cancellationToken = default)
      => Task.FromResult(result);

    public Task RestoreAfterSessionAsync(CancellationToken cancellationToken = default)
      => Task.CompletedTask;

    public string? ResolvePreferredCaptureInputDevice() => result.CaptureInputDevice;
  }

  private sealed class RecordingProcessAudioPolicy : IProcessAudioPolicy
  {
    private readonly bool muteSucceeds;

    public RecordingProcessAudioPolicy(bool muteSucceeds)
    {
      this.muteSucceeds = muteSucceeds;
    }

    public Task<bool> TryApplyGameLocalMuteAsync(int? processId, CancellationToken cancellationToken = default)
      => Task.FromResult(muteSucceeds);
  }
}
