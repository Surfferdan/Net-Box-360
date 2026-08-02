namespace XeniaManager.Models;

public sealed record EmulatorConfigDto(IReadOnlyDictionary<string, string> Values);

public sealed record UpdateConfigRequest(IReadOnlyDictionary<string, string> Values);
