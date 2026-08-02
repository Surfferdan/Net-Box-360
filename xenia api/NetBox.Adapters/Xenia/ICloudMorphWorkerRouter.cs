namespace NetBox.Adapters.Xenia;

public interface ICloudMorphWorkerRouter
{
  string? AcquireWorker(string sessionId);
  void ReleaseWorker(string sessionId);
}
