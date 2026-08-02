namespace XeniaManager.Models;

public sealed record BackendEventDto(
  string Type,
  DateTimeOffset Timestamp,
  IReadOnlyDictionary<string, string> Data);
